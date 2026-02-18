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
            var exists = await _db.Set<CachePayment>().FindAsync(p.DocEntry);
            if (exists != null) continue;

            _db.Set<CachePayment>().Add(new CachePayment
            {
                SapDocEntry = p.DocEntry,
                SapDocNum = p.DocNum,
                CardCode = p.CardCode,
                DocDate = p.DocDate,
                TotalPaid = p.TotalPaid
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