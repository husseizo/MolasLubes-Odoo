using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Caching;
using MolasLubes.Infrastructure.Services.Sync;
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
        _logger.LogInformation("🧾 Invoice Sync started (OINV + INV1)");

        using var scope = _scopeFactory.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<SapInvoiceReader>();
        var cache = scope.ServiceProvider.GetRequiredService<InvoiceCacheService>();
        var neonSync = scope.ServiceProvider.GetRequiredService<NeonInvoiceSyncService>();

        // Use the last known DocDate from the cache as the SAP delta cursor.
        // Falls back to a full read when the cache is empty (first run).
        // A 1-day overlap is already applied inside GetLastSapSyncDateAsync.
        var fromDate = await cache.GetLastSapSyncDateAsync();

        var invoices = fromDate.HasValue
            ? reader.ReadInvoices(fromDate.Value).ToList()
            : reader.ReadAllInvoices().ToList();

        _logger.LogInformation(
            "🧾 Invoices read from SAP | FromDate={FromDate} Headers={Count} Lines={Lines}",
            fromDate?.ToString("yyyy-MM-dd") ?? "ALL",
            invoices.Count,
            invoices.Sum(i => i.Lines.Count));

        if (invoices.Count > 0)
            await cache.CacheInvoicesAsync(invoices);

        // 🔄 Sync cached invoices + lines → Neon (delta + orphan backfill)
        await neonSync.SyncDeltaAsync();

        _logger.LogInformation("✅ Invoice Sync completed (OINV + INV1)");
    }
}