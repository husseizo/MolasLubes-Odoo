using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

[DisallowConcurrentExecution]
public class OdooInvoicePushJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OdooInvoicePushJob> _logger;

    public OdooInvoicePushJob(
        IServiceScopeFactory scopeFactory,
        ILogger<OdooInvoicePushJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("🧾 Odoo Invoice Push Job started");

        using var scope = _scopeFactory.CreateScope();

        var service = scope.ServiceProvider
            .GetRequiredService<OdooInvoicePushService>();

        await service.PushPendingInvoicesAsync();

        _logger.LogInformation("✅ Odoo Invoice Push Job completed");
    }
}
