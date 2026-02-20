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

        // Normalize incoming warehouse code (e.g. "WH" → "MainWHSE") so the
        // preferred-warehouse lookup finds the correct row in CacheProducts.
        if (!string.IsNullOrWhiteSpace(warehouseCode))
            warehouseCode = NormalizeWarehouse(warehouseCode);

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

                    // Log a per-warehouse breakdown to make the root cause diagnosable
                    // (e.g. distinguish "SAP has 0 stock" from "stuck reservations drained cache").
                    foreach (var wh in warehouses)
                    {
                        _logger.LogWarning(
                            "📊 StockDetail | Item={Item} | Warehouse={Whs} | OnHandSap={OnHand} | AvailableCache={Available}",
                            itemCode, wh.WarehouseCode, wh.OnHandSap, wh.AvailableCache);
                    }

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



    // =====================================================
    // 🔧 REPAIR CACHE (RECOMPUTE AvailableCache FROM SAP ON-HAND)
    // =====================================================
    /// <summary>
    /// Recomputes AvailableCache for every warehouse row of <paramref name="itemCode"/>
    /// as: Max(0, OnHandSap − active uncommitted reservations).
    /// Use this when AvailableCache has drifted from reality due to stuck
    /// reservations or a missed sync, without needing a full SAP sync.
    /// </summary>
    public async Task<IReadOnlyList<(string Warehouse, decimal OnHandSap, decimal ActiveReserved, decimal NewAvailable)>>
        RepairCacheAsync(string itemCode)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            throw new ArgumentException("ItemCode is required", nameof(itemCode));

        _logger.LogInformation("🔧 RepairCache started | Item={Item}", itemCode);

        var products = await _db.CacheProducts
            .Where(x => x.ItemCode == itemCode)
            .ToListAsync();

        if (products.Count == 0)
            throw new InvalidOperationException($"No cache rows found for item: {itemCode}");

        var results = new List<(string, decimal, decimal, decimal)>();

        foreach (var product in products)
        {
            var reserved = await _db.CacheStockReservations
                .Where(r => r.ItemCode == product.ItemCode &&
                            r.WarehouseCode == product.WarehouseCode &&
                            r.ReleasedAt == null &&
                            !r.IsCommitted)
                .SumAsync(r => r.Quantity);

            var newAvailable = Math.Max(0m, product.OnHandSap - reserved);

            _logger.LogInformation(
                "🔧 Repair | Whs={Whs} | OnHandSap={OnHand} | ActiveReserved={Reserved} | OldAvail={Old} → NewAvail={New}",
                product.WarehouseCode, product.OnHandSap, reserved, product.AvailableCache, newAvailable);

            product.AvailableCache = newAvailable;
            results.Add((product.WarehouseCode, product.OnHandSap, reserved, newAvailable));
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("✅ RepairCache completed | Item={Item} | Warehouses={Count}", itemCode, products.Count);

        return results;
    }

    // =====================================================
    // 🔍 DIAGNOSTICS (READ-ONLY)
    // =====================================================
    public async Task<object> GetStockDiagnosticsAsync(string itemCode)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            throw new ArgumentException("ItemCode is required", nameof(itemCode));

        var warehouses = await _db.CacheProducts
            .AsNoTracking()
            .Where(x => x.ItemCode == itemCode)
            .Select(x => new
            {
                x.WarehouseCode,
                x.OnHandSap,
                x.AvailableCache,
                x.IsActive,
                x.LastSapSyncAt
            })
            .ToListAsync();

        var activeReservations = await _db.CacheStockReservations
            .AsNoTracking()
            .Where(r => r.ItemCode == itemCode && r.ReleasedAt == null && !r.IsCommitted)
            .Select(r => new
            {
                r.Id,
                r.WarehouseCode,
                r.Quantity,
                r.Reference,
                r.CreatedAt,
                r.IsCommitted
            })
            .ToListAsync();

        return new
        {
            ItemCode = itemCode,
            Warehouses = warehouses,
            ActiveReservations = activeReservations,
            TotalOnHandSap = warehouses.Sum(w => w.OnHandSap),
            TotalAvailableCache = warehouses.Sum(w => w.AvailableCache),
            TotalActiveReserved = activeReservations.Sum(r => r.Quantity)
        };
    }

    private string NormalizeWarehouse(string warehouseCode)
    {
        if (string.IsNullOrWhiteSpace(warehouseCode))
            throw new ArgumentException("WarehouseCode is required");

        var trimmed = warehouseCode.Trim();

        // Compare in upper-case but return original casing for the pass-through so
        // that DB lookups work correctly regardless of the column collation.
        return trimmed.ToUpperInvariant() switch
        {
            "WH" => "MainWHSE",     // 🔥 MAP API → REAL SAP CODE
            _ => trimmed            // pass-through with original casing preserved
        };
    }
}