using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Persistence;

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
    // 🔥 TRUE DELTA UPSERT (HEADER + LINES)
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

        // Single batch load of headers + their lines
        var existingMap = await _db.CacheDeliveries
            .Include(x => x.Lines)
            .Where(x => docEntries.Contains(x.SapDocEntry))
            .ToDictionaryAsync(x => x.SapDocEntry);

        var inserted = 0;
        var updated  = 0;
        var skipped  = 0;

        foreach (var d in list)
        {
            if (!existingMap.TryGetValue(d.DocEntry, out var row))
            {
                // ➕ INSERT header + lines
                var header = new CacheDelivery
                {
                    SapDocEntry    = d.DocEntry,
                    SapDocNum      = d.DocNum,
                    CardCode       = d.CardCode,
                    DeliveryDate   = d.DocDate,
                    BaseOrderEntry = d.BaseOrderEntry,
                    DeliveredQty   = d.DeliveredQuantity,
                    SapUpdateDate  = d.SapUpdateDate,
                    IsCancelled    = d.IsCancelled,
                    LastSapSyncAt  = now
                };

                foreach (var l in d.Lines)
                    header.Lines.Add(BuildLine(d.DocEntry, l));

                _db.CacheDeliveries.Add(header);
                inserted++;
            }
            else
            {
                // 🔥 Skip if SAP version is not newer AND lines are already populated.
                // If lines are missing (e.g. table was just created), always process
                // so we backfill line data even for headers that haven't changed in SAP.
                if (d.SapUpdateDate <= row.SapUpdateDate && row.Lines.Count > 0)
                {
                    skipped++;
                    continue;
                }

                // Update header
                row.SapDocNum      = d.DocNum;
                row.CardCode       = d.CardCode;
                row.DeliveryDate   = d.DocDate;
                row.BaseOrderEntry = d.BaseOrderEntry;
                row.DeliveredQty   = d.DeliveredQuantity;
                row.SapUpdateDate  = d.SapUpdateDate;
                row.IsCancelled    = d.IsCancelled;
                row.LastSapSyncAt  = now;

                // Smart line merge — preserve Odoo UDFs written by external systems.
                // Match on (SapDocEntry, LineNum) which is the stable SAP position key.
                var existingLineMap = row.Lines
                    .ToDictionary(l => l.LineNum);

                var mergedLines = new List<CacheDeliveryLine>();

                foreach (var l in d.Lines)
                {
                    existingLineMap.TryGetValue(l.LineNum, out var existing);

                    mergedLines.Add(new CacheDeliveryLine
                    {
                        SapDocEntry   = d.DocEntry,
                        LineNum       = l.LineNum,
                        ItemCode      = l.ItemCode,
                        Description   = l.Description,
                        Quantity      = l.Quantity,
                        LineTotal     = l.LineTotal,
                        GrossBuyPr    = l.GrossBuyPr,
                        BaseEntry     = l.BaseEntry,
                        BaseLine      = l.BaseLine,

                        // Prefer non-null SAP value over cached value
                        OdooMoveId           = l.OdooMoveId           ?? existing?.OdooMoveId,
                        OdooSalesOrderLineId = l.OdooSalesOrderLineId ?? existing?.OdooSalesOrderLineId,
                        OdooStatus           = l.OdooStatus           ?? existing?.OdooStatus,
                        OdooSyncDir          = l.OdooSyncDir          ?? existing?.OdooSyncDir,
                        OdooErrorMsg         = l.OdooErrorMsg         ?? existing?.OdooErrorMsg,
                        OdooLastSync         = l.OdooLastSync         ?? existing?.OdooLastSync,
                    });
                }

                _db.CacheDeliveryLines.RemoveRange(row.Lines);
                row.Lines = mergedLines;

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

    public async Task<bool> HasAnyDeliveryLineAsync()
    {
        return await _db.CacheDeliveryLines.AnyAsync();
    }

    // =====================================================
    // HELPERS
    // =====================================================
    private static CacheDeliveryLine BuildLine(int docEntry, SapDeliveryLineDto l)
        => new()
        {
            SapDocEntry          = docEntry,
            LineNum              = l.LineNum,
            ItemCode             = l.ItemCode,
            Description          = l.Description,
            Quantity             = l.Quantity,
            LineTotal            = l.LineTotal,
            GrossBuyPr           = l.GrossBuyPr,
            BaseEntry            = l.BaseEntry,
            BaseLine             = l.BaseLine,
            OdooMoveId           = l.OdooMoveId,
            OdooSalesOrderLineId = l.OdooSalesOrderLineId,
            OdooStatus           = l.OdooStatus,
            OdooSyncDir          = l.OdooSyncDir,
            OdooErrorMsg         = l.OdooErrorMsg,
            OdooLastSync         = l.OdooLastSync,
        };
}
