using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Caching;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;
using System.Diagnostics;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

[DisallowConcurrentExecution]
public class InvoiceFullSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InvoiceFullSyncJob> _logger;

    public InvoiceFullSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<InvoiceFullSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogWarning("🔥 INVOICE FULL SYNC STARTED");

        using var scope = _scopeFactory.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<SapInvoiceReader>();
        var cache = scope.ServiceProvider.GetRequiredService<InvoiceCacheService>();
        var neonSync = scope.ServiceProvider.GetRequiredService<NeonInvoiceSyncService>();

        try
        {
            // 1️⃣ Read ALL invoices from SAP
            var invoices = reader.ReadAllInvoices().ToList();

            _logger.LogInformation(
                "🧾 Full read from SAP | Headers={Count} Lines={Lines}",
                invoices.Count,
                invoices.Sum(i => i.Lines.Count));

            // 2️⃣ Cache all invoices
            if (invoices.Count > 0)
                await cache.CacheInvoicesAsync(invoices);

            // 3️⃣ Full sync Cache → Neon
            await neonSync.SyncFullAsync();

            sw.Stop();
            _logger.LogWarning(
                "🏁 INVOICE FULL SYNC COMPLETED | Headers={Count} | DurationSec={Sec}",
                invoices.Count, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "❌ INVOICE FULL SYNC FAILED | DurationSec={Sec}",
                sw.Elapsed.TotalSeconds);
            throw;
        }
    }
}
