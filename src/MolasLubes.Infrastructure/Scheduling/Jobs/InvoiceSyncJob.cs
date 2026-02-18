using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Caching;
using Quartz;

[DisallowConcurrentExecution]
public class InvoiceSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InvoiceSyncJob> _logger;

    public InvoiceSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<InvoiceSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("🧾 Invoice Sync started");

        using var scope = _scopeFactory.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<SapInvoiceReader>();
        var cache = scope.ServiceProvider.GetRequiredService<InvoiceCacheService>();

        var hasAny = await cache.HasAnyInvoiceAsync();

        var invoices = hasAny
            ? reader.ReadInvoices(DateTime.UtcNow.AddDays(-2)).ToList()
            : reader.ReadAllInvoices().ToList();

        _logger.LogInformation("🧾 Invoices read from SAP | Count={Count}", invoices.Count);

        if (invoices.Count > 0)
            await cache.CacheInvoicesAsync(invoices);

        _logger.LogInformation("✅ Invoice Sync completed");
    }
}