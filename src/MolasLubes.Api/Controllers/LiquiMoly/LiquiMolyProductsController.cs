using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Api.Security;
using MolasLubes.Api.Utilities;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Integrations.Meguin;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

namespace MolasLubes.Api.Controllers.LiquiMoly;

[ApiController]
[Route("api/liquimoly/products")]
public class LiquiMolyProductsController : ControllerBase
{
    private readonly NeonDbContext _db;

    public LiquiMolyProductsController(NeonDbContext db)
    {
        _db = db;
    }

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
                (x.SpecGrade != null && x.SpecGrade.Contains(s)) ||
                (x.PrimaryBarcode != null && x.PrimaryBarcode.Contains(s)));
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
                x.PrimaryBarcode,
                x.PrimaryBarcodeUomCode,
                x.HasUnitBarcode,
                x.BarcodeResolutionStatus,
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpGet("{articleNumber}")]
    public async Task<IActionResult> GetOne(string articleNumber)
    {
        var p = await _db.LiquiMolyProducts.AsNoTracking()
            .Where(x => x.ArticleNumber == articleNumber)
            .FirstOrDefaultAsync();

        if (p is null)
            return NotFound();

        var response = new
        {
            p.ArticleNumber,
            p.Name,
            p.ProductUrl,
            p.Category,
            p.SubCategory,
            p.Description,
            p.PackagingSize,
            AllPackagingSizes = Deserialise<List<string>>(p.AllPackagingSizes),
            p.Liter,
            p.SpecGrade,
            p.ImageUrl,
            AllImageUrls = Deserialise<List<string>>(p.AllImageUrls),
            p.PrimaryBarcode,
            p.PrimaryBarcodeUomCode,
            p.PrimaryBarcodeUomName,
            p.PrimaryBarcodeUomEntry,
            p.PrimaryBarcodeBaseQtyInGroup,
            p.HasUnitBarcode,
            p.BarcodeResolutionStatus,
            p.BarcodeResolutionNote,
            BarcodeInfo = new
            {
                p.HasUnitBarcode,
                p.PrimaryBarcode,
                p.PrimaryBarcodeUomCode,
                p.PrimaryBarcodeUomName,
                p.PrimaryBarcodeUomEntry,
                p.PrimaryBarcodeBaseQtyInGroup,
                p.BarcodeResolutionStatus,
                p.BarcodeResolutionNote,
                AllBarcodes = Deserialise<List<LiquiMolyBarcodeRowDto>>(p.AllBarcodes)
            },
            SapUomInfo = Deserialise<LiquiMolySapUomInfoDto>(p.SapUomInfo),
            Approvals = Deserialise<List<string>>(p.Approvals),
            Specifications = Deserialise<Dictionary<string, string>>(p.Specifications),
            SpecificationItems = Deserialise<List<string>>(p.SpecificationItems),
            OverviewProperties = Deserialise<List<string>>(p.OverviewProperties),
            Application = p.Application,
            LiquiMolyRecommendations = Deserialise<List<string>>(p.LiquiMolyRecommendations),
            p.ProductInfoPdfUrl,
            p.SafetyDataSheetPdfUrl,
            p.IsActive,
            p.ScrapedAt,
        };

        return Ok(response);
    }

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

    private static T? Deserialise<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }
}

