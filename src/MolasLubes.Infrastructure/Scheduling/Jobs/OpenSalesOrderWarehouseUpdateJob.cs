using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Quartz job that reads all open SAP Sales Orders (ORDR, DocStatus='O')
/// and updates their line warehouses to COCWHSE (Cocoa Warehouse).
/// 
/// Replaces the old quotation conversion job (OQUT → ORDR).
/// Now: read existing open ORDR → update warehouse on editable lines → save back to SAP.
/// </summary>
[DisallowConcurrentExecution]
public class OpenSalesOrderWarehouseUpdateJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OpenSalesOrderWarehouseUpdateJob> _logger;

    public OpenSalesOrderWarehouseUpdateJob(
        IServiceScopeFactory scopeFactory,
        ILogger<OpenSalesOrderWarehouseUpdateJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("🏢 Open Sales Order Warehouse Update job started");

        using var scope = _scopeFactory.CreateScope();
        var updater = scope.ServiceProvider.GetRequiredService<SapOpenSalesOrderWarehouseUpdater>();

        int updated = 0;
        int skipped = 0;
        int failed = 0;

        try
        {
            var result = await updater.UpdateOpenOrderWarehousesAsync();

            updated = result.UpdatedCount;
            skipped = result.SkippedCount;
            failed = result.FailedCount;

            sw.Stop();
            _logger.LogInformation(
                "✅ Open Sales Order Warehouse Update completed | Updated={Updated} Skipped={Skipped} Failed={Failed} | Duration={Duration}ms",
                updated,
                skipped,
                failed,
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "❌ Open Sales Order Warehouse Update FAILED | Updated={Updated} Failed={Failed} | Duration={Duration}ms",
                updated,
                failed,
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}
