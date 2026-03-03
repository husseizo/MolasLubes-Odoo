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
///   1. Reads distinct active ItemCodes from <see cref="MolasCacheDbContext.CacheProducts"/>.
///   2. Looks up each article number on the Liqui-Moly website via the search page.
///   3. Upserts results into the local SQL Server cache via
///      <see cref="LiquiMolyCacheSyncService"/>.
///   4. Upserts results into Neon PostgreSQL via
///      <see cref="LiquiMolyNeonSyncService"/>.
///   5. Marks products no longer found on the website as inactive in both stores.
///
/// Scheduled (default) every 24 hours via Program.cs configuration.
/// Can also be triggered manually via the admin API.
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

            // ── Step 1: Resolve article numbers from local cache ────────
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
                _logger.LogWarning("[LiquiMoly] No active products in CacheProducts — skipping scrape");
                return;
            }

            // ── Step 2: Scrape Liqui-Moly by article number ─────────────
            var products = await scraper.ScrapeByArticleNumbersAsync(
                articleNumbers, context.CancellationToken);

            if (products.Count == 0)
            {
                _logger.LogWarning("[LiquiMoly] Scraper returned 0 products — skipping save");
                return;
            }

            var scrapedNumbers = products.Select(p => p.ArticleNumber).ToList();

            // ── Step 3: Save to local cache (SQL Server) ────────────────
            await cacheSync.UpsertAsync(products);
            await cacheSync.DeactivateStaleAsync(scrapedNumbers);

            // ── Step 4: Save to Neon (PostgreSQL) ──────────────────────
            await neonSync.UpsertAsync(products);
            await neonSync.DeactivateStaleAsync(scrapedNumbers);

            sw.Stop();
            _logger.LogInformation(
                "[LiquiMoly] Scrape job completed | Found={Found}/{Total} | DurationMs={Ms}",
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
