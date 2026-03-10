using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Quartz job that reads all open SAP Quotations (OQUT, DocStatus='O')
/// and converts each one to a Sales Order (ORDR) via the DI API CopyFrom.
/// </summary>
[DisallowConcurrentExecution]
public class QuotationToSalesOrderJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QuotationToSalesOrderJob> _logger;

    public QuotationToSalesOrderJob(
        IServiceScopeFactory scopeFactory,
        ILogger<QuotationToSalesOrderJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("QuotationToSalesOrder job started");

        using var scope = _scopeFactory.CreateScope();
        var converter   = scope.ServiceProvider.GetRequiredService<SapQuotationConverter>();

        int converted = 0;
        int failed    = 0;

        try
        {
            var openQuotations = converter.ReadOpenQuotations().ToList();

            _logger.LogInformation(
                "Open Quotations found | Count={Count}",
                openQuotations.Count);

            foreach (var quot in openQuotations)
            {
                try
                {
                    converter.ConvertToSalesOrder(quot.DocEntry);
                    converted++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex,
                        "Failed to convert Quotation | DocEntry={DocEntry} | DocNum={DocNum} | Customer={Customer}",
                        quot.DocEntry,
                        quot.DocNum,
                        quot.CardCode);
                }
            }

            sw.Stop();
            _logger.LogInformation(
                "QuotationToSalesOrder job completed | Converted={Converted} | Failed={Failed} | DurationMs={Ms}",
                converted, failed, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "QuotationToSalesOrder job FAILED | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
            throw;
        }

        await Task.CompletedTask;
    }
}
