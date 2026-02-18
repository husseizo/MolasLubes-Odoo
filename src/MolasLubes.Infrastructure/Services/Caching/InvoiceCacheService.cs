using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Domain.Entities.Cache; // ✅ FIX HII
using MolasLubes.Domain.Entities.Invoices;
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
                // 🔁 Replace lines on update
                _db.CacheInvoiceLines.RemoveRange(exists.Lines);

                exists.Lines = inv.Lines.Select(l => new CacheInvoiceLine
                {
                    SapDocEntry = inv.DocEntry,
                    ItemCode = l.ItemCode,
                    Quantity = l.Quantity,
                    LineTotal = l.LineTotal,
                    BaseEntry = l.BaseEntry,
                    BaseLine = l.BaseLine,
                    OdooInvoiceLineId = l.OdooInvoiceLineId
                }).ToList();

                continue;
            }

            var header = new CacheInvoice
            {
                SapDocEntry = inv.DocEntry,
                SapDocNum = inv.DocNum,
                CardCode = inv.CardCode,
                DocDate = inv.DocDate,
                DocTotal = inv.DocTotal,
                VatSum = inv.VatSum
            };

            // 📦 LINES (INV1)
            foreach (var line in inv.Lines)
            {
                header.Lines.Add(new CacheInvoiceLine
                {
                    SapDocEntry = inv.DocEntry,
                    ItemCode = line.ItemCode,
                    Quantity = line.Quantity,
                    LineTotal = line.LineTotal,
                    BaseEntry = line.BaseEntry,
                    BaseLine = line.BaseLine,
                    OdooInvoiceLineId = line.OdooInvoiceLineId
                });
            }

            _db.CacheInvoices.Add(header);

            // 🔁 CLOSE RELATED SALES ORDERS
            foreach (var line in inv.Lines)
            {
                if (line.BaseEntry > 0)
                {
                    await _orderStatus.MarkOrderDeliveredAsync(line.BaseEntry);
                }
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
}