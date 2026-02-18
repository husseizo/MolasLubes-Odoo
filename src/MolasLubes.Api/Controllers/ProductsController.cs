using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Services.Pricing;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/products")]

public class ProductsController : ControllerBase
{
    private readonly MolasCacheDbContext _db;
    private readonly CustomerPricingService _pricing;

    public ProductsController(
        MolasCacheDbContext db,
        CustomerPricingService pricing)
    {
        _db = db;
        _pricing = pricing;
    }

    // =====================================================
    // GET /api/products
    // =====================================================
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int take = 200)
    {
        take = Math.Clamp(take, 1, 1000);

        var q = _db.CacheProducts.AsNoTracking().AsQueryable();

        if (activeOnly)
            q = q.Where(x => x.IsActive);

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
                x.WarehouseCode,
                x.Barcode,
                x.IsActive,
                x.PriceList_1,
                x.PriceList_2,
                x.PriceList_3,
                x.LastSapSyncAt
            })
            .ToListAsync();

        return Ok(data);
    }

    // =====================================================
    // GET /api/products/{itemCode}
    // =====================================================
    [HttpGet("{itemCode}")]
    public async Task<IActionResult> GetOne(string itemCode)
    {
        var p = await _db.CacheProducts.AsNoTracking()
            .Where(x => x.ItemCode == itemCode)
            .Select(x => new
            {
                x.ItemCode,
                x.ItemName,
                x.OnHandSap,
                x.AvailableCache,
                x.IsActive,
                x.PriceList_1,
                x.PriceList_2,
                x.PriceList_3,
                x.LastSapSyncAt
            })
            .FirstOrDefaultAsync();

        return p == null ? NotFound() : Ok(p);
    }

    // =====================================================
    // GET /api/products/{itemCode}/price?cardCode=C0001
    // =====================================================
    [HttpGet("{itemCode}/price")]
    public async Task<IActionResult> GetCustomerPrice(
        string itemCode,
        [FromQuery] string cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            return BadRequest("cardCode is required");

        var price = await _pricing
            .GetCustomerItemPriceAsync(
                cardCode.Trim(),
                itemCode.Trim());

        if (price == null)
            return NotFound(new { Message = "Price not found" });

        return Ok(price);
    }
}