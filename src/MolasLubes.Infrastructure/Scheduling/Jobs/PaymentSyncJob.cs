using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Caching;
using Quartz;

[DisallowConcurrentExecution]
public class PaymentSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentSyncJob> _logger;

    public PaymentSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("💰 Payment Sync started");

        using var scope = _scopeFactory.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<SapPaymentReader>();
        var cache = scope.ServiceProvider.GetRequiredService<PaymentCacheService>();

        var hasAny = await cache.HasAnyPaymentAsync();

        var payments = hasAny
            ? reader.ReadPayments(DateTime.UtcNow.AddDays(-2)).ToList()
            : reader.ReadAllPayments().ToList();

        _logger.LogInformation("💰 Payments read from SAP | Count={Count}", payments.Count);

        if (payments.Count > 0)
            await cache.CachePaymentAsync(payments);

        _logger.LogInformation("✅ Payment Sync completed");
    }
}