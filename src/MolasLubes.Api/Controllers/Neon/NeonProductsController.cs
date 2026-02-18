using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Api.Controllers.Neon;

[ApiController]
[Route("api/neon/products")]
public class NeonProductsController : ControllerBase
{
    private readonly NeonDbContext _db;

    public NeonProductsController(NeonDbContext db)
    {
        _db = db;
    }

    // GET /api/neon/products
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] int take = 200)
    {
        take = Math.Clamp(take, 1, 1000);

        var q = _db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            q = q.Where(x =>
                x.ItemCode.Contains(search) ||
                x.ItemName.Contains(search));
        }

        var data = await q
            .OrderBy(x => x.ItemCode)
            .Take(take)
            .Select(x => new
            {
                x.ItemCode,
                x.ItemName,
                x.OnHandSap,
                x.AvailableCache,
                x.SyncedAt
            })
            .ToListAsync();

        return Ok(data);
    }

    // GET /api/neon/products/{itemCode}
    [HttpGet("{itemCode}")]
    public async Task<IActionResult> GetOne(string itemCode)
    {
        var p = await _db.Products.AsNoTracking()
            .Where(x => x.ItemCode == itemCode)
            .Select(x => new
            {
                x.ItemCode,
                x.ItemName,
                x.OnHandSap,
                x.AvailableCache,
                x.SyncedAt
            })
            .FirstOrDefaultAsync();

        return p == null ? NotFound() : Ok(p);
    }
}