using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Domain.Entities.Cache;

namespace MolasLubes.Infrastructure.Services.Finance;

public class CustomerCreditService
{
    private readonly MolasCacheDbContext _db;
    private readonly SapCustomerReader _reader;
    private readonly ILogger<CustomerCreditService> _logger;

    public CustomerCreditService(
        MolasCacheDbContext db,
        SapCustomerReader reader,
        ILogger<CustomerCreditService> logger)
    {
        _db = db;
        _reader = reader;
        _logger = logger;
    }

    /// <summary>
    /// 🔄 Refresh credit from SAP and return cached customer
    /// SAP = source of truth
    /// Cache = fast read
    /// </summary>
    public async Task<CacheCustomer?> RefreshAndGetCustomerCreditAsync(string cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            throw new ArgumentException("CardCode is required", nameof(cardCode));

        _logger.LogInformation(
            "💳 Refreshing customer credit | CardCode={CardCode}",
            cardCode);

        // ============================
        // 1️⃣ READ CREDIT FROM SAP
        // ============================
        var sap = _reader.ReadCustomerCredit(cardCode);

        if (sap == null)
        {
            _logger.LogWarning(
                "⚠️ SAP credit not found | CardCode={CardCode}",
                cardCode);
            return null;
        }

        // ============================
        // 2️⃣ LOAD FROM CACHE
        // ============================
        var customer = await _db.CacheCustomers.FindAsync(cardCode);

        if (customer == null)
        {
            _logger.LogWarning(
                "⚠️ Customer not found in cache | CardCode={CardCode}",
                cardCode);
            return null;
        }

        // ============================
        // 3️⃣ UPDATE CACHE
        // ============================
        customer.CreditLimit = sap.CreditLimit;
        customer.OutstandingBalance = sap.Balance;
        customer.AvailableCredit = sap.CreditLimit - sap.Balance;
        customer.CreditUpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "✅ Credit refreshed | CardCode={CardCode} | Limit={Limit} | Balance={Balance} | Available={Available}",
            cardCode,
            customer.CreditLimit,
            customer.OutstandingBalance,
            customer.AvailableCredit);

        // ============================
        // 4️⃣ RETURN UPDATED CACHE
        // ============================
        return customer;
    }
}