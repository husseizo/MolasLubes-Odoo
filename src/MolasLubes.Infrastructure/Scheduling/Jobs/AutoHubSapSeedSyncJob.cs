using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

[DisallowConcurrentExecution]
public class AutoHubSapSeedSyncJob : IJob
{
    // Static so it survives across transient instances. On process restart it
    // resets to null, which forces an immediate full sync — intentional.
    private static DateTime? _lastFullSyncAt;
    private static readonly TimeSpan FullSyncInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoHubSapSeedSyncJob> _logger;

    public AutoHubSapSeedSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<AutoHubSapSeedSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("AutoHubSapSeedSyncJob: started");

        using var scope = _scopeFactory.CreateScope();

        var reader = scope.ServiceProvider
            .GetRequiredService<SapAutoHubSeedReader>();

        var cache = scope.ServiceProvider
            .GetRequiredService<GermaxCacheSyncService>();

        // =============================
        // 1. DETERMINE FULL vs DELTA
        // =============================
        // Full sync runs:
        //   a) on first run (no watermark exists)
        //   b) every 24 h (catches frozen / reclassified items that delta misses,
        //      because the SAP query only returns frozenFor='N' rows)
        var watermark = cache
            .GetWatermarkAsync()
            .GetAwaiter()
            .GetResult();

        var now = DateTime.UtcNow;
        var isFullSync = watermark is null
            || _lastFullSyncAt is null
            || (now - _lastFullSyncAt.Value) >= FullSyncInterval;

        _logger.LogInformation(
            "AutoHubSapSeedSyncJob: mode={Mode} | Watermark={Watermark} | LastFull={LastFull}",
            isFullSync ? "FULL" : "DELTA",
            watermark?.ToString("u") ?? "none",
            _lastFullSyncAt?.ToString("u") ?? "none");

        // =============================
        // 2. READ FROM SAP
        // =============================
        var seeds = isFullSync
            ? reader.ReadAll()
            : reader.ReadSince(watermark!.Value);

        _logger.LogInformation(
            "AutoHubSapSeedSyncJob: SAP returned {Count} seed rows", seeds.Count);

        if (seeds.Count == 0 && !isFullSync)
        {
            _logger.LogInformation(
                "AutoHubSapSeedSyncJob: no changed rows since watermark — nothing to upsert");
            return Task.CompletedTask;
        }

        // =============================
        // 3. UPSERT INTO CACHE
        // =============================
        var (inserted, updated, deactivated) = cache
            .UpsertSeedAsync(seeds, isFullSync)
            .GetAwaiter()
            .GetResult();

        // Record the full-sync timestamp only after a successful upsert
        if (isFullSync)
            _lastFullSyncAt = now;

        _logger.LogInformation(
            "AutoHubSapSeedSyncJob: completed | Inserted={Inserted} | Updated={Updated} | Deactivated={Deactivated}",
            inserted, updated, deactivated);

        return Task.CompletedTask;
    }
}
