using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

[DisallowConcurrentExecution]
public class AutoHubSapSeedSyncJob : IJob
{
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
        var watermark = cache
            .GetWatermarkAsync()
            .GetAwaiter()
            .GetResult();

        var isFullSync = watermark is null;

        _logger.LogInformation(
            "AutoHubSapSeedSyncJob: mode={Mode} | Watermark={Watermark}",
            isFullSync ? "FULL" : "DELTA",
            watermark?.ToString("u") ?? "none");

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

        _logger.LogInformation(
            "AutoHubSapSeedSyncJob: completed | Inserted={Inserted} | Updated={Updated} | Deactivated={Deactivated}",
            inserted, updated, deactivated);

        return Task.CompletedTask;
    }
}
