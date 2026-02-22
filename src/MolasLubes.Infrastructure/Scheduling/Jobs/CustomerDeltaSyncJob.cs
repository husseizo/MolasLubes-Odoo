using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Services.Caching;
using Quartz;
using System.Diagnostics;

[DisallowConcurrentExecution]
public class CustomerDeltaSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CustomerDeltaSyncJob> _logger;

    public CustomerDeltaSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<CustomerDeltaSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("🚀 Customer Sync started");

        using var scope = _scopeFactory.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<SapCustomerReader>();
        var cache = scope.ServiceProvider.GetRequiredService<CustomerCacheService>();

        try
        {
            List<SapCustomerDto> customers;

            var hasAny = await cache.HasAnyCustomerAsync();

            // =====================================================
            // 🟡 FIRST RUN → FULL SYNC
            // =====================================================
            if (!hasAny)
            {
                _logger.LogWarning("🟡 Cache empty → FULL sync");
                customers = reader.ReadAllCustomers().ToList();
            }
            else
            {
                var watermark = await cache.GetLastSapUpdateDateAsync();

                if (watermark == null)
                {
                    _logger.LogWarning("🟡 No watermark found → FULL sync");
                    customers = reader.ReadAllCustomers().ToList();
                }
                else
                {
                    var fromDate = watermark.Value.AddMinutes(-5);

                    _logger.LogInformation(
                        "🔄 Running DELTA from UpdateDate >= {FromDate}",
                        fromDate);

                    customers = reader.ReadCustomersDelta(fromDate).ToList();

                    // =====================================================
                    // 🛑 SAFETY FALLBACK
                    // If DELTA returned 0 but cache is too small
                    // =====================================================
                    if (customers.Count == 0)
                    {
                        var cacheCount = (await cache.GetCustomersAsync(1, 1_000_000)).Total;

                        if (cacheCount < 100) // threshold safety
                        {
                            _logger.LogWarning(
                                "⚠ DELTA returned 0 and cache count suspiciously low ({Count}) → FORCING FULL SYNC",
                                cacheCount);

                            customers = reader.ReadAllCustomers().ToList();
                        }
                    }
                }
            }

            _logger.LogInformation(
                "👥 Customers read from SAP | Count={Count}",
                customers.Count);

            if (customers.Count > 0)
            {
                await cache.UpsertCustomersAsync(customers);
            }

            sw.Stop();
            _logger.LogInformation(
                "✅ Customer Sync completed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "❌ Customer Sync failed | DurationMs={Ms}",
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}