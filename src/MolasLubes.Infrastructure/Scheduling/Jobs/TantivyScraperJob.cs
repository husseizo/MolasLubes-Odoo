using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.TantivyScraper;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Quartz job that scrapes VIKA and Borsehung catalog pages for every
/// TantivyPart that has a U_Article_No but no successful scrape yet.
///
/// Uses Playwright (headless Chromium) — one browser per job run,
/// sequential per-item processing with a configurable delay to be
/// polite to the target servers.
///
/// Trigger: nightly at 02:30 UTC (after Germax at 01:30).
/// Also manually triggerable via POST /api/admin/autohub/tantivy/scrape.
/// </summary>
[DisallowConcurrentExecution]
public class TantivyScraperJob : IJob
{
    private readonly IServiceScopeFactory           _scopeFactory;
    private readonly ILogger<TantivyScraperJob>     _logger;

    public TantivyScraperJob(
        IServiceScopeFactory scopeFactory,
        ILogger<TantivyScraperJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("TantivyScraperJob: started");

        var ct = context.CancellationToken;

        try
        {
            using var scope  = _scopeFactory.CreateScope();
            var syncSvc      = scope.ServiceProvider.GetRequiredService<TantivyScrapeResultSyncService>();
            var settings     = scope.ServiceProvider.GetRequiredService<IOptions<TantivyScraperSettings>>().Value;
            var scraperLogger = scope.ServiceProvider.GetRequiredService<ILogger<TantivyScraperService>>();

            await using var scraper = new TantivyScraperService(
                scope.ServiceProvider.GetRequiredService<IOptions<TantivyScraperSettings>>(),
                scraperLogger);

            await scraper.InitAsync();

            int totalScraped = 0, totalNoMatch = 0, totalError = 0;
            int consecutiveErrors = 0;

            while (!ct.IsCancellationRequested)
            {
                var pending = await syncSvc.GetPendingAsync(settings.BatchSize, ct);
                if (pending.Count == 0) break;

                _logger.LogInformation(
                    "TantivyScraperJob: processing batch | Count={Count}",
                    pending.Count);

                foreach (var seed in pending)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        var result = await scraper.TryScrapeAsync(seed, ct);
                        consecutiveErrors = 0;

                        if (result != null)
                        {
                            await syncSvc.SaveScrapedAsync(result, seed.Brand, seed.ArticleNo, ct);
                            totalScraped++;
                            _logger.LogDebug(
                                "TantivyScraperJob: SCRAPED | {Code} | {Name}",
                                seed.ItemCode, result.PartName);
                        }
                        else
                        {
                            await syncSvc.MarkStatusAsync(
                                seed.ItemCode, "NO_MATCH", null, seed.Brand, seed.ArticleNo, ct);
                            totalNoMatch++;
                            _logger.LogDebug(
                                "TantivyScraperJob: NO_MATCH | {Code} | Article={Article}",
                                seed.ItemCode, seed.ArticleNo);
                        }

                        await Task.Delay(settings.DelayBetweenRequestsMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        consecutiveErrors++;
                        var msg = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                        await syncSvc.MarkStatusAsync(
                            seed.ItemCode, "ERROR", msg, seed.Brand, seed.ArticleNo, ct);
                        totalError++;
                        _logger.LogWarning(ex,
                            "TantivyScraperJob: ERROR | {Code} | ConsecutiveErrors={N}",
                            seed.ItemCode, consecutiveErrors);

                        await Task.Delay(settings.DelayBetweenRequestsMs, ct);

                        if (consecutiveErrors >= settings.ConsecutiveErrorThreshold)
                        {
                            _logger.LogWarning(
                                "TantivyScraperJob: {N} consecutive errors — rate limit suspected; " +
                                "backing off {BackoffMs}ms then recreating browser",
                                consecutiveErrors, settings.RateLimitBackoffMs);

                            await Task.Delay(settings.RateLimitBackoffMs, ct);
                            await scraper.RecreateAsync();
                            consecutiveErrors = 0;
                        }
                    }
                }
            }

            sw.Stop();
            _logger.LogInformation(
                "TantivyScraperJob: completed | Scraped={S} | NoMatch={N} | Error={E} | DurationMs={Ms}",
                totalScraped, totalNoMatch, totalError, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("TantivyScraperJob: cancelled | DurationMs={Ms}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "TantivyScraperJob: FAILED | DurationMs={Ms}", sw.ElapsedMilliseconds);
        }
    }
}
