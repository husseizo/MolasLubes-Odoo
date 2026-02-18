using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

[DisallowConcurrentExecution]
public class NeonSalesOrderLineSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NeonSalesOrderLineSyncJob> _logger;

    public NeonSalesOrderLineSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<NeonSalesOrderLineSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("📄 Neon SALES ORDER LINE sync job started");

        using var scope = _scopeFactory.CreateScope();

        var service =
            scope.ServiceProvider.GetRequiredService<NeonSalesOrderLineSyncService>();

        await service.SyncDeltaAsync();

        _logger.LogInformation("✅ Neon SALES ORDER LINE sync job completed");
    }
}