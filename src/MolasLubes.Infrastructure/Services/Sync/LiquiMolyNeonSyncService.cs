using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

/// <summary>
/// Upserts scraped Liqui-Moly product data into Neon PostgreSQL
/// (<see cref="NeonDbContext"/> → <c>NeonLiquiMolyProducts</c>).
/// </summary>
public class LiquiMolyNeonSyncService
{
    private readonly MolasCacheDbContext                    _cache;
    private readonly NeonDbContext                          _neon;
    private readonly ILogger<LiquiMolyNeonSyncService>     _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = false,
    };

    public LiquiMolyNeonSyncService(
        MolasCacheDbContext cache,
        NeonDbContext neon,
        ILogger<LiquiMolyNeonSyncService> logger)
    {
        _cache  = cache;
        _neon   = neon;
        _logger = logger;
    }

    // =====================================================
    // FULL SYNC — CACHE → NEON
    // =====================================================
    public async Task SyncFromCacheAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[LiquiMoly][Neon] Cache sync started");

        var source = await _cache.CacheLiquiMolyProducts
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var sourceArticles = new HashSet<string>(
            source.Select(x => x.ArticleNumber),
            StringComparer.OrdinalIgnoreCase);

        var existing = await _neon.LiquiMolyProducts
            .ToDictionaryAsync(x => x.ArticleNumber, ct);

        if (source.Count == 0 && existing.Count == 0)
        {
            _logger.LogInformation("[LiquiMoly][Neon] Cache sync found nothing to replicate");
            return;
        }

        var strategy = _neon.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neon.Database.BeginTransactionAsync(ct);

            var upserted = 0;
            var deactivated = 0;

            foreach (var row in source)
            {
                if (existing.TryGetValue(row.ArticleNumber, out var neon))
                {
                    MapFromCache(row, neon);
                }
                else
                {
                    _neon.LiquiMolyProducts.Add(BuildEntityFromCache(row));
                }

                upserted++;
            }

            foreach (var neon in existing.Values)
            {
                if (neon.IsActive && !sourceArticles.Contains(neon.ArticleNumber))
                {
                    neon.IsActive = false;
                    deactivated++;
                }
            }

            await _neon.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "[LiquiMoly][Neon] Cache sync completed | Upserted={Upserted} | Deactivated={Deactivated}",
                upserted, deactivated);
        });
    }

    // =====================================================
    // UPSERT — SCRAPE → NEON
    // =====================================================
    public async Task UpsertAsync(IEnumerable<LiquiMolyProductDto> incoming)
    {
        _logger.LogInformation("[LiquiMoly][Neon] Upsert started");

        var incomingList = incoming.ToList();
        if (incomingList.Count == 0)
        {
            _logger.LogWarning("[LiquiMoly][Neon] No products to upsert");
            return;
        }

        var strategy = _neon.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _neon.Database.BeginTransactionAsync();

            var articleNumbers = incomingList
                .Select(p => p.ArticleNumber)
                .Distinct()
                .ToList();

            var existing = await _neon.LiquiMolyProducts
                .Where(x => articleNumbers.Contains(x.ArticleNumber))
                .ToDictionaryAsync(x => x.ArticleNumber);

            int inserted = 0, updated = 0;
            var now = DateTime.UtcNow;

            foreach (var dto in incomingList)
            {
                if (existing.TryGetValue(dto.ArticleNumber, out var entity))
                {
                    MapToEntity(dto, entity, now);
                    updated++;
                }
                else
                {
                    var newEntity = new NeonLiquiMolyProduct
                    {
                        ArticleNumber = dto.ArticleNumber,
                        IsActive      = true,
                    };
                    MapToEntity(dto, newEntity, now);
                    _neon.LiquiMolyProducts.Add(newEntity);
                    inserted++;
                }
            }

            await _neon.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "[LiquiMoly][Neon] Upsert done | Inserted={Inserted} | Updated={Updated}",
                inserted, updated);
        });
    }

    // =====================================================
    // DEACTIVATE — mark products not seen in latest scrape
    // =====================================================
    public async Task DeactivateStaleAsync(IEnumerable<string> scrapedArticleNumbers)
    {
        var strategy = _neon.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            var scraped = scrapedArticleNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var stale = await _neon.LiquiMolyProducts
                .Where(x => x.IsActive && !scraped.Contains(x.ArticleNumber))
                .ToListAsync();

            if (stale.Count == 0) return;

            foreach (var p in stale)
                p.IsActive = false;

            await _neon.SaveChangesAsync();

            _logger.LogInformation(
                "[LiquiMoly][Neon] Deactivated {Count} stale products", stale.Count);
        });
    }

    // =====================================================
    // MAPPING HELPER
    // =====================================================
    private static void MapToEntity(LiquiMolyProductDto dto, NeonLiquiMolyProduct entity, DateTime now)
    {
        entity.Name                  = dto.Name;
        entity.Category              = dto.Category;
        entity.SubCategory           = dto.SubCategory;
        entity.Description           = dto.Description;
        entity.SpecGrade             = dto.SpecGrade;
        entity.PackagingSize         = dto.PackagingSize;
        entity.Liter                 = dto.Liter;
        entity.ImageUrl              = dto.ImageUrl;
        entity.ProductUrl            = dto.ProductUrl;
        entity.IsActive              = true;
        entity.ScrapedAt             = now;

        // Serialise list/dict fields to JSON strings
        entity.AllPackagingSizes     = dto.AllPackagingSizes.Count > 0
            ? JsonSerializer.Serialize(dto.AllPackagingSizes, _json)
            : null;

        entity.AllImageUrls          = dto.AllImageUrls.Count > 0
            ? JsonSerializer.Serialize(dto.AllImageUrls, _json)
            : null;

        entity.Approvals             = dto.Approvals.Count > 0
            ? JsonSerializer.Serialize(dto.Approvals, _json)
            : null;

        entity.Specifications        = dto.Specifications.Count > 0
            ? JsonSerializer.Serialize(dto.Specifications, _json)
            : null;

        entity.OverviewProperties    = dto.OverviewProperties.Count > 0
            ? JsonSerializer.Serialize(dto.OverviewProperties, _json)
            : null;

        entity.ProductInfoPdfUrl     = dto.ProductInfoPdfUrl;
        entity.SafetyDataSheetPdfUrl = dto.SafetyDataSheetPdfUrl;
    }

    private static NeonLiquiMolyProduct BuildEntityFromCache(CacheLiquiMolyProduct row)
    {
        var entity = new NeonLiquiMolyProduct
        {
            ArticleNumber = row.ArticleNumber,
            Name          = row.Name,
        };

        MapFromCache(row, entity);
        return entity;
    }

    private static void MapFromCache(CacheLiquiMolyProduct row, NeonLiquiMolyProduct entity)
    {
        entity.Name                  = row.Name;
        entity.Category              = row.Category;
        entity.SubCategory           = row.SubCategory;
        entity.Description           = row.Description;
        entity.SpecGrade             = row.SpecGrade;
        entity.PackagingSize         = row.PackagingSize;
        entity.Liter                 = row.Liter;
        entity.ImageUrl              = row.ImageUrl;
        entity.ProductUrl            = row.ProductUrl;
        entity.IsActive              = row.IsActive;
        entity.ScrapedAt             = row.ScrapedAt;
        entity.AllPackagingSizes     = row.AllPackagingSizes;
        entity.AllImageUrls          = row.AllImageUrls;
        entity.Approvals             = row.Approvals;
        entity.Specifications        = row.Specifications;
        entity.OverviewProperties    = row.OverviewProperties;
        entity.ProductInfoPdfUrl     = row.ProductInfoPdfUrl;
        entity.SafetyDataSheetPdfUrl = row.SafetyDataSheetPdfUrl;
    }
}
