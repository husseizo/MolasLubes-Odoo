using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Caching;

public class InvoiceCacheService
{
    private readonly MolasCacheDbContext _db;
    private readonly SalesOrderStatusService _orderStatus;
    private readonly ILogger<InvoiceCacheService> _logger;

    public InvoiceCacheService(
        MolasCacheDbContext db,
        SalesOrderStatusService orderStatus,
        ILogger<InvoiceCacheService> logger)
    {
        _db = db;
        _orderStatus = orderStatus;
        _logger = logger;
    }

    public async Task CacheInvoicesAsync(IEnumerable<SapInvoiceDto> invoices)
    {
        var list = invoices.ToList();

        foreach (var inv in list)
        {
            var exists = await _db.CacheInvoices
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.SapDocEntry == inv.DocEntry);

            if (exists != null)
            {
                // 🔁 Update header fields
                exists.SapDocNum   = inv.DocNum;
                exists.CardCode    = inv.CardCode;
                exists.CardName    = inv.CardName;
                exists.DocDate     = inv.DocDate;
                exists.DocTotal    = inv.DocTotal;
                exists.VatSum      = inv.VatSum;
                exists.OdooInvoiceId = inv.OdooInvoiceId;
                exists.OdooStatus    = inv.OdooStatus;
                exists.OdooSyncDir   = inv.OdooSyncDir;
                exists.OdooErrorMsg  = inv.OdooErrorMsg;
                exists.OdooLastSync  = inv.OdooLastSync;
                exists.CachedAt      = DateTime.UtcNow;

                // 🔁 Smart line merge — preserve Odoo UDFs written back by external
                //    systems (e.g. Odoo confirming the line was synced).
                //    Match on BaseEntry+BaseLine which uniquely identifies the
                //    source delivery line and is stable across SAP re-reads.
                var existingLineMap = exists.Lines
                    .ToDictionary(l => (l.BaseEntry, l.BaseLine));

                var mergedLines = new List<CacheInvoiceLine>();

                foreach (var l in inv.Lines)
                {
                    existingLineMap.TryGetValue((l.BaseEntry, l.BaseLine), out var existing);

                    mergedLines.Add(new CacheInvoiceLine
                    {
                        SapDocEntry = inv.DocEntry,
                        ItemCode    = l.ItemCode,
                        Description = l.Description,
                        Quantity    = l.Quantity,
                        LineTotal   = l.LineTotal,
                        GrossBuyPr  = l.GrossBuyPr,
                        BaseEntry   = l.BaseEntry,
                        BaseLine    = l.BaseLine,

                        // Prefer the value that is non-null (SAP UDF > cached UDF)
                        OdooInvoiceLineId = l.OdooInvoiceLineId ?? existing?.OdooInvoiceLineId,
                        OdooStatus        = l.OdooStatus        ?? existing?.OdooStatus,
                        OdooSyncDir       = l.OdooSyncDir       ?? existing?.OdooSyncDir,
                        OdooErrorMsg      = l.OdooErrorMsg      ?? existing?.OdooErrorMsg,
                        OdooLastSync      = l.OdooLastSync      ?? existing?.OdooLastSync,
                    });
                }

                // Remove old lines and replace with merged set
                _db.CacheInvoiceLines.RemoveRange(exists.Lines);
                exists.Lines = mergedLines;

                continue;
            }

            var header = new CacheInvoice
            {
                SapDocEntry = inv.DocEntry,
                SapDocNum   = inv.DocNum,
                CardCode    = inv.CardCode,
                CardName    = inv.CardName,
                DocDate     = inv.DocDate,
                DocTotal    = inv.DocTotal,
                VatSum      = inv.VatSum,
                OdooInvoiceId = inv.OdooInvoiceId,
                OdooStatus    = inv.OdooStatus,
                OdooSyncDir   = inv.OdooSyncDir,
                OdooErrorMsg  = inv.OdooErrorMsg,
                OdooLastSync  = inv.OdooLastSync
            };

            // 📦 LINES (INV1)
            foreach (var line in inv.Lines)
            {
                header.Lines.Add(new CacheInvoiceLine
                {
                    SapDocEntry       = inv.DocEntry,
                    ItemCode          = line.ItemCode,
                    Description       = line.Description,
                    Quantity          = line.Quantity,
                    LineTotal         = line.LineTotal,
                    GrossBuyPr        = line.GrossBuyPr,
                    BaseEntry         = line.BaseEntry,
                    BaseLine          = line.BaseLine,
                    OdooInvoiceLineId = line.OdooInvoiceLineId,
                    OdooStatus        = line.OdooStatus,
                    OdooSyncDir       = line.OdooSyncDir,
                    OdooErrorMsg      = line.OdooErrorMsg,
                    OdooLastSync      = line.OdooLastSync
                });
            }

            _db.CacheInvoices.Add(header);

            // 🔁 CLOSE RELATED SALES ORDERS
            foreach (var line in inv.Lines)
            {
                if (line.BaseEntry > 0)
                    await _orderStatus.MarkOrderDeliveredAsync(line.BaseEntry);
            }
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "🧾 Invoice cache updated | Count={Count}",
            list.Count);
    }

    public async Task<bool> HasAnyInvoiceAsync()
    {
        return await _db.CacheInvoices.AnyAsync();
    }

    // Returns the most recent DocDate in the cache minus a 1-day overlap buffer.
    // Used by InvoiceSyncJob as a cursor for SAP delta reads so we don't rely
    // on a hardcoded -2 days window.
    public async Task<DateTime?> GetLastSapSyncDateAsync()
    {
        var maxDate = await _db.CacheInvoices
            .MaxAsync(x => (DateTime?)x.DocDate);

        // Subtract 1 day as safety overlap (handles same-day invoices added
        // after the last sync ran)
        return maxDate?.AddDays(-1);
    }
}
