using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

[DisallowConcurrentExecution]
public class OdooPaymentPushJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OdooPaymentPushJob> _logger;

    public OdooPaymentPushJob(
        IServiceScopeFactory scopeFactory,
        ILogger<OdooPaymentPushJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("💳 Odoo Payment Push Job started");

        using var scope = _scopeFactory.CreateScope();

        var service = scope.ServiceProvider
            .GetRequiredService<OdooPaymentPushService>();

        await service.PushPendingPaymentsAsync();

        _logger.LogInformation("✅ Odoo Payment Push Job completed");
    }
}
