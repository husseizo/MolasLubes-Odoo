using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Formats RDR1.Dscription for every Open Sales Order to
/// "U_ItemName/U_Manufacturer/OriginalDescription".
///
/// Runs daily at 06:00 EAT (03:00 UTC) — start of business hours.
/// Also manually triggerable via:
///   POST /api/admin/sync/sap/sales-orders/format-descriptions
/// </summary>
[DisallowConcurrentExecution]
public class SalesOrderDescriptionFormatJob : IJob
{
    private readonly IServiceScopeFactory             _scopeFactory;
    private readonly ILogger<SalesOrderDescriptionFormatJob> _logger;

    public SalesOrderDescriptionFormatJob(
        IServiceScopeFactory scopeFactory,
        ILogger<SalesOrderDescriptionFormatJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("SalesOrderDescriptionFormatJob: started");

        try
        {
            using var scope   = _scopeFactory.CreateScope();
            var updater = scope.ServiceProvider
                .GetRequiredService<SapSalesOrderLineDescriptionUpdater>();

            updater.UpdateAllOpenSalesOrderDescriptions();

            sw.Stop();
            _logger.LogInformation(
                "SalesOrderDescriptionFormatJob: completed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(
                "SalesOrderDescriptionFormatJob: cancelled | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "SalesOrderDescriptionFormatJob: FAILED | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
            throw;
        }

        return Task.CompletedTask;
    }
}
