using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

/// <summary>
/// Replicates enriched Germax product rows from Live2021CacheDb (SQL Server)
/// to the AutoHub Neon database (PostgreSQL).
///
/// Only active SCRAPED rows are replicated.  Any Neon row whose ItemCode is
/// no longer in the active-SCRAPED set is deactivated rather than deleted,
/// so historical records are preserved.
/// </summary>
public class GermaxAutoHubSyncService
{
    private readonly Live2021CacheDbContext _cacheDb;
    private readonly AutoHubDbContext       _autoHubDb;
    private readonly ILogger<GermaxAutoHubSyncService> _logger;

    public GermaxAutoHubSyncService(
        Live2021CacheDbContext cacheDb,
        AutoHubDbContext autoHubDb,
        ILogger<GermaxAutoHubSyncService> logger)
    {
        _cacheDb   = cacheDb;
        _autoHubDb = autoHubDb;
        _logger    = logger;
    }

    /// <summary>
    /// Full upsert: copies all active SCRAPED rows from the cache into Neon.
    /// Deactivates Neon rows that are no longer in the active-SCRAPED set.
    /// </summary>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("GermaxAutoHubSyncService: sync started");

        // 1. Source: all active SCRAPED rows in the cache
        var source = await _cacheDb.GermaxProducts
            .Where(x => x.IsActive && x.ScrapeStatus == "SCRAPED")
            .ToListAsync(ct);

        if (source.Count == 0)
        {
            _logger.LogInformation(
                "GermaxAutoHubSyncService: no active SCRAPED rows in cache — nothing to sync");
            return;
        }

        var sourceCodes = new HashSet<string>(
            source.Select(x => x.ItemCode), StringComparer.OrdinalIgnoreCase);

        // 2. Load all existing Neon rows for O(1) lookup (and deactivation check)
        var existing = await _autoHubDb.GermaxProducts
            .ToDictionaryAsync(x => x.ItemCode, ct);

        var upserted    = 0;
        var deactivated = 0;

        // 3. Upsert each source row
        foreach (var row in source)
        {
            if (existing.TryGetValue(row.ItemCode, out var neon))
            {
                neon.ItemName            = row.ItemName;
                neon.ItemGroupName       = row.ItemGroupName;
                neon.EngineCode          = row.EngineCode;
                neon.GermaxArticleNumber = row.GermaxArticleNumber;
                neon.OemPartNumber       = row.OemPartNumber;
                neon.FitForAuto          = row.FitForAuto;
                neon.Description         = row.Description;
                neon.ImageUrl            = row.ImageUrl;
                neon.AllImageUrls        = row.AllImageUrls;
                neon.ProductUrl          = row.ProductUrl;
                neon.MatchMethod         = row.MatchMethod;
                neon.MatchScore          = row.MatchScore;
                neon.ScrapedAt           = row.ScrapedAt;
                neon.LastSapSeedAt       = row.LastSapSeedAt;
                neon.IsActive            = true;
                neon.ScrapeStatus        = row.ScrapeStatus;
                neon.ScrapeError         = row.ScrapeError;
            }
            else
            {
                _autoHubDb.GermaxProducts.Add(new NeonGermaxProduct
                {
                    ItemCode             = row.ItemCode,
                    ItemName             = row.ItemName,
                    ItemGroupName        = row.ItemGroupName,
                    EngineCode           = row.EngineCode,
                    GermaxArticleNumber  = row.GermaxArticleNumber,
                    OemPartNumber        = row.OemPartNumber,
                    FitForAuto           = row.FitForAuto,
                    Description          = row.Description,
                    ImageUrl             = row.ImageUrl,
                    AllImageUrls         = row.AllImageUrls,
                    ProductUrl           = row.ProductUrl,
                    MatchMethod          = row.MatchMethod,
                    MatchScore           = row.MatchScore,
                    ScrapedAt            = row.ScrapedAt,
                    LastSapSeedAt        = row.LastSapSeedAt,
                    IsActive             = true,
                    ScrapeStatus         = row.ScrapeStatus,
                    ScrapeError          = row.ScrapeError
                });
            }

            upserted++;
        }

        // 4. Deactivate Neon rows no longer in the active-SCRAPED set
        foreach (var neon in existing.Values)
        {
            if (neon.IsActive && !sourceCodes.Contains(neon.ItemCode))
            {
                neon.IsActive = false;
                deactivated++;
            }
        }

        await _autoHubDb.SaveChangesAsync(ct);

        _logger.LogInformation(
            "GermaxAutoHubSyncService: completed | Upserted={Upserted} | Deactivated={Deactivated}",
            upserted, deactivated);
    }
}
