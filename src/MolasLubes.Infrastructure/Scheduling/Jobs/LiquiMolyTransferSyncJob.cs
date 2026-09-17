using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Services.Sync;
using MolasLubes.Infrastructure.Scheduling;
using Quartz;
using System.Diagnostics;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Drains the SapEventOutbox for OWTQ / OWTR events and syncs them to Neon.
/// Runs every 2 minutes during business hours. DisallowConcurrentExecution ensures
/// no two runs overlap (important: SAP DI API is serialised via a critical section).
/// </summary>
[DisallowConcurrentExecution]
public class LiquiMolyTransferSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LiquiMolyTransferSyncJob> _logger;

    public LiquiMolyTransferSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<LiquiMolyTransferSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("LiquiMolyTransferSync job started");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider
                .GetRequiredService<LiquiMolyTransferSyncService>();

            await service.ProcessPendingAsync(context.CancellationToken);

            sw.Stop();
            _logger.LogInformation(
                "LiquiMolyTransferSync job completed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "LiquiMolyTransferSync job failed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);

            await QuartzRetryHelper.HandleRetryAsync(context, ex);
        }
    }
}
