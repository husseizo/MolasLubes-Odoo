using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Integrations.TantivyScraper.Dtos;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

public class TantivyScrapeResultSyncService
{
    private readonly AutoHubDbContext                         _db;
    private readonly ILogger<TantivyScrapeResultSyncService> _logger;

    public TantivyScrapeResultSyncService(
        AutoHubDbContext db,
        ILogger<TantivyScrapeResultSyncService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // =====================================================
    // PENDING QUEUE
    // =====================================================

    /// <summary>
    /// Returns up to <paramref name="batchSize"/> TantivyParts that have not yet
    /// been successfully scraped (no SCRAPED or NO_MATCH record exists).
    /// ERROR items are re-queued for retry.
    /// </summary>
    public async Task<List<TantivySeedDto>> GetPendingAsync(int batchSize, CancellationToken ct)
    {
        var doneSet = await _db.TantivyScraped
            .Where(s => s.ScrapeStatus == "SCRAPED" || s.ScrapeStatus == "NO_MATCH")
            .Select(s => s.ItemCode)
            .ToHashSetAsync(ct);

        return await _db.TantivyParts
            .Where(p => p.ArticleNo != null && !doneSet.Contains(p.ItemCode))
            .OrderBy(p => p.MdlTest)
            .ThenBy(p => p.ItemCode)
            .Take(batchSize)
            .Select(p => new TantivySeedDto
            {
                ItemCode   = p.ItemCode,
                ItemName   = p.ItemName,
                Brand      = p.MdlTest ?? "VIKA",
                ArticleNo  = p.ArticleNo,
                EngineCode = p.EngineCode
            })
            .ToListAsync(ct);
    }

    // =====================================================
    // SAVE SCRAPED RESULT
    // =====================================================

    public async Task SaveScrapedAsync(
        TantivyScrapedDto dto,
        string brand,
        string? articleNo,
        CancellationToken ct)
    {
        var now      = DateTime.UtcNow;
        var existing = await _db.TantivyScraped
            .FirstOrDefaultAsync(s => s.ItemCode == dto.ItemCode, ct);

        if (existing == null)
        {
            _db.TantivyScraped.Add(new NeonTantivyScraped
            {
                ItemCode         = dto.ItemCode,
                ArticleNo        = articleNo,
                Brand            = brand,
                PartName         = dto.PartName,
                Specifications   = dto.Specifications,
                ReferenceNumbers = dto.ReferenceNumbers,
                Applications     = dto.Applications,
                ProductUrl       = dto.ProductUrl,
                ImageUrl         = dto.ImageUrl,
                ScrapeStatus     = "SCRAPED",
                ScrapedAt        = now,
                LastSeedAt       = now
            });
        }
        else
        {
            existing.ArticleNo        = articleNo;
            existing.Brand            = brand;
            existing.PartName         = dto.PartName;
            existing.Specifications   = dto.Specifications;
            existing.ReferenceNumbers = dto.ReferenceNumbers;
            existing.Applications     = dto.Applications;
            existing.ProductUrl       = dto.ProductUrl;
            existing.ImageUrl         = dto.ImageUrl;
            existing.ScrapeStatus     = "SCRAPED";
            existing.ScrapeError      = null;
            existing.ScrapedAt        = now;
            existing.LastSeedAt       = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    // =====================================================
    // MARK STATUS (NO_MATCH / ERROR)
    // =====================================================

    public async Task MarkStatusAsync(
        string itemCode,
        string status,
        string? error,
        string? brand,
        string? articleNo,
        CancellationToken ct)
    {
        var now      = DateTime.UtcNow;
        var existing = await _db.TantivyScraped
            .FirstOrDefaultAsync(s => s.ItemCode == itemCode, ct);

        if (existing == null)
        {
            _db.TantivyScraped.Add(new NeonTantivyScraped
            {
                ItemCode     = itemCode,
                ArticleNo    = articleNo,
                Brand        = brand,
                ScrapeStatus = status,
                ScrapeError  = error,
                LastSeedAt   = now
            });
        }
        else
        {
            existing.ScrapeStatus = status;
            existing.ScrapeError  = error;
            existing.LastSeedAt   = now;
        }

        await _db.SaveChangesAsync(ct);
    }

    // =====================================================
    // STATS
    // =====================================================

    public async Task<(int scraped, int noMatch, int error, int pending)> GetStatsAsync(
        CancellationToken ct)
    {
        var total   = await _db.TantivyParts.CountAsync(p => p.ArticleNo != null, ct);
        var scraped = await _db.TantivyScraped.CountAsync(s => s.ScrapeStatus == "SCRAPED",  ct);
        var noMatch = await _db.TantivyScraped.CountAsync(s => s.ScrapeStatus == "NO_MATCH", ct);
        var error   = await _db.TantivyScraped.CountAsync(s => s.ScrapeStatus == "ERROR",    ct);

        return (scraped, noMatch, error, pending: total - scraped - noMatch - error);
    }

    // =====================================================
    // RESET ERRORS (re-queue for retry)
    // =====================================================

    public async Task<int> ResetErrorsAsync(CancellationToken ct)
    {
        var errors = await _db.TantivyScraped
            .Where(s => s.ScrapeStatus == "ERROR")
            .ToListAsync(ct);

        foreach (var row in errors)
        {
            row.ScrapeStatus = "PENDING";
            row.ScrapeError  = null;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("TantivyScrapeResultSyncService: reset {Count} ERROR rows to PENDING", errors.Count);
        return errors.Count;
    }

    // =====================================================
    // SYNC ARTICLE NUMBERS (TAN Numbers)
    // Copies ArticleNo from Tantivy_parts → neon_tantivy_scraped.
    // Creates a PENDING seed row when none exists yet so the
    // item is automatically picked up on the next scrape run.
    // Updates article_no on existing rows where the value is
    // NULL, empty, or stale (differs from TantivyParts).
    // =====================================================

    public async Task<(int seeded, int updated)> SyncArticleNumbersFromPartsAsync(
        CancellationToken ct = default)
    {
        var parts = await _db.TantivyParts
            .Where(p => p.ArticleNo != null && p.ArticleNo != "")
            .ToListAsync(ct);

        if (parts.Count == 0)
        {
            _logger.LogWarning("SyncArticleNumbers: Tantivy_parts has no rows with ArticleNo — nothing to sync");
            return (0, 0);
        }

        var itemCodes    = parts.Select(p => p.ItemCode).ToList();
        var existingRows = await _db.TantivyScraped
            .Where(s => itemCodes.Contains(s.ItemCode))
            .ToDictionaryAsync(s => s.ItemCode, ct);

        var now    = DateTime.UtcNow;
        int seeded = 0, updated = 0;

        foreach (var part in parts)
        {
            var tan = part.ArticleNo!;

            if (existingRows.TryGetValue(part.ItemCode, out var row))
            {
                if (string.IsNullOrEmpty(row.ArticleNo) || row.ArticleNo != tan)
                {
                    row.ArticleNo  = tan;
                    row.LastSeedAt = now;
                    updated++;
                }
            }
            else
            {
                _db.TantivyScraped.Add(new NeonTantivyScraped
                {
                    ItemCode     = part.ItemCode,
                    ArticleNo    = tan,
                    Brand        = part.MdlTest ?? "VIKA",
                    ScrapeStatus = "PENDING",
                    LastSeedAt   = now
                });
                seeded++;
            }
        }

        if (seeded > 0 || updated > 0)
            await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SyncArticleNumbers: seeded={Seeded} new PENDING rows, updated={Updated} existing article_no values",
            seeded, updated);

        return (seeded, updated);
    }
}
