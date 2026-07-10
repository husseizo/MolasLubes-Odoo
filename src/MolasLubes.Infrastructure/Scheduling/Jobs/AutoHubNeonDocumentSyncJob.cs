using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;
using System.Diagnostics;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

[DisallowConcurrentExecution]
public class AutoHubNeonDocumentSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoHubNeonDocumentSyncJob> _logger;

    public AutoHubNeonDocumentSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<AutoHubNeonDocumentSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("AutoHub Neon document sync: started");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider
                .GetRequiredService<AutoHubNeonDocumentSyncService>();

            // Each document type syncs independently — one failure doesn't block the others
            await RunAsync("Deliveries",         () => svc.SyncDeliveriesAsync(ct));
            await RunAsync("SalesOrders",        () => svc.SyncSalesOrdersAsync(ct));
            await RunAsync("Invoices",           () => svc.SyncInvoicesAsync(ct));
            await RunAsync("GoodsReceipts",      () => svc.SyncGoodsReceiptsAsync(ct));
            await RunAsync("StockTransfers",     () => svc.SyncStockTransfersAsync(ct));
            await RunAsync("InventoryCountings", () => svc.SyncInventoryCountingsAsync(ct));
            await RunAsync("PurchaseOrders",     () => svc.SyncPurchaseOrdersAsync(ct));
            await RunAsync("UoMs",               () => svc.SyncUoMsAsync(ct));

            _logger.LogInformation(
                "AutoHub Neon document sync: all types completed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AutoHub Neon document sync: unexpected failure | DurationMs={Ms}",
                sw.ElapsedMilliseconds);

            await QuartzRetryHelper.HandleRetryAsync(context, ex);
        }
    }

    private async Task RunAsync(string name, Func<Task> fn)
    {
        try   { await fn(); }
        catch (Exception ex) { _logger.LogError(ex, "AutoHub doc sync [{Name}]: failed (continuing)", name); }
    }
}
