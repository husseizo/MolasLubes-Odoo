using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Caching;

public class ProductCacheService
{
    private readonly MolasCacheDbContext _db;
    private readonly ILogger<ProductCacheService> _logger;

    public ProductCacheService(
        MolasCacheDbContext db,
        ILogger<ProductCacheService> logger)
    {
        _db = db;
        _logger = logger;
    }



    public async Task ClearAllProductsFastAsync()
    {
        _logger.LogWarning("🧹 Clearing CacheProducts table before FULL sync");

        await _db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE CacheProducts");

        _logger.LogWarning("🧹 CacheProducts table cleared");
    }

    // ============================
    // 🔄 FULL / DELTA UPSERT
    // ============================
    public async Task UpsertProductsAsync(IEnumerable<SapProductDto> products)
    {
        _logger.LogInformation("🗄️ Starting PRODUCT cache upsert");

        var inserted = 0;
        var updated = 0;
        var now = DateTime.UtcNow;

        foreach (var p in products)
        {
            // 💰 SAFE price extraction
            p.PriceLists.TryGetValue(1, out var price1);
            p.PriceLists.TryGetValue(2, out var price2);
            p.PriceLists.TryGetValue(3, out var price3);

            var cache = await _db.CacheProducts.FindAsync(
            p.ItemCode,
            p.WarehouseCode);

            if (cache == null)
            {
                cache = new CacheProduct
                {
                    ItemCode = p.ItemCode,
                    WarehouseCode = p.WarehouseCode,
                    ItemName = p.ItemName,

                    OnHandSap = p.OnHand,
                    AvailableCache = p.OnHand,
                    IsActive = true,

                    Barcode = p.Barcode,   // ✅ FIX ADDED

                    PriceList_1 = price1,
                    PriceList_2 = price2,
                    PriceList_3 = price3,

                    // 🔗 ODOO
                    OdooProductId = p.OdooProductId,
                    OdooStatus = p.OdooStatus ?? "SYNCED",
                    OdooErrorMsg = p.OdooErrorMsg,
                    OdooSyncDir = p.OdooSyncDir ?? "FROM_SAP",
                    OdooLastSync = p.OdooLastSync ?? now,

                    LastSapSyncAt = now
                };

                _db.CacheProducts.Add(cache);
                inserted++;
            }
            else
            {
                cache.ItemName = p.ItemName;

                cache.OnHandSap = p.OnHand;
                cache.AvailableCache = p.OnHand;
                cache.IsActive = true;

                cache.Barcode = p.Barcode;

                cache.PriceList_1 = price1;
                cache.PriceList_2 = price2;
                cache.PriceList_3 = price3;

                // 🔗 ODOO
                cache.OdooProductId = p.OdooProductId;
                cache.OdooStatus = p.OdooStatus ?? cache.OdooStatus;
                cache.OdooErrorMsg = p.OdooErrorMsg;
                cache.OdooSyncDir = p.OdooSyncDir ?? cache.OdooSyncDir;
                cache.OdooLastSync = p.OdooLastSync ?? now;

                cache.LastSapSyncAt = now;
                updated++;
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "🗄️ Product cache upsert completed | Inserted={Inserted} | Updated={Updated}",
            inserted,
            updated);
    }
}