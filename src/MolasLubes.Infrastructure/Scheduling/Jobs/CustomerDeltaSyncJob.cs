using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Caching;
using Quartz;
using System.Diagnostics;

[DisallowConcurrentExecution]
public class CustomerDeltaSyncJob : IJob
{
    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CustomerDeltaSyncJob> _logger;

    public CustomerDeltaSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<CustomerDeltaSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("🚀 Customer Sync started");

        using var scope = _scopeFactory.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<SapCustomerReader>();
        var cache  = scope.ServiceProvider.GetRequiredService<CustomerCacheService>();

        try
        {
            var hasAny    = await cache.HasAnyCustomerAsync();
            var watermark = hasAny ? await cache.GetLastSapUpdateDateAsync() : null;

            // =====================================================
            // FULL SYNC — streamed: read batch → upsert → repeat
            // =====================================================
            if (!hasAny || watermark == null)
            {
                _logger.LogWarning("🟡 Cache empty or no watermark → FULL sync (streaming)");
                await RunFullSyncAsync(reader, cache);
            }
            else
            {
                // =====================================================
                // DELTA
                // =====================================================
                var fromDate  = watermark.Value.AddMinutes(-5);
                _logger.LogInformation("🔄 Running DELTA from UpdateDate >= {FromDate}", fromDate);

                var customers = reader.ReadCustomersDelta(fromDate);

                // =====================================================
                // SAFETY FALLBACK: delta empty but cache suspiciously small
                // =====================================================
                if (customers.Count == 0)
                {
                    var cacheCount = (await cache.GetCustomersAsync(1, 1_000_000)).Total;
                    if (cacheCount < 100)
                    {
                        _logger.LogWarning(
                            "⚠ DELTA returned 0 and cache count suspiciously low ({Count}) → FORCING FULL SYNC",
                            cacheCount);
                        await RunFullSyncAsync(reader, cache);
                        goto done;
                    }
                }

                _logger.LogInformation("👥 Customers read from SAP | Count={Count}", customers.Count);

                if (customers.Count > 0)
                    await cache.UpsertCustomersAsync(customers);
            }

            done:
            sw.Stop();
            _logger.LogInformation(
                "✅ Customer Sync completed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "❌ Customer Sync failed | DurationMs={Ms}", sw.ElapsedMilliseconds);
            throw;
        }
    }

    // =====================================================
    // Streams full customer data in batches so memory stays
    // bounded and the cache is populated progressively.
    // =====================================================
    private async Task RunFullSyncAsync(SapCustomerReader reader, CustomerCacheService cache)
    {
        string lastCardCode = "";
        int totalRead       = 0;
        int batchNum        = 0;

        while (true)
        {
            batchNum++;
            var batch = reader.ReadCustomerBatchAfter(lastCardCode, BatchSize);

            if (batch.Count == 0)
                break;

            await cache.UpsertCustomersAsync(batch);
            totalRead    += batch.Count;
            lastCardCode  = batch[^1].CardCode;

            _logger.LogInformation(
                "📦 Full sync batch {Batch} upserted | BatchCount={Count} | TotalSoFar={Total}",
                batchNum, batch.Count, totalRead);

            if (batch.Count < BatchSize)
                break;
        }

        _logger.LogInformation("✅ Full sync complete | TotalCustomers={Total}", totalRead);
    }
}
