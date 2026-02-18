using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Caching;
using Quartz;
using System.Diagnostics;

[DisallowConcurrentExecution]
public class SalesOrderSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SalesOrderSyncJob> _logger;

    public SalesOrderSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<SalesOrderSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("🚀 SalesOrder Sync started");

        using var scope = _scopeFactory.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<SapSalesOrderReader>();
        var cache = scope.ServiceProvider.GetRequiredService<SalesOrderCacheService>();

        try
        {
            var hasAny = await cache.HasAnySalesOrderAsync();

            var orders = hasAny
                ? reader.ReadRecentSalesOrders(DateTime.UtcNow.AddDays(-1)).ToList()
                : reader.ReadAllSalesOrders().ToList();

            _logger.LogInformation("🛒 SalesOrders read from SAP | Count={Count}", orders.Count);

            if (orders.Count > 0)
                await cache.UpsertSalesOrdersAsync(orders);

            sw.Stop();
            _logger.LogInformation("✅ SalesOrder Sync completed | DurationMs={Ms}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "❌ SalesOrder Sync failed | DurationMs={Ms}", sw.ElapsedMilliseconds);
            throw;
        }
    }
}