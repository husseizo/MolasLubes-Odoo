using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Caching;

public class CustomerCacheService
{
    private readonly MolasCacheDbContext _db;
    private readonly ILogger<CustomerCacheService> _logger;

    public CustomerCacheService(
        MolasCacheDbContext db,
        ILogger<CustomerCacheService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // =====================================================
    // CHECK IF ANY
    // =====================================================
    public async Task<bool> HasAnyCustomerAsync()
        => await _db.CacheCustomers.AsNoTracking().AnyAsync();

    // =====================================================
    // GET SINGLE
    // =====================================================
    public async Task<CacheCustomer?> GetCustomerAsync(string cardCode)
        => await _db.CacheCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CardCode == cardCode);

    // =====================================================
    // GET PAGED + SEARCH
    // Returns total count alongside the page items so callers
    // can build pagination UIs without a separate count call.
    // =====================================================
    public async Task<(int Total, IReadOnlyList<CacheCustomer> Items)> GetCustomersAsync(
        int page,
        int pageSize,
        string? search = null,
        bool? activeOnly = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 500) pageSize = 50;

        var query = _db.CacheCustomers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(x =>
                x.CardCode.ToLower().Contains(s) ||
                x.CardName.ToLower().Contains(s));
        }

        if (activeOnly == true)
            query = query.Where(x => x.IsActive);

        var total = await query.CountAsync();

        var items = await query
            .OrderBy(x => x.CardCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (total, items);
    }

    // =====================================================
    // BULK UPSERT (ENTERPRISE SAFE)
    // =====================================================
    public async Task UpsertCustomersAsync(IEnumerable<SapCustomerDto> customers)
    {
        var now = DateTime.UtcNow;
        var list = customers
            .Where(x => !string.IsNullOrWhiteSpace(x.CardCode))
            .GroupBy(x => x.CardCode)
            .Select(g => g.First())
            .ToList();

        if (list.Count == 0)
        {
            _logger.LogWarning("⚠ UpsertCustomersAsync called with empty list");
            return;
        }

        _logger.LogInformation(
            "🗄️ Starting CUSTOMER cache upsert | BatchSize={BatchSize}",
            list.Count);

        var originalAutoDetect = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            var inserted = 0;
            var updated = 0;

            var cardCodes = list.Select(x => x.CardCode).ToList();

            var existingCustomers = await _db.CacheCustomers
                .AsNoTracking()
                .Where(x => cardCodes.Contains(x.CardCode))
                .ToDictionaryAsync(x => x.CardCode);

            foreach (var c in list)
            {
                if (existingCustomers.TryGetValue(c.CardCode, out var existing))
                {
                    var entity = new CacheCustomer
                    {
                        CardCode = existing.CardCode,
                        CardName = c.CardName,
                        IsActive = c.IsActive,  // propagate from SAP Inactive/Frozen flags

                        PriceList = c.PriceList,
                        SlpCode = c.SlpCode,

                        Phone1 = c.Phone1,
                        Phone2 = c.Phone2,
                        Email = c.Email,

                        BillToStreet = c.BillToStreet,
                        BillToCity = c.BillToCity,
                        BillToCountry = c.BillToCountry,

                        ShipToStreet = c.ShipToStreet,
                        ShipToCity = c.ShipToCity,
                        ShipToCountry = c.ShipToCountry,

                        OdooPartnerId = c.OdooPartnerId,
                        OdooStatus = c.OdooStatus ?? existing.OdooStatus,
                        OdooErrorMsg = c.OdooErrorMsg,
                        OdooSyncDir = c.OdooSyncDir ?? existing.OdooSyncDir,
                        OdooLastSync = c.OdooLastSync ?? now,

                        LastSapDeltaAt = c.UpdateDate ?? now,

                        CreditLimit = existing.CreditLimit,
                        OutstandingBalance = existing.OutstandingBalance,
                        AvailableCredit = existing.AvailableCredit,
                        CreditUpdatedAt = existing.CreditUpdatedAt
                    };

                    _db.CacheCustomers.Update(entity);
                    updated++;
                }
                else
                {
                    var newCustomer = new CacheCustomer
                    {
                        CardCode = c.CardCode,
                        CardName = c.CardName,
                        IsActive = c.IsActive,  // propagate from SAP

                        PriceList = c.PriceList,
                        SlpCode = c.SlpCode,

                        Phone1 = c.Phone1,
                        Phone2 = c.Phone2,
                        Email = c.Email,

                        BillToStreet = c.BillToStreet,
                        BillToCity = c.BillToCity,
                        BillToCountry = c.BillToCountry,

                        ShipToStreet = c.ShipToStreet,
                        ShipToCity = c.ShipToCity,
                        ShipToCountry = c.ShipToCountry,

                        OdooPartnerId = c.OdooPartnerId,
                        OdooStatus = c.OdooStatus ?? "SYNCED",
                        OdooErrorMsg = c.OdooErrorMsg,
                        OdooSyncDir = c.OdooSyncDir ?? "FROM_SAP",
                        OdooLastSync = c.OdooLastSync ?? now,

                        LastSapDeltaAt = c.UpdateDate ?? now
                    };

                    _db.CacheCustomers.Add(newCustomer);
                    inserted++;
                }
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "✅ Customer cache upsert completed | Inserted={Inserted} | Updated={Updated}",
                inserted,
                updated);
        }
        finally
        {
            _db.ChangeTracker.Clear();
            _db.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetect;
        }
    }

    // =====================================================
    // SINGLE UPSERT
    // =====================================================
    public async Task UpsertCustomerAsync(SapCustomerDto c)
    {
        if (string.IsNullOrWhiteSpace(c.CardCode))
            return;

        var now = DateTime.UtcNow;

        var existing = await _db.CacheCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CardCode == c.CardCode);

        if (existing == null)
        {
            _db.CacheCustomers.Add(new CacheCustomer
            {
                CardCode = c.CardCode,
                CardName = c.CardName,
                IsActive = c.IsActive,

                PriceList = c.PriceList,
                SlpCode = c.SlpCode,

                Phone1 = c.Phone1,
                Phone2 = c.Phone2,
                Email = c.Email,

                BillToStreet = c.BillToStreet,
                BillToCity = c.BillToCity,
                BillToCountry = c.BillToCountry,

                ShipToStreet = c.ShipToStreet,
                ShipToCity = c.ShipToCity,
                ShipToCountry = c.ShipToCountry,

                OdooPartnerId = c.OdooPartnerId,
                OdooStatus = c.OdooStatus ?? "SYNCED",
                OdooErrorMsg = c.OdooErrorMsg,
                OdooSyncDir = c.OdooSyncDir ?? "FROM_SAP",
                OdooLastSync = c.OdooLastSync ?? now,
                LastSapDeltaAt = c.UpdateDate ?? now
            });
        }
        else
        {
            _db.CacheCustomers.Update(new CacheCustomer
            {
                CardCode = existing.CardCode,
                CardName = c.CardName,
                IsActive = c.IsActive,  // propagate from SAP

                PriceList = c.PriceList,
                SlpCode = c.SlpCode,

                Phone1 = c.Phone1,
                Phone2 = c.Phone2,
                Email = c.Email,

                BillToStreet = c.BillToStreet,
                BillToCity = c.BillToCity,
                BillToCountry = c.BillToCountry,

                ShipToStreet = c.ShipToStreet,
                ShipToCity = c.ShipToCity,
                ShipToCountry = c.ShipToCountry,

                OdooPartnerId = c.OdooPartnerId,
                OdooStatus = c.OdooStatus ?? existing.OdooStatus,
                OdooErrorMsg = c.OdooErrorMsg,
                OdooSyncDir = c.OdooSyncDir ?? existing.OdooSyncDir,
                OdooLastSync = c.OdooLastSync ?? now,
                LastSapDeltaAt = c.UpdateDate ?? existing.LastSapDeltaAt,
                CreditLimit = existing.CreditLimit,
                OutstandingBalance = existing.OutstandingBalance,
                AvailableCredit = existing.AvailableCredit,
                CreditUpdatedAt = existing.CreditUpdatedAt
            });
        }

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    // =====================================================
    // FAST CLEAR (NO TRACKING SIDE EFFECTS)
    // =====================================================
    public async Task ClearAllCustomersFastAsync()
    {
        _logger.LogWarning("🧹 Clearing CacheCustomers (FAST)");
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM CacheCustomers");
        _db.ChangeTracker.Clear();
    }

    // =====================================================
    // WATERMARK
    // =====================================================
    public async Task<DateTime?> GetLastSapUpdateDateAsync()
    {
        return await _db.CacheCustomers
            .AsNoTracking()
            .Where(x => x.LastSapDeltaAt > new DateTime(1900, 1, 1))
            .OrderByDescending(x => x.LastSapDeltaAt)
            .Select(x => (DateTime?)x.LastSapDeltaAt)
            .FirstOrDefaultAsync();
    }
}
