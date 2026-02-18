using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Api.Controllers.Neon;

[ApiController]
[Route("api/neon/prices")]
public class NeonPriceListsController : ControllerBase
{
    private readonly NeonDbContext _db;

    public NeonPriceListsController(NeonDbContext db)
    {
        _db = db;
    }

    // GET /api/neon/prices?itemCode=1024
    [HttpGet]
    public async Task<IActionResult> GetPrices(
        [FromQuery] string? itemCode)
    {
        var q = _db.PriceLists.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(itemCode))
        {
            itemCode = itemCode.Trim();
            q = q.Where(x => x.ItemCode == itemCode);
        }

        var data = await q
            .OrderBy(x => x.ItemCode)
            .ThenBy(x => x.PriceList)
            .Select(x => new
            {
                x.ItemCode,
                x.PriceList,
                x.Price,
                x.SyncedAt
            })
            .ToListAsync();

        return Ok(data);
    }
}