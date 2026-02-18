using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.Orders;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Orders;

public class ReservationCommitService
{
    private readonly MolasCacheDbContext _db;
    private readonly SapSalesOrderCreator _sap;
    private readonly ILogger<ReservationCommitService> _logger;

    public ReservationCommitService(
        MolasCacheDbContext db,
        SapSalesOrderCreator sap,
        ILogger<ReservationCommitService> logger)
    {
        _db = db;
        _sap = sap;
        _logger = logger;
    }

    public async Task<(int DocEntry, int DocNum)> CommitAsync(
     string customerCode,
     IEnumerable<long> reservationIds,
     string? externalOrderId = null)
    {
        using var tx = await _db.Database.BeginTransactionAsync();

        var reservations = await _db.CacheStockReservations
            .Where(r => reservationIds.Contains(r.Id))
            .ToListAsync();

        if (!reservations.Any())
            throw new InvalidOperationException("No reservations found");

        if (reservations.Any(r => r.IsCommitted || r.ReleasedAt != null))
            throw new InvalidOperationException("One or more reservations already used");

        // 🔹 Determine ExternalOrderId:
        //    Priority 1 – explicit caller-provided value (e.g. from Odoo SO ID on the request)
        //    Priority 2 – value already stored on the reservations
        if (string.IsNullOrWhiteSpace(externalOrderId))
        {
            externalOrderId = reservations
                .Select(r => r.OdooSalesOrderId)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        }

        var distinctOdooIds = reservations
            .Select(r => r.OdooSalesOrderId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (distinctOdooIds.Count > 1)
            throw new InvalidOperationException(
                $"Reservations belong to multiple Odoo Sales Orders: {string.Join(", ", distinctOdooIds)}. " +
                "Commit one Odoo order at a time.");

        _logger.LogInformation(
            "Committing reservations → Creating SO | Customer={Customer} | ExternalOrderId={ExternalId}",
            customerCode,
            externalOrderId);

        // 🔹 Build Sales Order DTO
        var dto = new CreateSalesOrderDto
        {
            CustomerCode = customerCode,
            ExternalOrderId = externalOrderId, // 👈 CRITICAL FIX

            Lines = reservations
                .GroupBy(r => r.ItemCode)
                .Select(g => new CreateSalesOrderLineDto
                {
                    ItemCode = g.Key,
                    Quantity = g.Sum(x => x.Quantity)
                })
                .ToList()
        };

        // 🔹 Create order in SAP
        var sapResult = _sap.CreateOrder(dto);

        // 🔹 Mark reservations as committed
        foreach (var r in reservations)
        {
            r.IsCommitted = true;
            r.SapDocEntry = sapResult.DocEntry;
            r.Reference ??= $"SAP_SO:{sapResult.DocEntry}";
        }

        // 🔹 Cache Sales Order
        _db.CacheSalesOrders.Add(new CacheSalesOrder
        {
            SapDocEntry = sapResult.DocEntry,
            SapDocNum = sapResult.DocNum,
            CustomerCode = customerCode,
            OdooSalesOrderId = externalOrderId, // 👈 Keep traceability
            DocStatus = "O",
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        _logger.LogInformation(
            "SalesOrder committed from reservations | DocEntry={DocEntry} | Customer={CustomerCode} | OdooSO={ExternalId}",
            sapResult.DocEntry,
            customerCode,
            externalOrderId);

        return (sapResult.DocEntry, sapResult.DocNum);
    }
}