using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Api.Controllers.AutoHub;

/// <summary>
/// Read-only API for AutoHub Germax enriched products.
/// No API key required — same access level as other product read endpoints.
///
/// Enriched products are served from the AutoHub Neon database (AutoHubDbContext).
/// The /pending endpoint is served from the Live2021 SQL Server cache, since
/// PENDING items are never promoted to Neon until they are SCRAPED.
/// </summary>
[ApiController]
[Route("api/autohub/germax/products")]
public class AutoHubGermaxProductsController : ControllerBase
{
    private readonly AutoHubDbContext       _autoHubDb;
    private readonly Live2021CacheDbContext _cacheDb;

    public AutoHubGermaxProductsController(
        AutoHubDbContext autoHubDb,
        Live2021CacheDbContext cacheDb)
    {
        _autoHubDb = autoHubDb;
        _cacheDb   = cacheDb;
    }

    // ------------------------------------------------------------------
    // GET /api/autohub/germax/products
    // ------------------------------------------------------------------
    /// <summary>
    /// Returns a paginated list of enriched Germax products from Neon.
    /// Only SCRAPED rows are included (no PENDING / ERROR / NO_MATCH).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? itemGroup,
        [FromQuery] bool    activeOnly = true,
        [FromQuery] int     take       = 100)
    {
        take = Math.Clamp(take, 1, 500);

        var q = _autoHubDb.GermaxProducts.AsNoTracking()
            .Where(x => x.ScrapeStatus == "SCRAPED");

        if (activeOnly)
            q = q.Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(itemGroup))
            q = q.Where(x => x.ItemGroupName != null
                && x.ItemGroupName.Contains(itemGroup));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                x.ItemCode.Contains(s) ||
                (x.GermaxArticleNumber != null && x.GermaxArticleNumber.Contains(s)) ||
                (x.OemPartNumber       != null && x.OemPartNumber.Contains(s))       ||
                (x.FitForAuto          != null && x.FitForAuto.Contains(s)));
        }

        var data = await q
            .OrderBy(x => x.ItemGroupName)
            .ThenBy(x => x.ItemCode)
            .Take(take)
            .Select(x => new
            {
                x.ItemCode,
                x.ItemName,
                x.ItemGroupName,
                x.EngineCode,
                x.GermaxArticleNumber,
                x.OemPartNumber,
                x.FitForAuto,
                x.ProductUrl,
                x.ImageUrl,
                x.MatchMethod,
                x.MatchScore,
                x.IsActive,
                x.ScrapedAt
            })
            .ToListAsync();

        return Ok(data);
    }

    // ------------------------------------------------------------------
    // GET /api/autohub/germax/products/pending
    // ------------------------------------------------------------------
    /// <summary>
    /// Returns active PENDING rows from the Live2021 cache.
    /// Useful for monitoring the enrichment queue depth.
    /// </summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending([FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 500);

        var data = await _cacheDb.GermaxProducts.AsNoTracking()
            .Where(x => x.IsActive && x.ScrapeStatus == "PENDING")
            .OrderBy(x => x.LastSapSeedAt)
            .Take(take)
            .Select(x => new
            {
                x.ItemCode,
                x.ItemName,
                x.ItemGroupName,
                x.EngineCode,
                x.LastSapSeedAt
            })
            .ToListAsync();

        return Ok(data);
    }

    // ------------------------------------------------------------------
    // GET /api/autohub/germax/products/{itemCode}
    // ------------------------------------------------------------------
    /// <summary>
    /// Returns full enrichment detail for a single item from Neon.
    /// <c>allImageUrls</c> is returned as a typed array (deserialized from JSON).
    /// Returns 404 when the item is not found or is not yet SCRAPED.
    /// </summary>
    [HttpGet("{itemCode}")]
    public async Task<IActionResult> GetOne(string itemCode)
    {
        var p = await _autoHubDb.GermaxProducts.AsNoTracking()
            .Where(x => x.ItemCode == itemCode && x.ScrapeStatus == "SCRAPED")
            .FirstOrDefaultAsync();

        if (p is null) return NotFound();

        return Ok(new
        {
            // ── Identity ──────────────────────────────────────────────
            p.ItemCode,
            p.ItemName,
            p.ItemGroupName,
            p.EngineCode,

            // ── Germax enrichment ──────────────────────────────────────
            p.GermaxArticleNumber,
            p.OemPartNumber,
            p.FitForAuto,
            p.Description,
            p.ProductUrl,

            // ── Media ─────────────────────────────────────────────────
            p.ImageUrl,
            AllImageUrls = Deserialize<List<string>>(p.AllImageUrls),

            // ── Match metadata ─────────────────────────────────────────
            p.MatchMethod,
            p.MatchScore,

            // ── Lifecycle ─────────────────────────────────────────────
            p.IsActive,
            p.ScrapeStatus,
            p.ScrapedAt,
            p.LastSapSeedAt
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }
}
