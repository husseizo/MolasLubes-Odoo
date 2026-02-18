using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Stock;

public class StockReservationService
{
    private readonly MolasCacheDbContext _db;
    private readonly ILogger<StockReservationService> _logger;

    public StockReservationService(
        MolasCacheDbContext db,
        ILogger<StockReservationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // =====================================================
    // 📌 RESERVE STOCK (WAREHOUSE-AWARE + CONCURRENCY SAFE)
    // =====================================================
    public async Task<CacheStockReservation> ReserveAsync(
    string itemCode,
    string warehouseCode,
    decimal qty,
    string? reference)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            throw new ArgumentException("ItemCode is required", nameof(itemCode));

        if (qty <= 0)
            throw new ArgumentException("Quantity must be greater than zero", nameof(qty));

        const int maxRetries = 3;

        _logger.LogInformation(
            "🔍 Reserve requested | Item={Item} | PreferredWhs={Whs} | Qty={Qty}",
            itemCode, warehouseCode, qty);

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                // 🔎 Get all warehouses for item
                var warehouses = await _db.CacheProducts
                    .Where(x => x.ItemCode == itemCode && x.IsActive)
                    .OrderByDescending(x => x.AvailableCache)
                    .ToListAsync();

                if (!warehouses.Any())
                    throw new InvalidOperationException($"Product not found: {itemCode}");

                // 1️⃣ Try preferred warehouse first
                var selected = warehouses
                    .FirstOrDefault(x =>
                        x.WarehouseCode == warehouseCode &&
                        x.AvailableCache >= qty);

                // 2️⃣ If not enough → auto allocate highest stock warehouse
                if (selected == null)
                {
                    selected = warehouses
                        .FirstOrDefault(x => x.AvailableCache >= qty);
                }

                if (selected == null)
                {
                    var totalAvailable = warehouses.Sum(x => x.AvailableCache);

                    throw new InvalidOperationException(
                        $"Insufficient total stock | Item={itemCode} | Requested={qty} | TotalAvailable={totalAvailable}");
                }

                _logger.LogInformation(
                    "📦 Allocating from warehouse {Warehouse} | AvailableBefore={Available}",
                    selected.WarehouseCode,
                    selected.AvailableCache);

                selected.AvailableCache -= qty;

                var reservation = new CacheStockReservation
                {
                    ItemCode = itemCode,
                    WarehouseCode = selected.WarehouseCode, // 🔥 actual warehouse
                    Quantity = qty,
                    Reference = reference,
                    CreatedAt = DateTime.UtcNow,
                    IsCommitted = false
                };

                _db.CacheStockReservations.Add(reservation);

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                _logger.LogInformation(
                    "✅ Reserved | Item={Item} | Warehouse={Warehouse} | Qty={Qty} | NewAvailable={Available}",
                    itemCode,
                    selected.WarehouseCode,
                    qty,
                    selected.AvailableCache);

                return reservation;
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
            {
                await tx.RollbackAsync();

                foreach (var entry in _db.ChangeTracker.Entries())
                    entry.State = EntityState.Detached;

                _logger.LogWarning(
                    "⚠ Concurrency retry | Item={Item} | Attempt={Attempt}",
                    itemCode,
                    attempt);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        throw new InvalidOperationException(
            "Failed to reserve stock due to concurrent updates.");
    }

    // =====================================================
    // 🔓 RELEASE STOCK (IDEMPOTENT + WAREHOUSE SAFE)
    // =====================================================
    public async Task ReleaseAsync(long reservationId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();

        try
        {
            _logger.LogInformation(
                "🔍 Release requested | ReservationId={ReservationId}",
                reservationId);

            var reservation = await _db.CacheStockReservations
                .SingleOrDefaultAsync(x => x.Id == reservationId);

            if (reservation == null)
            {
                _logger.LogWarning(
                    "❌ Release failed | Reservation not found | ReservationId={ReservationId}",
                    reservationId);

                throw new InvalidOperationException("Reservation not found");
            }

            _logger.LogInformation(
                "📦 Reservation loaded | ItemCode={ItemCode} | Whs={Warehouse} | Qty={Qty} | ReleasedAt={ReleasedAt}",
                reservation.ItemCode,
                reservation.WarehouseCode,
                reservation.Quantity,
                reservation.ReleasedAt);

            if (reservation.ReleasedAt != null)
            {
                _logger.LogInformation(
                    "ℹ Reservation already released | ReservationId={ReservationId}",
                    reservationId);

                return;
            }

            // 🔥 Normalize warehouse (if using mapping layer)
            var normalizedWarehouse = NormalizeWarehouse(reservation.WarehouseCode);

            _logger.LogInformation(
                "🔁 Warehouse normalized for release | Original={Original} | Normalized={Normalized}",
                reservation.WarehouseCode,
                normalizedWarehouse);

            var product = await _db.CacheProducts
                .SingleOrDefaultAsync(x =>
                    x.ItemCode == reservation.ItemCode &&
                    x.WarehouseCode == normalizedWarehouse);

            if (product == null)
            {
                _logger.LogError(
                    "❌ Product not found during release | ItemCode={ItemCode} | Whs={Warehouse}",
                    reservation.ItemCode,
                    normalizedWarehouse);

                throw new InvalidOperationException(
                    $"Product not found: {reservation.ItemCode} in warehouse {normalizedWarehouse}");
            }

            _logger.LogInformation(
                "📊 Stock before release | Available={Available}",
                product.AvailableCache);

            // 🔓 Restore stock
            product.AvailableCache += reservation.Quantity;

            reservation.ReleasedAt = DateTime.UtcNow;
            reservation.IsCommitted = false;
            reservation.SapDocEntry = null;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation(
                "✅ Stock released successfully | ItemCode={ItemCode} | Whs={Warehouse} | Qty={Qty} | NewAvailable={Available} | ReservationId={ReservationId}",
                reservation.ItemCode,
                normalizedWarehouse,
                reservation.Quantity,
                product.AvailableCache,
                reservation.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();

            _logger.LogError(
                ex,
                "❌ Release transaction failed | ReservationId={ReservationId}",
                reservationId);

            throw;
        }
    }



    private string NormalizeWarehouse(string warehouseCode)
    {
        if (string.IsNullOrWhiteSpace(warehouseCode))
            throw new ArgumentException("WarehouseCode is required");

        warehouseCode = warehouseCode.Trim().ToUpperInvariant();

        return warehouseCode switch
        {
            "WH" => "MainWHSE",     // 🔥 MAP API → REAL SAP CODE
            _ => warehouseCode      // pass-through if already valid
        };
    }
}