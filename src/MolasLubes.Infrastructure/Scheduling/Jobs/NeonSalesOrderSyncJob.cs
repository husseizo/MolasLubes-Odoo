using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;
using System.Diagnostics;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

[DisallowConcurrentExecution]
public class NeonSalesOrderSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NeonSalesOrderSyncJob> _logger;

    public NeonSalesOrderSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<NeonSalesOrderSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("🧾 Neon SALES ORDER sync started");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var service = scope.ServiceProvider
                .GetRequiredService<NeonSalesOrderSyncService>();

            await service.SyncDeltaAsync();

            // 🔗 CHAIN ORDER LINES JOB
            await context.Scheduler.TriggerJob(
                new JobKey("NeonSalesOrderLineSyncJob"));

            sw.Stop();
            _logger.LogInformation(
                "✅ Neon SALES ORDER sync completed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "❌ Neon SALES ORDER sync failed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);

            if (context.RefireCount < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(5 * (context.RefireCount + 1)));
                throw new JobExecutionException(ex)
                {
                    RefireImmediately = true
                };
            }

            throw;
        }
    }
}