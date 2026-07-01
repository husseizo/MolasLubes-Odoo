using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;
using System.Diagnostics;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

[DisallowConcurrentExecution]
public class NeonAutoHubStockSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NeonAutoHubStockSyncJob> _logger;

    public NeonAutoHubStockSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<NeonAutoHubStockSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("AutoHub Neon stock sync: started");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider
                .GetRequiredService<AutoHubNeonStockSyncService>();

            await svc.SyncAsync(context.CancellationToken);

            _logger.LogInformation(
                "AutoHub Neon stock sync: completed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AutoHub Neon stock sync: failed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);

            await QuartzRetryHelper.HandleRetryAsync(context, ex);
        }
    }
}
