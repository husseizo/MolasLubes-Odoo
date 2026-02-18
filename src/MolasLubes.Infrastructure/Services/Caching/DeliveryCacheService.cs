using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Persistence;
using System.Linq;

namespace MolasLubes.Infrastructure.Services.Caching;

public class DeliveryCacheService
{
    private readonly MolasCacheDbContext _db;
    private readonly ILogger<DeliveryCacheService> _logger;

    public DeliveryCacheService(
        MolasCacheDbContext db,
        ILogger<DeliveryCacheService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // =====================================================
    // 🔥 TRUE DELTA UPSERT
    // =====================================================
    public async Task RegisterDeliveriesAsync(IEnumerable<SapDeliveryDto> deliveries)
    {
        var list = deliveries.ToList();

        if (list.Count == 0)
        {
            _logger.LogInformation("ℹ No deliveries to register");
            return;
        }

        var now = DateTime.UtcNow;

        var docEntries = list.Select(d => d.DocEntry).ToList();

        var existingMap = await _db.CacheDeliveries
            .Where(x => docEntries.Contains(x.SapDocEntry))
            .ToDictionaryAsync(x => x.SapDocEntry);

        var inserted = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var d in list)
        {
            if (!existingMap.TryGetValue(d.DocEntry, out var row))
            {
                // ➕ INSERT
                _db.CacheDeliveries.Add(new CacheDelivery
                {
                    SapDocEntry = d.DocEntry,
                    SapDocNum = d.DocNum,
                    CardCode = d.CardCode,
                    DeliveryDate = d.DocDate,
                    BaseOrderEntry = d.BaseOrderEntry,
                    DeliveredQty = d.DeliveredQuantity,

                    SapUpdateDate = d.SapUpdateDate,
                    IsCancelled = d.IsCancelled,
                    LastSapSyncAt = now
                });

                inserted++;
            }
            else
            {
                // 🔥 ONLY update if SAP version is newer
                if (d.SapUpdateDate <= row.SapUpdateDate)
                {
                    skipped++;
                    continue;
                }

                row.SapDocNum = d.DocNum;
                row.CardCode = d.CardCode;
                row.DeliveryDate = d.DocDate;
                row.BaseOrderEntry = d.BaseOrderEntry;
                row.DeliveredQty = d.DeliveredQuantity;

                row.SapUpdateDate = d.SapUpdateDate;
                row.IsCancelled = d.IsCancelled;
                row.LastSapSyncAt = now;

                updated++;
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "📦 Delivery cache delta sync | Total={Total} | Inserted={Inserted} | Updated={Updated} | Skipped={Skipped}",
            list.Count,
            inserted,
            updated,
            skipped);
    }

    // =====================================================
    // 🔥 WATERMARK FOR NEXT DELTA
    // =====================================================
    public async Task<DateTime?> GetLastSapUpdateDateAsync()
    {
        return await _db.CacheDeliveries
            .Where(x => x.SapUpdateDate > new DateTime(1900, 1, 1))
            .OrderByDescending(x => x.SapUpdateDate)
            .Select(x => (DateTime?)x.SapUpdateDate)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> HasAnyDeliveryAsync()
    {
        return await _db.CacheDeliveries.AnyAsync();
    }
}