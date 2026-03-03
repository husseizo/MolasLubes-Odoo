using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Services.Sync;
using MolasLubes.Infrastructure.Scheduling;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Quartz job that:
///   1. Reads distinct active ItemCodes from CacheProducts.
///   2. Passes them to <see cref="LiquiMolyProductScraperService.ScrapeByArticleNumbersAsync"/>
///      which uses a 2-phase approach:
///        Phase 1 — scan category listing pages to locate the matching product stubs
///                  (no detail-page visits, just pagination + filtering).
///        Phase 2 — visit each matched product's detail page in shuffled order
///                  with human-like variable delays.
///   3. Upserts found products into SQL Server cache via LiquiMolyCacheSyncService.
///   4. Upserts found products into Neon PostgreSQL via LiquiMolyNeonSyncService.
///   5. Marks products no longer found on the website as inactive in both stores.
///
/// Scheduled (default) every 24 hours via Program.cs.
/// Can also be triggered manually via POST /api/admin/liquimoly/scrape.
/// </summary>
[DisallowConcurrentExecution]
public class LiquiMolyProductScrapeJob : IJob
{
    private readonly IServiceScopeFactory             _scopeFactory;
    private readonly ILogger<LiquiMolyProductScrapeJob> _logger;

    public LiquiMolyProductScrapeJob(
        IServiceScopeFactory scopeFactory,
        ILogger<LiquiMolyProductScrapeJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[LiquiMoly] Product scrape job started");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var cacheDb   = scope.ServiceProvider.GetRequiredService<MolasCacheDbContext>();
            var scraper   = scope.ServiceProvider.GetRequiredService<LiquiMolyProductScraperService>();
            var cacheSync = scope.ServiceProvider.GetRequiredService<LiquiMolyCacheSyncService>();
            var neonSync  = scope.ServiceProvider.GetRequiredService<LiquiMolyNeonSyncService>();

            // ── Step 1: Distinct active ItemCodes from CacheProducts ─────────
            var articleNumbers = await cacheDb.CacheProducts
                .Where(p => p.IsActive)
                .Select(p => p.ItemCode)
                .Distinct()
                .ToListAsync(context.CancellationToken);

            _logger.LogInformation(
                "[LiquiMoly] Resolved {Count} distinct article numbers from CacheProducts",
                articleNumbers.Count);

            if (articleNumbers.Count == 0)
            {
                _logger.LogWarning("[LiquiMoly] No active products in CacheProducts — skipping");
                return;
            }

            // ── Step 2: Scrape (category scan → targeted detail enrichment) ──
            var products = await scraper.ScrapeByArticleNumbersAsync(
                articleNumbers, context.CancellationToken);

            if (products.Count == 0)
            {
                _logger.LogWarning("[LiquiMoly] Scraper returned 0 products — skipping save");
                return;
            }

            var scrapedNumbers = products.Select(p => p.ArticleNumber).ToList();

            // ── Step 3: Save to local cache (SQL Server) ────────────────────
            await cacheSync.UpsertAsync(products);
            await cacheSync.DeactivateStaleAsync(scrapedNumbers);

            // ── Step 4: Save to Neon (PostgreSQL) ──────────────────────────
            await neonSync.UpsertAsync(products);
            await neonSync.DeactivateStaleAsync(scrapedNumbers);

            sw.Stop();
            _logger.LogInformation(
                "[LiquiMoly] Job complete | Found={Found}/{Total} | DurationMs={Ms}",
                products.Count, articleNumbers.Count, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[LiquiMoly] Scrape job FAILED | DurationMs={Ms}",
                sw.ElapsedMilliseconds);

            await QuartzRetryHelper.HandleRetryAsync(context, ex);
        }
    }
}
