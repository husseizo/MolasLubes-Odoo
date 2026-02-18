using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Caching;
using MolasLubes.Infrastructure.Services.Sync; // 👈 Neon sync
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

[DisallowConcurrentExecution]
public class ProductFullSyncJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductFullSyncJob> _logger;

    public ProductFullSyncJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductFullSyncJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("🚀 Product Full Sync started");

        // 🔥 FULL ISOLATED SCOPE
        using var scope = _scopeFactory.CreateScope();

        var reader = scope.ServiceProvider
            .GetRequiredService<SapProductReader>();

        var cache = scope.ServiceProvider
            .GetRequiredService<ProductCacheService>();

        var neonSync = scope.ServiceProvider
            .GetRequiredService<ProductNeonSyncService>();

        // =============================
        // 1️⃣ READ FROM SAP
        // =============================
        var products = reader.ReadAllProducts();

        _logger.LogInformation(
            "📦 Products fetched from SAP | Count={Count}",
            products.Count);

        if (products.Count == 0)
        {
            _logger.LogWarning("⚠ No products returned from SAP");
            return Task.CompletedTask;
        }

        // =============================
        // 2️⃣ UPSERT TO SQL CACHE
        // =============================
        cache.UpsertProductsAsync(products)
             .GetAwaiter()
             .GetResult();

        _logger.LogInformation(
            "🗄 Products cached successfully | Count={Count}",
            products.Count);

        // =============================
        // 3️⃣ DELTA SYNC CACHE → NEON
        // =============================
        neonSync.SyncDeltaAsync()   // ✅ FIXED HERE
                 .GetAwaiter()
                 .GetResult();

        _logger.LogInformation("✅ Product Full Sync completed");

        return Task.CompletedTask;
    }
}