using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.Germax;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Quartz job that retries Germax rows whose most recent scrape attempt ended
/// in ERROR or NO_MATCH, limited to failures within the last 7 days.
///
/// A capped batch size (25 items) keeps retry traffic conservative.  Items
/// that exceed the age window are left untouched until a manual admin trigger
/// re-queues them.  After a successful re-scrape the result is replicated to
/// Neon via <see cref="GermaxAutoHubSyncService"/>.
/// </summary>
[DisallowConcurrentExecution]
public class GermaxRetryFailedJob : IJob
{
    private const int BatchSize  = 25;
    private const int MaxAgeDays = 7;

    private readonly IServiceScopeFactory            _scopeFactory;
    private readonly ILogger<GermaxRetryFailedJob>   _logger;

    public GermaxRetryFailedJob(
        IServiceScopeFactory scopeFactory,
        ILogger<GermaxRetryFailedJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("GermaxRetryFailedJob: started");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var cache    = scope.ServiceProvider.GetRequiredService<GermaxCacheSyncService>();
            var scraper  = scope.ServiceProvider.GetRequiredService<GermaxProductScraperService>();
            var neonSync = scope.ServiceProvider.GetRequiredService<GermaxAutoHubSyncService>();

            var ct = context.CancellationToken;

            // A manual admin trigger passes bypassAgeFilter=true in the job data map
            // so stale failures (> 7 days) can be retried on demand.  Scheduled runs
            // always use the age window to keep traffic conservative.
            var bypassAgeFilter =
                context.MergedJobDataMap.TryGetValue("bypassAgeFilter", out var val)
                && val is bool flag && flag;

            int? maxAgeDays = bypassAgeFilter ? null : MaxAgeDays;

            var retryable = await cache.GetRetryableAsync(BatchSize, maxAgeDays, ct);

            if (retryable.Count == 0)
            {
                _logger.LogInformation(
                    "GermaxRetryFailedJob: no retryable items{Window} — done",
                    bypassAgeFilter ? " (all-time)" : $" within {MaxAgeDays}d window");
                return;
            }

            _logger.LogInformation(
                "GermaxRetryFailedJob: retrying {Count} item(s){Window} | cap={Cap}",
                retryable.Count,
                bypassAgeFilter ? " (all-time)" : $" (age ≤{MaxAgeDays}d)",
                BatchSize);

            var totalScraped = 0;
            var totalNoMatch = 0;
            var totalError   = 0;

            foreach (var seed in retryable)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var dto = await scraper.TryEnrichAsync(seed, ct);

                    if (dto != null)
                    {
                        await cache.EnrichAsync(dto, ct);
                        totalScraped++;
                        _logger.LogDebug(
                            "GermaxRetryFailedJob: SCRAPED | ItemCode={Code} | Score={Score:F1}",
                            seed.ItemCode, dto.MatchScore);
                    }
                    else
                    {
                        await cache.MarkScrapeResultAsync(seed.ItemCode, "NO_MATCH", null, ct);
                        totalNoMatch++;
                        _logger.LogDebug(
                            "GermaxRetryFailedJob: NO_MATCH | ItemCode={Code}", seed.ItemCode);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "GermaxRetryFailedJob: enrichment error | ItemCode={Code}", seed.ItemCode);
                    await cache.MarkScrapeResultAsync(seed.ItemCode, "ERROR", ex.Message, ct);
                    totalError++;
                }
            }

            // Replicate any newly SCRAPED rows to Neon
            if (totalScraped > 0)
                await neonSync.SyncAsync(ct);

            sw.Stop();
            _logger.LogInformation(
                "GermaxRetryFailedJob: completed | Scraped={Scraped} | NoMatch={NoMatch} | Error={Error} | DurationMs={Ms}",
                totalScraped, totalNoMatch, totalError, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(
                "GermaxRetryFailedJob: cancelled | DurationMs={Ms}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "GermaxRetryFailedJob: FAILED | DurationMs={Ms}", sw.ElapsedMilliseconds);
        }
    }
}
