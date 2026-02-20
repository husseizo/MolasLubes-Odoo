using Microsoft.AspNetCore.Mvc;
using MolasLubes.Infrastructure.Services.Stock;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/stock")]
public class StockController : ControllerBase
{
    private readonly StockReservationService _svc;

    public StockController(StockReservationService svc)
    {
        _svc = svc;
    }

    // POST /api/stock/reserve
    [HttpPost("reserve")]
    public async Task<IActionResult> Reserve([FromBody] ReserveStockRequest req)
    {
        var res = await _svc.ReserveAsync(
    req.ItemCode.Trim(),
    req.WarehouseCode,     // ✅ NEW
    req.Quantity,
    req.Reference);
        return Ok(new
        {
            res.Id,
            res.ItemCode,
            res.Quantity,
            res.Reference,
            res.CreatedAt
        });
    }

    // POST /api/stock/release/123
    [HttpPost("release/{id:long}")]
    public async Task<IActionResult> Release(long id)
    {
        await _svc.ReleaseAsync(id);
        return Ok(new { Message = "Released", ReservationId = id });
    }

    // GET /api/stock/diagnostics/3715
    // Returns per-warehouse OnHandSap, AvailableCache, and all active reservations.
    // Use this to find out why a reservation is failing before calling /repair.
    [HttpGet("diagnostics/{itemCode}")]
    public async Task<IActionResult> Diagnostics(string itemCode)
    {
        var result = await _svc.GetStockDiagnosticsAsync(itemCode.Trim());
        return Ok(result);
    }

    // POST /api/stock/repair/3715
    // Recomputes AvailableCache = Max(0, OnHandSap − activeReservations) for every
    // warehouse row of the item WITHOUT touching SAP.
    // Use when AvailableCache has drifted to 0 due to stuck/stale reservations
    // and you cannot wait for the next full product sync.
    [HttpPost("repair/{itemCode}")]
    public async Task<IActionResult> RepairCache(string itemCode)
    {
        var rows = await _svc.RepairCacheAsync(itemCode.Trim());

        return Ok(new
        {
            ItemCode = itemCode,
            Repaired = rows.Select(r => new
            {
                Warehouse = r.Warehouse,
                OnHandSap = r.OnHandSap,
                ActiveReserved = r.ActiveReserved,
                NewAvailableCache = r.NewAvailable
            })
        });
    }
}