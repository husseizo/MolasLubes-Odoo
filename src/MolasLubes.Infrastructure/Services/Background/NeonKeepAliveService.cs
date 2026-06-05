using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Persistence;
using Npgsql;

namespace MolasLubes.Infrastructure.Services.Background;

public class NeonKeepAliveService : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan PingInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

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
                await PingWithRetryAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Neon keep-alive service stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Neon keep-alive failed after retries");
            }

            try
            {
                await Task.Delay(PingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PingWithRetryAsync(CancellationToken stoppingToken)
    {
        Exception? lastTransient = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NeonDbContext>();

                await db.Database.ExecuteSqlRawAsync("SELECT 1", stoppingToken);

                _logger.LogInformation("Neon keep-alive ping successful");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientNeonFailure(ex))
            {
                lastTransient = ex;

                if (attempt == MaxAttempts)
                    break;

                _logger.LogWarning(
                    ex,
                    "Neon keep-alive transient failure, retrying | Attempt={Attempt}/{MaxAttempts} | DelaySeconds={DelaySeconds}",
                    attempt,
                    MaxAttempts,
                    RetryDelay.TotalSeconds);

                await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        throw lastTransient ?? new InvalidOperationException(
            "Neon keep-alive failed without a captured transient exception.");
    }

    private static bool IsTransientNeonFailure(Exception ex) =>
        ex is TimeoutException
        || ex is NpgsqlException
        || ex.InnerException is TimeoutException
        || ex.InnerException is NpgsqlException;
}
