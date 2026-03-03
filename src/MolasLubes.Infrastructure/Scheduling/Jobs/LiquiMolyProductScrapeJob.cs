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
///   1. Reads distinct active (ItemCode, ItemName) pairs from CacheProducts.
///   2. Passes them to <see cref="LiquiMolyProductScraperService.ScrapeByArticleNumbersAsync"/>
///      which searches Liqui-Moly one product at a time in randomised order,
///      using the product name (not the bare number) as the search term so the
///      site does not redirect to the oil-guide page.
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

            // ── Step 1: Resolve distinct (ItemCode, ItemName) from CacheProducts ─
            // GroupBy ItemCode so that each article number appears once, taking
            // the MAX ItemName — the value is identical across warehouse rows.
            var items = await cacheDb.CacheProducts
                .Where(p => p.IsActive)
                .GroupBy(p => p.ItemCode)
                .Select(g => new { Code = g.Key, Name = g.Max(p => p.ItemName) })
                .ToListAsync(context.CancellationToken);

            _logger.LogInformation(
                "[LiquiMoly] Resolved {Count} distinct products from CacheProducts",
                items.Count);

            if (items.Count == 0)
            {
                _logger.LogWarning("[LiquiMoly] No active products in CacheProducts — skipping");
                return;
            }

            // ── Step 2: Scrape Liqui-Moly one product at a time ─────────────
            var products = await scraper.ScrapeByArticleNumbersAsync(
                items.Select(i => (i.Code, i.Name ?? i.Code)),
                context.CancellationToken);

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
                products.Count, items.Count, sw.ElapsedMilliseconds);
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
