using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Caching;
using Quartz;

[DisallowConcurrentExecution]
public class DeliveryDeltaSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeliveryDeltaSyncJob> _logger;

    public DeliveryDeltaSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<DeliveryDeltaSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("🚚 Delivery Sync started");

        using var scope = _scopeFactory.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<SapDeliveryReader>();
        var cache = scope.ServiceProvider.GetRequiredService<DeliveryCacheService>();
        var orderStatus = scope.ServiceProvider.GetRequiredService<SalesOrderStatusService>();

        var watermark = await cache.GetLastSapUpdateDateAsync();

        var deliveries = watermark == null
            ? reader.ReadAllDeliveries().ToList()
            : reader.ReadRecentDeliveries(watermark.Value.AddMinutes(-2)).ToList();

        _logger.LogInformation("🚚 Deliveries read from SAP | Count={Count}", deliveries.Count);

        if (deliveries.Count == 0)
            return;

        await cache.RegisterDeliveriesAsync(deliveries);

        foreach (var d in deliveries)
        {
            if (d.BaseOrderEntry > 0)
                await orderStatus.MarkOrderDeliveredAsync(d.BaseOrderEntry);
        }

        _logger.LogInformation("✅ Delivery Sync completed");
    }
}