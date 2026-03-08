using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Integrations.Meguin;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Services.Sync;
using MolasLubes.Infrastructure.Scheduling;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Quartz job that scrapes both the Liqui-Moly and Meguin product catalogues.
///
/// Products whose SAP <c>ItemName</c> contains "meguin" (case-insensitive) are
/// routed to <see cref="MeguinProductScraperService"/>; all others are scraped
/// from Liqui-Moly.  Both sets are upserted into the same SQL Server and Neon
/// stores via <see cref="LiquiMolyCacheSyncService"/> /
/// <see cref="LiquiMolyNeonSyncService"/>.
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

            var cacheDb        = scope.ServiceProvider.GetRequiredService<MolasCacheDbContext>();
            var lmScraper      = scope.ServiceProvider.GetRequiredService<LiquiMolyProductScraperService>();
            var meguinScraper  = scope.ServiceProvider.GetRequiredService<MeguinProductScraperService>();
            var cacheSync      = scope.ServiceProvider.GetRequiredService<LiquiMolyCacheSyncService>();
            var neonSync       = scope.ServiceProvider.GetRequiredService<LiquiMolyNeonSyncService>();
            var settings       = scope.ServiceProvider.GetRequiredService<IOptions<LiquiMolyScraperSettings>>().Value;

            int batchSize = settings.BatchSize > 0 ? settings.BatchSize : 50;

            // ── Step 1: Fetch distinct active products (ItemCode + ItemName) ──
            var activeProducts = await cacheDb.CacheProducts
                .Where(p => p.IsActive)
                .Select(p => new { p.ItemCode, p.ItemName })
                .Distinct()
                .ToListAsync(context.CancellationToken);

            if (activeProducts.Count == 0)
            {
                _logger.LogWarning("[LiquiMoly] No active products in CacheProducts — skipping");
                return;
            }

            // ── Step 2: Split by brand ────────────────────────────────────────
            // Products with "meguin" in the SAP item name are Meguin products.
            var meguinSkus = activeProducts
                .Where(p => p.ItemName != null &&
                            p.ItemName.Contains("meguin", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.ItemCode)
                .Distinct()
                .ToList();

            var lmSkus = activeProducts
                .Where(p => p.ItemName == null ||
                            !p.ItemName.Contains("meguin", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.ItemCode)
                .Distinct()
                .ToList();

            _logger.LogInformation(
                "[LiquiMoly] SKU split | LiquiMoly={Lm} | Meguin={Meg} | Total={Total} (BatchSize={Bs})",
                lmSkus.Count, meguinSkus.Count, activeProducts.Count, batchSize);

            // ── Step 3: Scrape both brands in batches ─────────────────────────
            var allScrapedNumbers = new List<string>();
            int totalFound = 0;

            totalFound += await ScrapeInBatchesAsync(
                lmSkus, lmScraper, cacheSync, neonSync, batchSize,
                "LiquiMoly", allScrapedNumbers, context.CancellationToken);

            totalFound += await ScrapeInBatchesAsync(
                meguinSkus, meguinScraper, cacheSync, neonSync, batchSize,
                "Meguin", allScrapedNumbers, context.CancellationToken);

            if (allScrapedNumbers.Count == 0)
            {
                _logger.LogWarning("[LiquiMoly] No products found across all batches — skipping deactivation");
                sw.Stop();
                _logger.LogInformation(
                    "[LiquiMoly] Job complete | Found=0/{Total} | DurationMs={Ms}",
                    activeProducts.Count, sw.ElapsedMilliseconds);
                return;
            }

            // ── Step 4: Deactivate stale products (not seen in this run) ──────
            await cacheSync.DeactivateStaleAsync(allScrapedNumbers);
            await neonSync.DeactivateStaleAsync(allScrapedNumbers);

            sw.Stop();
            _logger.LogInformation(
                "[LiquiMoly] Job complete | Found={Found}/{Total} | DurationMs={Ms}",
                totalFound, activeProducts.Count, sw.ElapsedMilliseconds);
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

    private async Task<int> ScrapeInBatchesAsync(
        List<string> skus,
        LiquiMolyProductScraperService scraper,
        LiquiMolyCacheSyncService cacheSync,
        LiquiMolyNeonSyncService neonSync,
        int batchSize,
        string brandLabel,
        List<string> allScrapedNumbers,
        CancellationToken ct)
    {
        if (skus.Count == 0) return 0;

        int totalFound  = 0;
        int batchCount  = (int)Math.Ceiling((double)skus.Count / batchSize);

        for (int i = 0; i < batchCount; i++)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogWarning("[{Brand}] Cancellation requested — stopping after batch {Batch}/{Total}",
                    brandLabel, i, batchCount);
                break;
            }

            var batch = skus.Skip(i * batchSize).Take(batchSize).ToList();

            _logger.LogInformation(
                "[{Brand}] Batch {Batch}/{Total} — scraping {Count} article(s)",
                brandLabel, i + 1, batchCount, batch.Count);

            var products = await scraper.ScrapeByArticleNumbersAsync(batch, ct);

            if (products.Count == 0)
            {
                _logger.LogWarning(
                    "[{Brand}] Batch {Batch}/{Total} — scraper returned 0 products",
                    brandLabel, i + 1, batchCount);
                continue;
            }

            totalFound += products.Count;
            allScrapedNumbers.AddRange(products.Select(p => p.ArticleNumber));

            await cacheSync.UpsertAsync(products);
            await neonSync.UpsertAsync(products);

            _logger.LogInformation(
                "[{Brand}] Batch {Batch}/{Total} saved | Found={Found} | RunningTotal={RunningTotal}",
                brandLabel, i + 1, batchCount, products.Count, totalFound);
        }

        return totalFound;
    }
}