[ApiController]
[Route("api/admin/liquimoly")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminLiquiMolyController : ControllerBase
{
    private static readonly Regex ArticlePattern = new(@"^\d{3,6}$", RegexOptions.Compiled);

    private readonly ISchedulerFactory _schedulerFactory;
    private readonly LiquiMolyNeonSyncService _neonSyncService;
    private readonly LiquiMolyProductScraperService _liquiMolyScraper;
    private readonly MeguinProductScraperService _meguinScraper;
    private readonly SapProductBarcodeReader _barcodeReader;
    private readonly LiquiMolyCacheSyncService _cacheSyncService;
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly ILogger<AdminLiquiMolyController> _logger;

    public AdminLiquiMolyController(
        ISchedulerFactory schedulerFactory,
        LiquiMolyNeonSyncService neonSyncService,
        LiquiMolyProductScraperService liquiMolyScraper,
        MeguinProductScraperService meguinScraper,
        SapProductBarcodeReader barcodeReader,
        LiquiMolyCacheSyncService cacheSyncService,
        IWebHostEnvironment hostEnvironment,
        ILogger<AdminLiquiMolyController> logger)
    {
        _schedulerFactory = schedulerFactory;
        _neonSyncService = neonSyncService;
        _liquiMolyScraper = liquiMolyScraper;
        _meguinScraper = meguinScraper;
        _barcodeReader = barcodeReader;
        _cacheSyncService = cacheSyncService;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    [HttpPost("scrape")]
    public async Task<IActionResult> TriggerScrape()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("LiquiMolyProductScrapeJob"));

        return Ok(new { Message = "Liqui-Moly product scrape triggered" });
    }

    [HttpPost("sync-neon")]
    public async Task<IActionResult> SyncNeon(CancellationToken ct)
    {
        await _neonSyncService.SyncFromCacheAsync(ct);

        return Ok(new
        {
            Message = "Liqui-Moly cache sync to Neon completed successfully"
        });
    }

    /// <summary>
    /// Manual scrape by article numbers for Liqui Moly or Meguin.
    /// Scraped products are upserted to both Cache and Neon stores and exported to an Excel file.
    /// </summary>
    [HttpPost("scrape-by-articles")]
    public async Task<IActionResult> ScrapeByArticles(
        [FromBody] ManualProductScrapeRequest request,
        CancellationToken ct)
    {
        if (request == null)
            return BadRequest(new { message = "Request body is required." });

        var brand = NormalizeBrand(request.Brand);
        if (brand == null)
            return BadRequest(new { message = "brand must be one of: LiquiMoly, Meguin." });

        var requestedArticles = (request.ArticleNumbers ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedArticles.Count == 0)
            return BadRequest(new { message = "At least one article number is required." });

        var invalidArticles = requestedArticles
            .Where(x => !ArticlePattern.IsMatch(x))
            .ToList();

        var validArticles = requestedArticles
            .Where(x => ArticlePattern.IsMatch(x))
            .ToList();

        if (validArticles.Count == 0)
        {
            return BadRequest(new
            {
                message = "No valid article numbers found. Expected 3-6 numeric digits.",
                invalidArticles
            });
        }

        List<LiquiMolyProductDto> scraped;
        if (string.Equals(brand, "Meguin", StringComparison.OrdinalIgnoreCase))
            scraped = await _meguinScraper.ScrapeByArticleNumbersAsync(validArticles, ct);
        else
            scraped = await _liquiMolyScraper.ScrapeByArticleNumbersAsync(validArticles, ct);

        // Barcode enrichment is best-effort so scraping still succeeds when SAP is unavailable.
        try
        {
            await _barcodeReader.EnrichAsync(scraped, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Manual scrape barcode enrichment failed (non-fatal) | Brand={Brand} | Requested={Requested}",
                brand, validArticles.Count);
        }

        if (scraped.Count > 0)
        {
            await _cacheSyncService.UpsertAsync(scraped);
            await _neonSyncService.UpsertAsync(scraped);
        }

        var foundSet = scraped
            .Select(x => x.ArticleNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingArticles = validArticles
            .Where(x => !foundSet.Contains(x))
            .ToList();

        var exportHeaders = new[]
        {
            "Brand",
            "ArticleNumber",
            "Status",
            "Name",
            "Category",
            "SubCategory",
            "PackagingSize",
            "SpecGrade",
            "ImageUrl",
            "ProductUrl",
            "PrimaryBarcode",
            "PrimaryBarcodeUom",
            "HasUnitBarcode",
            "Message"
        };

        var exportRows = new List<IReadOnlyList<string?>>();
        foreach (var product in scraped.OrderBy(x => x.ArticleNumber, StringComparer.OrdinalIgnoreCase))
        {
            exportRows.Add(new List<string?>
            {
                brand,
                product.ArticleNumber,
                "SCRAPED",
                product.Name,
                product.Category,
                product.SubCategory,
                product.PackagingSize,
                product.SpecGrade,
                product.ImageUrl,
                product.ProductUrl,
                product.PrimaryBarcode,
                product.PrimaryBarcodeUomCode,
                product.HasUnitBarcode ? "YES" : "NO",
                null
            });
        }

        foreach (var article in missingArticles)
        {
            exportRows.Add(new List<string?>
            {
                brand,
                article,
                "NOT_FOUND",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "Article was not found on the source website."
            });
        }

        foreach (var article in invalidArticles)
        {
            exportRows.Add(new List<string?>
            {
                brand,
                article,
                "INVALID",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "Article format must be 3-6 numeric digits."
            });
        }

        var bytes = SimpleXlsxWriter.BuildWorkbook($"{brand} Scrape", exportHeaders, exportRows);

        var exportDirectory = Path.Combine(_hostEnvironment.ContentRootPath, "exports", "manual-scrapes");
        Directory.CreateDirectory(exportDirectory);

        var fileName = $"{brand}_manual_scrape_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        var filePath = Path.Combine(exportDirectory, fileName);
        await System.IO.File.WriteAllBytesAsync(filePath, bytes, ct);

        if (request.DownloadFile)
        {
            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        return Ok(new
        {
            message = "Manual scrape completed.",
            brand,
            requested = requestedArticles.Count,
            validRequested = validArticles.Count,
            invalidRequested = invalidArticles.Count,
            scraped = scraped.Count,
            missing = missingArticles.Count,
            savedToCache = scraped.Count,
            savedToNeon = scraped.Count,
            fileName,
            filePath,
            downloadUrl = $"/api/admin/liquimoly/scrape-exports/{fileName}"
        });
    }

    [HttpGet("scrape-exports/{fileName}")]
    public IActionResult DownloadScrapeExport(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return BadRequest(new { message = "fileName is required." });

        if (fileName.Contains('/') || fileName.Contains('\\'))
            return BadRequest(new { message = "Invalid fileName." });

        var exportDirectory = Path.Combine(_hostEnvironment.ContentRootPath, "exports", "manual-scrapes");
        var fullPath = Path.Combine(exportDirectory, fileName);

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = $"Export file '{fileName}' not found." });

        return PhysicalFile(
            fullPath,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static string? NormalizeBrand(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand))
            return "LiquiMoly";

        var normalized = brand.Trim().ToUpperInvariant();
        return normalized switch
        {
            "LIQUIMOLY" or "LIQUI_MOLY" or "LIQUI-MOLY" or "LM" => "LiquiMoly",
            "MEGUIN" or "MG" => "Meguin",
            _ => null
        };
    }
}

public class ManualProductScrapeRequest
{
    public string? Brand { get; set; }
    public List<string> ArticleNumbers { get; set; } = new();
    public bool DownloadFile { get; set; } = false;
}
