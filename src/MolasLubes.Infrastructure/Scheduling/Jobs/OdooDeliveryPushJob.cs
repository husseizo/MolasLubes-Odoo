using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

[DisallowConcurrentExecution]
public class OdooDeliveryPushJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OdooDeliveryPushJob> _logger;

    public OdooDeliveryPushJob(
        IServiceScopeFactory scopeFactory,
        ILogger<OdooDeliveryPushJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("🚚 Odoo Delivery Push Job started");

        using var scope = _scopeFactory.CreateScope();

        var service = scope.ServiceProvider
            .GetRequiredService<OdooDeliveryPushService>();

        await service.PushPendingDeliveriesAsync();

        _logger.LogInformation("✅ Odoo Delivery Push Job completed");
    }
}
