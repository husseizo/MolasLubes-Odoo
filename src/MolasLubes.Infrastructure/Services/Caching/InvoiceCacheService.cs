using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Domain.Entities.Cache; // ✅ FIX HII
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
        foreach (var inv in invoices)
        {
            var exists = await _db.CacheInvoices.FindAsync(inv.DocEntry);
            if (exists != null)
                continue;

            _db.CacheInvoices.Add(new CacheInvoice
            {
                SapDocEntry = inv.DocEntry,
                SapDocNum = inv.DocNum,
                CardCode = inv.CardCode,
                DocDate = inv.DocDate,
                DocTotal = inv.DocTotal,
                VatSum = inv.VatSum
            });

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
            invoices.Count());
    }


    public async Task<bool> HasAnyInvoiceAsync()
    {
        return await _db.CacheInvoices.AnyAsync();
    }
}