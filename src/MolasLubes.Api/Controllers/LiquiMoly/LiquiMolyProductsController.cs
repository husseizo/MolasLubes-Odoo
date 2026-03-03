using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Persistence;
using Quartz;

namespace MolasLubes.Api.Controllers.LiquiMoly;

// =====================================================================
// READ CONTROLLER  — query Liqui-Moly products from Neon
// =====================================================================

[ApiController]
[Route("api/liquimoly/products")]
public class LiquiMolyProductsController : ControllerBase
{
    private readonly NeonDbContext _db;

    public LiquiMolyProductsController(NeonDbContext db)
    {
        _db = db;
    }

    // GET /api/liquimoly/products
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int take = 200)
    {
        take = Math.Clamp(take, 1, 1000);

        var q = _db.LiquiMolyProducts.AsNoTracking();

        if (activeOnly)
            q = q.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(x => x.Category != null && x.Category.Contains(category));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                x.ArticleNumber.Contains(s) ||
                x.Name.Contains(s) ||
                (x.SpecGrade != null && x.SpecGrade.Contains(s)));
        }

        var data = await q
            .OrderBy(x => x.Category)
            .ThenBy(x => x.ArticleNumber)
            .Take(take)
            .Select(x => new
            {
                x.ArticleNumber,
                x.Name,
                x.Category,
                x.SubCategory,
                x.SpecGrade,
                x.PackagingSize,
                x.IsActive,
                x.ScrapedAt,
                x.ProductUrl,
                x.ImageUrl,
            })
            .ToListAsync();

        return Ok(data);
    }

    // GET /api/liquimoly/products/{articleNumber}
    [HttpGet("{articleNumber}")]
    public async Task<IActionResult> GetOne(string articleNumber)
    {
        var p = await _db.LiquiMolyProducts.AsNoTracking()
            .Where(x => x.ArticleNumber == articleNumber)
            .Select(x => new
            {
                x.ArticleNumber,
                x.Name,
                x.Category,
                x.SubCategory,
                x.Description,
                x.SpecGrade,
                x.PackagingSize,
                x.IsActive,
                x.ScrapedAt,
                x.ProductUrl,
                x.ImageUrl,
            })
            .FirstOrDefaultAsync();

        return p is null ? NotFound() : Ok(p);
    }

    // GET /api/liquimoly/products/categories
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _db.LiquiMolyProducts.AsNoTracking()
            .Where(x => x.IsActive && x.Category != null)
            .Select(x => x.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        return Ok(categories);
    }
}

// =====================================================================
// ADMIN CONTROLLER  — manual scrape trigger
// =====================================================================

[ApiController]
[Route("api/admin/liquimoly")]
public class AdminLiquiMolyController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;

    public AdminLiquiMolyController(ISchedulerFactory schedulerFactory)
    {
        _schedulerFactory = schedulerFactory;
    }

    // POST /api/admin/liquimoly/scrape
    [HttpPost("scrape")]
    public async Task<IActionResult> TriggerScrape()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("LiquiMolyProductScrapeJob"));

        return Ok(new { Message = "Liqui-Moly product scrape triggered" });
    }
}
