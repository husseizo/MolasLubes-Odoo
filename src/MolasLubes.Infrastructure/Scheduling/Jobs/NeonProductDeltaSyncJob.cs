using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Services.Sync;
using MolasLubes.Infrastructure.Scheduling;
using Quartz;
using System.Diagnostics;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

[DisallowConcurrentExecution]
public class NeonProductDeltaSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NeonProductDeltaSyncJob> _logger;

    public NeonProductDeltaSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<NeonProductDeltaSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("📦 Neon PRODUCT DELTA sync started");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var productSync = scope.ServiceProvider
                .GetRequiredService<ProductNeonSyncService>();

            var priceSync = scope.ServiceProvider
                .GetRequiredService<PriceListNeonSyncService>();

            await productSync.SyncDeltaAsync();
            await priceSync.SyncAsync();

            sw.Stop();
            _logger.LogInformation(
                "✅ Neon PRODUCT DELTA sync completed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "❌ Neon PRODUCT DELTA sync failed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);

            await QuartzRetryHelper.HandleRetryAsync(context, ex);
        }
    }
}