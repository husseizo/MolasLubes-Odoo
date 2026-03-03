using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Services.Sync;
using MolasLubes.Infrastructure.Scheduling;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Quartz job that:
///   1. Runs the <see cref="LiquiMolyProductScraperService"/> to fetch the
///      Liqui-Moly public product catalog.
///   2. Upserts results into the local SQL Server cache via
///      <see cref="LiquiMolyCacheSyncService"/>.
///   3. Upserts results into Neon PostgreSQL via
///      <see cref="LiquiMolyNeonSyncService"/>.
///   4. Marks products no longer found on the website as inactive in both stores.
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

            var scraper   = scope.ServiceProvider.GetRequiredService<LiquiMolyProductScraperService>();
            var cacheSync = scope.ServiceProvider.GetRequiredService<LiquiMolyCacheSyncService>();
            var neonSync  = scope.ServiceProvider.GetRequiredService<LiquiMolyNeonSyncService>();

            // ── Step 1: Scrape ──────────────────────────────────────────
            var products = await scraper.ScrapeAllProductsAsync(
                context.CancellationToken);

            if (products.Count == 0)
            {
                _logger.LogWarning("[LiquiMoly] Scraper returned 0 products — skipping save");
                return;
            }

            var articleNumbers = products.Select(p => p.ArticleNumber).ToList();

            // ── Step 2: Save to local cache (SQL Server) ────────────────
            await cacheSync.UpsertAsync(products);
            await cacheSync.DeactivateStaleAsync(articleNumbers);

            // ── Step 3: Save to Neon (PostgreSQL) ──────────────────────
            await neonSync.UpsertAsync(products);
            await neonSync.DeactivateStaleAsync(articleNumbers);

            sw.Stop();
            _logger.LogInformation(
                "[LiquiMoly] Scrape job completed | Products={Count} | DurationMs={Ms}",
                products.Count, sw.ElapsedMilliseconds);
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
