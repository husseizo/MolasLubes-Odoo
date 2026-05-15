using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Api.Security;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Services.Sync;
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
            .FirstOrDefaultAsync();

        if (p is null) return NotFound();

        // Deserialise JSON-stored fields back to typed objects for the response
        var response = new
        {
            // ── Identity ──────────────────────────────────────────────
            p.ArticleNumber,
            p.Name,
            p.ProductUrl,

            // ── Classification ────────────────────────────────────────
            p.Category,
            p.SubCategory,

            // ── Description ───────────────────────────────────────────
            p.Description,

            // ── Packaging / sizes ─────────────────────────────────────
            p.PackagingSize,
            AllPackagingSizes = Deserialise<List<string>>(p.AllPackagingSizes),

            // ── Spec grade ────────────────────────────────────────────
            p.SpecGrade,

            // ── Media ─────────────────────────────────────────────────
            p.ImageUrl,
            AllImageUrls = Deserialise<List<string>>(p.AllImageUrls),

            // ── Approvals & Specifications ────────────────────────────
            Approvals          = Deserialise<List<string>>(p.Approvals),
            Specifications     = Deserialise<Dictionary<string, string>>(p.Specifications),
            SpecificationItems = Deserialise<List<string>>(p.SpecificationItems),
            OverviewProperties = Deserialise<List<string>>(p.OverviewProperties),
            Application        = p.Application,
            LiquiMolyRecommendations = Deserialise<List<string>>(p.LiquiMolyRecommendations),

            // ── Downloads ─────────────────────────────────────────────
            p.ProductInfoPdfUrl,
            p.SafetyDataSheetPdfUrl,

            // ── Meta ──────────────────────────────────────────────────
            p.IsActive,
            p.ScrapedAt,
        };

        return Ok(response);
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

    // ── Private helpers ───────────────────────────────────────────────────────

    private static T? Deserialise<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }
}

// =====================================================================
// ADMIN CONTROLLER  — manual scrape trigger
// =====================================================================

[ApiController]
[Route("api/admin/liquimoly")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminLiquiMolyController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly LiquiMolyNeonSyncService _neonSyncService;

    public AdminLiquiMolyController(
        ISchedulerFactory schedulerFactory,
        LiquiMolyNeonSyncService neonSyncService)
    {
        _schedulerFactory = schedulerFactory;
        _neonSyncService = neonSyncService;
    }

    // POST /api/admin/liquimoly/scrape
    [HttpPost("scrape")]
    public async Task<IActionResult> TriggerScrape()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("LiquiMolyProductScrapeJob"));

        return Ok(new { Message = "Liqui-Moly product scrape triggered" });
    }

    // POST /api/admin/liquimoly/sync-neon
    [HttpPost("sync-neon")]
    public async Task<IActionResult> SyncNeon(CancellationToken ct)
    {
        await _neonSyncService.SyncFromCacheAsync(ct);

        return Ok(new
        {
            Message = "Liqui-Moly cache sync to Neon completed successfully"
        });
    }
}
