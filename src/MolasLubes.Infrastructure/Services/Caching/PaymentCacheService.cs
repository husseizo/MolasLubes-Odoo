using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Caching;

public class PaymentCacheService
{
    private readonly MolasCacheDbContext _db;
    private readonly ILogger<PaymentCacheService> _logger;

    public PaymentCacheService(
        MolasCacheDbContext db,
        ILogger<PaymentCacheService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task CachePaymentAsync(IEnumerable<SapPaymentDto> payments)
    {
        foreach (var p in payments)
        {
            // Use the first RCT2 invoice link as the primary invoice reference.
            // Most payments in this business context apply to exactly one invoice.
            var firstLink = p.Invoices.FirstOrDefault();
            var invoiceDocEntry = firstLink?.InvoiceDocEntry ?? 0;
            var sumApplied     = firstLink?.SumApplied      ?? p.TotalPaid;

            var exists = await _db.Set<CachePayment>().FindAsync(p.DocEntry);

            if (exists != null)
            {
                // Update invoice link in case it was missing on a previous read
                exists.InvoiceDocEntry = invoiceDocEntry;
                exists.SumApplied      = sumApplied;
                exists.TotalPaid       = p.TotalPaid;
                exists.OdooPaymentId   = p.OdooPaymentId;
                exists.OdooStatus      = p.OdooStatus;
                exists.OdooSyncDir     = p.OdooSyncDir;
                exists.OdooErrorMsg    = p.OdooErrorMsg;
                exists.OdooLastSync    = p.OdooLastSync;
                continue;
            }

            _db.Set<CachePayment>().Add(new CachePayment
            {
                SapDocEntry    = p.DocEntry,
                SapDocNum      = p.DocNum,
                CardCode       = p.CardCode,
                DocDate        = p.DocDate,
                TotalPaid      = p.TotalPaid,
                InvoiceDocEntry = invoiceDocEntry,
                SumApplied     = sumApplied,
                OdooPaymentId  = p.OdooPaymentId,
                OdooStatus     = p.OdooStatus,
                OdooSyncDir    = p.OdooSyncDir,
                OdooErrorMsg   = p.OdooErrorMsg,
                OdooLastSync   = p.OdooLastSync
            });
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "💰 Payment cache updated | Count={Count}",
            payments.Count());
    }


    public async Task<bool> HasAnyPaymentAsync()
    {
        return await _db.Set<CachePayment>().AnyAsync();
    }
}