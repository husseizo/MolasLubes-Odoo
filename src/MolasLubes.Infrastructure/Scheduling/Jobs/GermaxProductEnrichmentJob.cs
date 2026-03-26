using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.Germax;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Quartz job that enriches PENDING seed rows in CacheGermaxProducts by
/// scraping germaxparts.com, then replicates the SCRAPED results to
/// the AutoHub Neon database.
///
/// Processes PENDING rows in batches of 50 until the queue is empty or the
/// job is cancelled.  Each item is individually fault-tolerant: an exception
/// on one item sets ScrapeStatus=ERROR and moves on to the next.
/// After each batch that produced at least one SCRAPED row, SCRAPED data is
/// replicated to Neon immediately, so the read API stays no more than one
/// batch behind during long runs.
/// </summary>
[DisallowConcurrentExecution]
public class GermaxProductEnrichmentJob : IJob
{
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory               _scopeFactory;
    private readonly ILogger<GermaxProductEnrichmentJob> _logger;

    public GermaxProductEnrichmentJob(
        IServiceScopeFactory scopeFactory,
        ILogger<GermaxProductEnrichmentJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("GermaxProductEnrichmentJob: started");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var cache    = scope.ServiceProvider.GetRequiredService<GermaxCacheSyncService>();
            var scraper  = scope.ServiceProvider.GetRequiredService<GermaxProductScraperService>();
            var neonSync = scope.ServiceProvider.GetRequiredService<GermaxAutoHubSyncService>();

            var ct = context.CancellationToken;

            var totalScraped  = 0;
            var totalNoMatch  = 0;
            var totalError    = 0;

            // =============================
            // 1. DRAIN PENDING QUEUE
            // =============================
            while (!ct.IsCancellationRequested)
            {
                var pending = await cache.GetPendingAsync(BatchSize, ct);
                if (pending.Count == 0) break;

                _logger.LogInformation(
                    "GermaxProductEnrichmentJob: processing batch | Count={Count}",
                    pending.Count);

                var batchScraped = 0;

                foreach (var seed in pending)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        var dto = await scraper.TryEnrichAsync(seed, ct);

                        if (dto != null)
                        {
                            await cache.EnrichAsync(dto, ct);
                            totalScraped++;
                            batchScraped++;
                            _logger.LogDebug(
                                "GermaxProductEnrichmentJob: SCRAPED | ItemCode={Code} | Score={Score:F1}",
                                seed.ItemCode, dto.MatchScore);
                        }
                        else
                        {
                            await cache.MarkScrapeResultAsync(seed.ItemCode, "NO_MATCH", null, ct);
                            totalNoMatch++;
                            _logger.LogDebug(
                                "GermaxProductEnrichmentJob: NO_MATCH | ItemCode={Code}", seed.ItemCode);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // propagate cancellation
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "GermaxProductEnrichmentJob: enrichment error | ItemCode={Code}",
                            seed.ItemCode);
                        await cache.MarkScrapeResultAsync(seed.ItemCode, "ERROR", ex.Message, ct);
                        totalError++;
                    }
                }

                // Replicate to Neon after each batch that produced new SCRAPED rows
                // so the read API stays current during long runs.
                if (batchScraped > 0)
                    await neonSync.SyncAsync(ct);
            }

            // =============================
            // 2. FINAL NEON SYNC
            // =============================
            // Run unconditionally: catches any SCRAPED rows that survived from a
            // previous partial run but were never replicated.
            await neonSync.SyncAsync(ct);

            sw.Stop();
            _logger.LogInformation(
                "GermaxProductEnrichmentJob: completed | Scraped={Scraped} | NoMatch={NoMatch} | Error={Error} | DurationMs={Ms}",
                totalScraped, totalNoMatch, totalError, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(
                "GermaxProductEnrichmentJob: cancelled | DurationMs={Ms}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "GermaxProductEnrichmentJob: FAILED | DurationMs={Ms}", sw.ElapsedMilliseconds);
        }
    }
}
