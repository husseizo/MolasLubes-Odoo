using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Sync;

/// <summary>
/// Upserts scraped Liqui-Moly product data into the local SQL Server cache
/// (<see cref="MolasCacheDbContext"/> → <c>CacheLiquiMolyProducts</c>).
/// </summary>
public class LiquiMolyCacheSyncService
{
    private readonly MolasCacheDbContext                    _db;
    private readonly ILogger<LiquiMolyCacheSyncService>    _logger;

    public LiquiMolyCacheSyncService(
        MolasCacheDbContext db,
        ILogger<LiquiMolyCacheSyncService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // =====================================================
    // UPSERT — SCRAPE → CACHE
    // =====================================================
    public async Task UpsertAsync(IEnumerable<LiquiMolyProductDto> incoming)
    {
        _logger.LogInformation("[LiquiMoly][Cache] Upsert started");

        var incomingList = incoming.ToList();
        if (incomingList.Count == 0)
        {
            _logger.LogWarning("[LiquiMoly][Cache] No products to upsert");
            return;
        }

        var articleNumbers = incomingList
            .Select(p => p.ArticleNumber)
            .Distinct()
            .ToList();

        var existing = await _db.CacheLiquiMolyProducts
            .Where(x => articleNumbers.Contains(x.ArticleNumber))
            .ToDictionaryAsync(x => x.ArticleNumber);

        int inserted = 0, updated = 0;
        var now = DateTime.UtcNow;

        foreach (var dto in incomingList)
        {
            if (existing.TryGetValue(dto.ArticleNumber, out var entity))
            {
                entity.Name          = dto.Name;
                entity.Category      = dto.Category;
                entity.SubCategory   = dto.SubCategory;
                entity.Description   = dto.Description;
                entity.SpecGrade     = dto.SpecGrade;
                entity.PackagingSize = dto.PackagingSize;
                entity.ImageUrl      = dto.ImageUrl;
                entity.ProductUrl    = dto.ProductUrl;
                entity.IsActive      = true;
                entity.ScrapedAt     = now;
                updated++;
            }
            else
            {
                _db.CacheLiquiMolyProducts.Add(new CacheLiquiMolyProduct
                {
                    ArticleNumber = dto.ArticleNumber,
                    Name          = dto.Name,
                    Category      = dto.Category,
                    SubCategory   = dto.SubCategory,
                    Description   = dto.Description,
                    SpecGrade     = dto.SpecGrade,
                    PackagingSize = dto.PackagingSize,
                    ImageUrl      = dto.ImageUrl,
                    ProductUrl    = dto.ProductUrl,
                    IsActive      = true,
                    ScrapedAt     = now,
                });
                inserted++;
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "[LiquiMoly][Cache] Upsert done | Inserted={Inserted} | Updated={Updated}",
            inserted, updated);
    }

    // =====================================================
    // DEACTIVATE — mark products not seen in latest scrape
    // =====================================================
    public async Task DeactivateStaleAsync(IEnumerable<string> scrapedArticleNumbers)
    {
        var scraped = scrapedArticleNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = await _db.CacheLiquiMolyProducts
            .Where(x => x.IsActive && !scraped.Contains(x.ArticleNumber))
            .ToListAsync();

        if (stale.Count == 0) return;

        foreach (var p in stale)
            p.IsActive = false;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "[LiquiMoly][Cache] Deactivated {Count} stale products", stale.Count);
    }
}
