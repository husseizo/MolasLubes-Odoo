using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Caching;
using Quartz;
using System.Diagnostics;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Reads all currently-open SAP sales orders (DocStatus = 'O', Cancelled = 'N')
/// via the DI API and upserts them — with full header and line detail — into
/// the local cache database.
/// </summary>
[DisallowConcurrentExecution]
public class SapOpenOrdersSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SapOpenOrdersSyncJob> _logger;

    public SapOpenOrdersSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<SapOpenOrdersSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("🚀 SapOpenOrders Sync started");

        using var scope = _scopeFactory.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<SapSalesOrderReader>();
        var cache  = scope.ServiceProvider.GetRequiredService<SalesOrderCacheService>();

        try
        {
            var orders = reader.ReadOpenSalesOrders().ToList();

            _logger.LogInformation(
                "🛒 Open SAP Sales Orders read | Count={Count}",
                orders.Count);

            if (orders.Count > 0)
                await cache.UpsertSalesOrdersAsync(orders);

            sw.Stop();
            _logger.LogInformation(
                "✅ SapOpenOrders Sync completed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "❌ SapOpenOrders Sync failed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}
