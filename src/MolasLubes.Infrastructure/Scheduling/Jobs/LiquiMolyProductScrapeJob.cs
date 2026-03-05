using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Services.Sync;
using MolasLubes.Infrastructure.Scheduling;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Quartz job that:
///   1. Reads distinct active ItemCodes from CacheProducts.
///   2. Splits them into batches (controlled by <see cref="LiquiMolyScraperSettings.BatchSize"/>).
///   3. For each batch, calls <see cref="LiquiMolyProductScraperService.ScrapeByArticleNumbersAsync"/>
///      which uses a 2-phase approach:
///        Phase 1 — per-article catalog search to locate matching product stubs.
///        Phase 2 — visit each matched product's detail page in shuffled order
///                  with human-like variable delays.
///   4. Upserts found products into SQL Server cache via LiquiMolyCacheSyncService.
///   5. Upserts found products into Neon PostgreSQL via LiquiMolyNeonSyncService.
///   6. After all batches complete, marks products no longer found as inactive in both stores.
///
/// Scheduled (default) every 24 hours via Program.cs.
/// Can also be triggered manually via POST /api/admin/liquimoly/scrape.
/// </summary>
[DisallowConcurrentExecution]
public class LiquiMolyProductScrapeJob : IJob
{
    private readonly IServiceScopeFactory               _scopeFactory;
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
            var settings  = scope.ServiceProvider.GetRequiredService<IOptions<LiquiMolyScraperSettings>>().Value;

            int batchSize = settings.BatchSize > 0 ? settings.BatchSize : 50;

            // ── Step 1: Distinct active ItemCodes from CacheProducts ─────────
            var articleNumbers = await cacheDb.CacheProducts
                .Where(p => p.IsActive)
                .Select(p => p.ItemCode)
                .Distinct()
                .ToListAsync(context.CancellationToken);

            _logger.LogInformation(
                "[LiquiMoly] Resolved {Count} distinct article numbers from CacheProducts (BatchSize={BatchSize})",
                articleNumbers.Count, batchSize);

            if (articleNumbers.Count == 0)
            {
                _logger.LogWarning("[LiquiMoly] No active products in CacheProducts — skipping");
                return;
            }

            // ── Step 2: Scrape in batches, saving incrementally ───────────────
            var allScrapedNumbers = new List<string>();
            int totalFound        = 0;
            int batchCount        = (int)Math.Ceiling((double)articleNumbers.Count / batchSize);

            for (int i = 0; i < batchCount; i++)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("[LiquiMoly] Cancellation requested — stopping after batch {Batch}/{Total}",
                        i, batchCount);
                    break;
                }

                var batch = articleNumbers
                    .Skip(i * batchSize)
                    .Take(batchSize)
                    .ToList();

                _logger.LogInformation(
                    "[LiquiMoly] Batch {Batch}/{Total} — scraping {Count} article(s)",
                    i + 1, batchCount, batch.Count);

                var products = await scraper.ScrapeByArticleNumbersAsync(
                    batch, context.CancellationToken);

                if (products.Count == 0)
                {
                    _logger.LogWarning(
                        "[LiquiMoly] Batch {Batch}/{Total} — scraper returned 0 products",
                        i + 1, batchCount);
                    continue;
                }

                totalFound += products.Count;
                allScrapedNumbers.AddRange(products.Select(p => p.ArticleNumber));

                // Save each batch incrementally so partial progress is not lost
                await cacheSync.UpsertAsync(products);
                await neonSync.UpsertAsync(products);

                _logger.LogInformation(
                    "[LiquiMoly] Batch {Batch}/{Total} saved | Found={Found} | RunningTotal={RunningTotal}",
                    i + 1, batchCount, products.Count, totalFound);
            }

            if (allScrapedNumbers.Count == 0)
            {
                _logger.LogWarning("[LiquiMoly] No products found across all batches — skipping deactivation");
                sw.Stop();
                _logger.LogInformation(
                    "[LiquiMoly] Job complete | Found=0/{Total} | DurationMs={Ms}",
                    articleNumbers.Count, sw.ElapsedMilliseconds);
                return;
            }

            // ── Step 3: Deactivate stale products (not seen in this run) ─────
            await cacheSync.DeactivateStaleAsync(allScrapedNumbers);
            await neonSync.DeactivateStaleAsync(allScrapedNumbers);

            sw.Stop();
            _logger.LogInformation(
                "[LiquiMoly] Job complete | Found={Found}/{Total} | Batches={Batches} | DurationMs={Ms}",
                totalFound, articleNumbers.Count, batchCount, sw.ElapsedMilliseconds);
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
