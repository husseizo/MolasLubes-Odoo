using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.Germax.Dtos;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class GermaxCacheSyncService
{
    private readonly Live2021CacheDbContext _db;
    private readonly ILogger<GermaxCacheSyncService> _logger;

    public GermaxCacheSyncService(
        Live2021CacheDbContext db,
        ILogger<GermaxCacheSyncService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Upserts seed rows from SAP into CacheGermaxProducts.
    /// When isFullSync is true, also marks any row not present in the
    /// incoming seed as IsActive=false (item was frozen or deleted in SAP).
    /// Never overwrites ScrapeStatus on a row that is already SCRAPED.
    /// </summary>
    public async Task<(int Inserted, int Updated, int Deactivated)> UpsertSeedAsync(
        IReadOnlyList<GermaxSeedDto> seeds,
        bool isFullSync,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "GermaxCacheSyncService: upsert started | Count={Count} | FullSync={FullSync}",
            seeds.Count, isFullSync);

        var now      = DateTime.UtcNow;
        var inserted = 0;
        var updated  = 0;

        // Pull all existing rows into a dictionary for O(1) lookup.
        // Seeds are expected to be in the thousands at most, so this is safe.
        var existing = await _db.GermaxProducts
            .ToDictionaryAsync(x => x.ItemCode, ct);

        var incomingCodes = new HashSet<string>(
            seeds.Select(s => s.ItemCode), StringComparer.OrdinalIgnoreCase);

        foreach (var seed in seeds)
        {
            if (existing.TryGetValue(seed.ItemCode, out var row))
            {
                // Update seed fields only — never overwrite enrichment columns
                row.ItemName      = seed.ItemName;
                row.ItemGroupName = seed.ItemGroupName;
                row.EngineCode    = seed.EngineCode;
                row.IsActive      = true;
                row.LastSapSeedAt = now;

                // Reset to PENDING only if it has never been attempted
                // (i.e. status is null). Leave SCRAPED / NO_MATCH / ERROR alone.
                if (row.ScrapeStatus == null)
                    row.ScrapeStatus = "PENDING";

                updated++;
            }
            else
            {
                _db.GermaxProducts.Add(new CacheGermaxProduct
                {
                    ItemCode      = seed.ItemCode,
                    ItemName      = seed.ItemName,
                    ItemGroupName = seed.ItemGroupName,
                    EngineCode    = seed.EngineCode,
                    IsActive      = true,
                    LastSapSeedAt = now,
                    ScrapeStatus  = "PENDING"
                });
                inserted++;
            }
        }

        // On a full sync, deactivate rows that SAP no longer returns
        var deactivated = 0;
        if (isFullSync)
        {
            foreach (var row in existing.Values)
            {
                if (row.IsActive && !incomingCodes.Contains(row.ItemCode))
                {
                    row.IsActive = false;
                    deactivated++;
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "GermaxCacheSyncService: upsert completed | Inserted={Inserted} | Updated={Updated} | Deactivated={Deactivated}",
            inserted, updated, deactivated);

        return (inserted, updated, deactivated);
    }

    /// <summary>
    /// Returns the latest LastSapSeedAt across all rows, or null if the
    /// table is empty. Used by the job to decide full vs delta read.
    /// </summary>
    public async Task<DateTime?> GetWatermarkAsync(CancellationToken ct = default)
    {
        if (!await _db.GermaxProducts.AnyAsync(ct))
            return null;

        return await _db.GermaxProducts
            .MaxAsync(x => (DateTime?)x.LastSapSeedAt, ct);
    }

    /// <summary>
    /// Returns up to <paramref name="batchSize"/> active PENDING rows as seed DTOs,
    /// ordered oldest-first so the queue drains in a stable order.
    /// </summary>
    public async Task<List<GermaxSeedDto>> GetPendingAsync(
        int batchSize,
        CancellationToken ct = default)
    {
        return await _db.GermaxProducts
            .Where(x => x.IsActive && x.ScrapeStatus == "PENDING")
            .OrderBy(x => x.LastSapSeedAt)
            .Take(batchSize)
            .Select(x => new GermaxSeedDto
            {
                ItemCode      = x.ItemCode,
                ItemName      = x.ItemName,
                EngineCode    = x.EngineCode,
                ItemGroupCode = string.Empty,        // not persisted on cache row
                ItemGroupName = x.ItemGroupName ?? string.Empty
            })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns up to <paramref name="batchSize"/> active ERROR or NO_MATCH rows ordered
    /// oldest-failure-first.  When <paramref name="maxAgeDays"/> is provided only rows
    /// whose <c>ScrapedAt</c> falls within that window are returned; pass <c>null</c>
    /// to include all failures regardless of age (used by manual admin triggers).
    /// </summary>
    public async Task<List<GermaxSeedDto>> GetRetryableAsync(
        int batchSize,
        int? maxAgeDays = 7,
        CancellationToken ct = default)
    {
        var query = _db.GermaxProducts
            .Where(x => x.IsActive
                && (x.ScrapeStatus == "ERROR" || x.ScrapeStatus == "NO_MATCH")
                && x.ScrapedAt != null);

        if (maxAgeDays.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays.Value);
            query = query.Where(x => x.ScrapedAt >= cutoff);
        }

        return await query
            .OrderBy(x => x.ScrapedAt)
            .Take(batchSize)
            .Select(x => new GermaxSeedDto
            {
                ItemCode      = x.ItemCode,
                ItemName      = x.ItemName,
                EngineCode    = x.EngineCode,
                ItemGroupCode = string.Empty,
                ItemGroupName = x.ItemGroupName ?? string.Empty
            })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Writes an enrichment success back to the cache row.
    /// Sets ScrapeStatus=SCRAPED, populates all product fields, records ScrapedAt.
    /// </summary>
    public async Task EnrichAsync(
        GermaxProductDto dto,
        CancellationToken ct = default)
    {
        var row = await _db.GermaxProducts
            .FindAsync(new object?[] { dto.ItemCode }, ct);

        if (row == null)
        {
            _logger.LogWarning(
                "GermaxCacheSyncService.EnrichAsync: ItemCode={Code} not found — skipped",
                dto.ItemCode);
            return;
        }

        row.GermaxArticleNumber = dto.GermaxArticleNumber;
        row.OemPartNumber       = dto.OemPartNumber;
        row.PartsCatalog        = dto.PartsCatalog;
        row.FitForAuto          = dto.FitForAuto;
        row.Description         = dto.Description;
        row.ImageUrl            = dto.ImageUrl;
        row.AllImageUrls        = dto.AllImageUrls;
        row.ProductUrl          = dto.ProductUrl;
        row.MatchMethod         = dto.MatchMethod;
        row.MatchScore          = dto.MatchScore;
        row.ScrapedAt           = DateTime.UtcNow;
        row.ScrapeStatus        = "SCRAPED";
        row.ScrapeError         = null;

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "GermaxCacheSyncService.EnrichAsync: saved | ItemCode={Code} | Method={Method} | Score={Score:F1}",
            dto.ItemCode, dto.MatchMethod, dto.MatchScore);
    }

    /// <summary>
    /// Records a terminal non-success outcome (NO_MATCH or ERROR) on a cache row.
    /// <paramref name="error"/> is truncated to 1 000 characters to fit the column.
    /// </summary>
    public async Task MarkScrapeResultAsync(
        string itemCode,
        string status,
        string? error,
        CancellationToken ct = default)
    {
        var row = await _db.GermaxProducts
            .FindAsync(new object?[] { itemCode }, ct);

        if (row == null) return;

        row.ScrapeStatus = status;
        row.ScrapeError  = error is { Length: > 1000 } ? error[..1000] : error;
        row.ScrapedAt    = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }
}
