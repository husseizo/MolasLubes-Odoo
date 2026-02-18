using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

namespace MolasLubes.Infrastructure.Services.Caching;

public class SalesOrderCacheService
{
    private readonly MolasCacheDbContext _db;
    private readonly ILogger<SalesOrderCacheService> _logger;

    public SalesOrderCacheService(
        MolasCacheDbContext db,
        ILogger<SalesOrderCacheService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> HasAnySalesOrderAsync()
    {
        return await _db.CacheSalesOrders.AnyAsync();
    }

    public async Task UpsertSalesOrdersAsync(
        IEnumerable<SapSalesOrderDto> orders)
    {
        var list = orders.ToList();

        if (!list.Any())
        {
            _logger.LogInformation("ℹ No sales orders to cache");
            return;
        }

        var docEntries = list.Select(x => x.DocEntry).ToList();

        var existingOrders = await _db.CacheSalesOrders
            .Include(o => o.Lines)
            .Where(o => docEntries.Contains(o.SapDocEntry))
            .ToDictionaryAsync(o => o.SapDocEntry);

        var inserted = 0;
        var updated = 0;
        var now = DateTime.UtcNow;

        foreach (var o in list)
        {
            if (!existingOrders.TryGetValue(o.DocEntry, out var cache))
            {
                cache = new CacheSalesOrder
                {
                    SapDocEntry = o.DocEntry,
                    SapDocNum = o.DocNum,
                    CustomerCode = o.CardCode,   // ✅ FIXED
                    DocStatus = o.DocStatus,
                    OdooSalesOrderId = o.OdooSalesOrderId,
                    OdooStatus = o.OdooStatus,
                    OdooErrorMsg = o.OdooErrorMsg,
                    OdooSyncDir = o.OdooSyncDir,
                    OdooLastSync = o.OdooLastSync,
                    CreatedAt = now
                };

                foreach (var l in o.Lines)
                {
                    cache.Lines.Add(new CacheSalesOrderLine
                    {
                        SapDocEntry = o.DocEntry,
                        ItemCode = l.ItemCode,
                        Quantity = l.Quantity
                    });
                }

                _db.CacheSalesOrders.Add(cache);
                inserted++;
            }
            else
            {
                cache.DocStatus = o.DocStatus;
                cache.OdooSalesOrderId = o.OdooSalesOrderId;
                cache.OdooStatus = o.OdooStatus;
                cache.OdooErrorMsg = o.OdooErrorMsg;
                cache.OdooSyncDir = o.OdooSyncDir;
                cache.OdooLastSync = o.OdooLastSync;
                cache.LastUpdatedAt = now;

                // 🔁 Replace lines
                _db.CacheSalesOrderLines.RemoveRange(cache.Lines);

                cache.Lines = o.Lines.Select(l =>
                    new CacheSalesOrderLine
                    {
                        SapDocEntry = o.DocEntry,
                        ItemCode = l.ItemCode,
                        Quantity = l.Quantity
                    }).ToList();

                updated++;
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "🛒 SalesOrder cache sync completed | Total={Total} | Inserted={Inserted} | Updated={Updated}",
            list.Count,
            inserted,
            updated);
    }
}