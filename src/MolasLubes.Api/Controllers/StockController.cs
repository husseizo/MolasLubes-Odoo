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
}