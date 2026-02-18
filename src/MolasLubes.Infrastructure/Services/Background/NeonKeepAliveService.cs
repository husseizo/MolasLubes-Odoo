using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Background;

public class NeonKeepAliveService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NeonKeepAliveService> _logger;

    public NeonKeepAliveService(
        IServiceScopeFactory scopeFactory,
        ILogger<NeonKeepAliveService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NeonDbContext>();

                await db.Database.ExecuteSqlRawAsync("SELECT 1", stoppingToken);

                _logger.LogInformation("🔥 Neon keep-alive ping successful");
            }
            catch (OperationCanceledException)
            {
                // ✅ Expected during shutdown
                _logger.LogInformation("🛑 Neon keep-alive service stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Neon keep-alive failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // ✅ Expected during shutdown
                break;
            }
        }
    }
    }