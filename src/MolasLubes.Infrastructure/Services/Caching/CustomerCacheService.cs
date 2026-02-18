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
    // GET PAGED
    // =====================================================
    public async Task<IReadOnlyList<CacheCustomer>> GetCustomersAsync(
        int page,
        int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;

        return await _db.CacheCustomers
            .AsNoTracking()
            .OrderBy(x => x.CardCode)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    // =====================================================
    // BULK UPSERT (ENTERPRISE SAFE)
    // =====================================================
    public async Task UpsertCustomersAsync(IEnumerable<SapCustomerDto> customers)
    {
        var now = DateTime.UtcNow;
        var list = customers
            .Where(x => !string.IsNullOrWhiteSpace(x.CardCode))
            .GroupBy(x => x.CardCode)       // 🔥 remove duplicates in batch
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

        // --------------------------------------------------
        // 🔥 Disable tracking auto-detect for performance
        // --------------------------------------------------
        var originalAutoDetect = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;

        try
        {
            var inserted = 0;
            var updated = 0;

            // --------------------------------------------------
            // Load existing customers WITHOUT tracking
            // --------------------------------------------------
            var cardCodes = list.Select(x => x.CardCode).ToList();

            var existingCustomers = await _db.CacheCustomers
                .AsNoTracking()
                .Where(x => cardCodes.Contains(x.CardCode))
                .ToDictionaryAsync(x => x.CardCode);

            foreach (var c in list)
            {
                if (existingCustomers.TryGetValue(c.CardCode, out var existing))
                {
                    // UPDATE (attach clean instance)
                    var entity = new CacheCustomer
                    {
                        CardCode = existing.CardCode,
                        CardName = c.CardName,
                        IsActive = true,

                        PriceList = c.PriceList,
                        SlpCode = c.SlpCode,

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
                        IsActive = true,

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
            // --------------------------------------------------
            // 🔥 CRITICAL MEMORY + TRACKING RESET
            // --------------------------------------------------
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
                IsActive = true,

                PriceList = c.PriceList,
                SlpCode = c.SlpCode,
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
                IsActive = true,
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