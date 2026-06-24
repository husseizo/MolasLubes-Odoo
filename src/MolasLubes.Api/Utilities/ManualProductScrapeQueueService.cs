using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Integrations.Meguin;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Sync;

namespace MolasLubes.Api.Utilities;

public sealed class ManualProductScrapeQueueService
{
    private static readonly Regex ArticlePattern = new(@"^\d{3,6}$", RegexOptions.Compiled);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly ILogger<ManualProductScrapeQueueService> _logger;
    private readonly Channel<ManualProductScrapeJobState> _queue;
    private readonly ConcurrentDictionary<string, ManualProductScrapeJobState> _jobs;

    public ManualProductScrapeQueueService(
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment hostEnvironment,
        ILogger<ManualProductScrapeQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _hostEnvironment = hostEnvironment;
        _logger = logger;

        _queue = Channel.CreateUnbounded<ManualProductScrapeJobState>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _jobs = new ConcurrentDictionary<string, ManualProductScrapeJobState>(StringComparer.OrdinalIgnoreCase);

        _ = Task.Run(RunWorkerAsync);
    }

    public (bool ok, string? error, ManualProductScrapeJobSnapshot? job) Enqueue(
        string? brandRaw,
        IReadOnlyCollection<string>? articleNumbers,
        string? exportModeRaw)
    {
        var brand = NormalizeBrand(brandRaw);
        if (brand == null)
            return (false, "brand must be one of: LiquiMoly, Meguin.", null);

        var exportMode = NormalizeExportMode(exportModeRaw);
        if (exportMode == null)
            return (false, "exportMode must be one of: SUMMARY, FULL.", null);

        var requestedArticles = (articleNumbers ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedArticles.Count == 0)
            return (false, "At least one article number is required.", null);

        var invalidArticles = requestedArticles
            .Where(x => !ArticlePattern.IsMatch(x))
            .ToList();

        var validArticles = requestedArticles
            .Where(x => ArticlePattern.IsMatch(x))
            .ToList();

        if (validArticles.Count == 0)
            return (false, "No valid article numbers found. Expected 3-6 numeric digits.", null);

        var job = new ManualProductScrapeJobState
        {
            JobId = Guid.NewGuid().ToString("N"),
            Brand = brand,
            RequestedArticles = requestedArticles,
            ValidArticles = validArticles,
            InvalidArticles = invalidArticles,
            ExportMode = exportMode,
            CreatedAtUtc = DateTime.UtcNow,
            Status = ManualProductScrapeJobStatus.Queued
        };

        _jobs[job.JobId] = job;
        _queue.Writer.TryWrite(job);

        return (true, null, ToSnapshot(job));
    }

    public bool TryGetJob(string jobId, out ManualProductScrapeJobSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(jobId))
            return false;

        if (!_jobs.TryGetValue(jobId, out var job))
            return false;

        snapshot = ToSnapshot(job);
        return true;
    }

    public bool TryGetCompletedExport(string jobId, out string fullPath, out string fileName, out string error)
    {
        fullPath = string.Empty;
        fileName = string.Empty;
        error = string.Empty;

        if (!_jobs.TryGetValue(jobId, out var job))
        {
            error = "Job not found.";
            return false;
        }

        if (job.Status != ManualProductScrapeJobStatus.Completed)
        {
            error = $"Job status is '{job.Status}', not COMPLETED.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(job.FilePath) || string.IsNullOrWhiteSpace(job.FileName))
        {
            error = "Completed job has no export file.";
            return false;
        }

        if (!File.Exists(job.FilePath))
        {
            error = "Export file is missing on server.";
            return false;
        }

        fullPath = job.FilePath;
        fileName = job.FileName;
        return true;
    }

    private async Task RunWorkerAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await ProcessJobAsync(job);
            }
            catch (Exception ex)
            {
                job.Status = ManualProductScrapeJobStatus.Failed;
                job.CompletedAtUtc = DateTime.UtcNow;
                job.Error = ex.Message;
                _logger.LogError(ex, "Manual scrape queued job failed unexpectedly | JobId={JobId}", job.JobId);
            }
        }
    }

    private async Task ProcessJobAsync(ManualProductScrapeJobState job)
    {
        job.Status = ManualProductScrapeJobStatus.Running;
        job.StartedAtUtc = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var liquiMolyScraper = scope.ServiceProvider.GetRequiredService<LiquiMolyProductScraperService>();
        var meguinScraper = scope.ServiceProvider.GetRequiredService<MeguinProductScraperService>();
        var barcodeReader = scope.ServiceProvider.GetRequiredService<SapProductBarcodeReader>();
        var barcodeWriter = scope.ServiceProvider.GetRequiredService<SapProductBarcodeWriter>();
        var cacheSyncService = scope.ServiceProvider.GetRequiredService<LiquiMolyCacheSyncService>();
        var neonSyncService = scope.ServiceProvider.GetRequiredService<LiquiMolyNeonSyncService>();

        var scraped = string.Equals(job.Brand, "Meguin", StringComparison.OrdinalIgnoreCase)
            ? await meguinScraper.ScrapeByArticleNumbersAsync(job.ValidArticles)
            : await liquiMolyScraper.ScrapeByArticleNumbersAsync(job.ValidArticles);

        try
        {
            await barcodeReader.EnrichAsync(scraped, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Manual scrape barcode enrichment failed (non-fatal) | JobId={JobId} | Brand={Brand} | Requested={Requested}",
                job.JobId, job.Brand, job.ValidArticles.Count);
        }

        try
        {
            await barcodeWriter.WriteAsync(scraped, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Manual scrape barcode write failed (non-fatal) | JobId={JobId} | Brand={Brand} | Requested={Requested}",
                job.JobId, job.Brand, job.ValidArticles.Count);
        }

        if (scraped.Count > 0)
        {
            await cacheSyncService.UpsertAsync(scraped);
            await neonSyncService.UpsertAsync(scraped);
        }

        var foundSet = scraped
            .Select(x => x.ArticleNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingArticles = job.ValidArticles
            .Where(x => !foundSet.Contains(x))
            .ToList();

        var exportHeaders = BuildExportHeaders(job.ExportMode);

        var exportRows = new List<IReadOnlyList<string?>>();
        foreach (var product in scraped.OrderBy(x => x.ArticleNumber, StringComparer.OrdinalIgnoreCase))
        {
            exportRows.Add(BuildExportRow(
                product,
                job.Brand,
                "SCRAPED",
                null,
                job.ExportMode,
                DateTime.UtcNow));
        }

        foreach (var article in missingArticles)
        {
            exportRows.Add(BuildMissingOrInvalidRow(
                article,
                job.Brand,
                "NOT_FOUND",
                "Article was not found on the source website.",
                job.ExportMode));
        }

        foreach (var article in job.InvalidArticles)
        {
            exportRows.Add(BuildMissingOrInvalidRow(
                article,
                job.Brand,
                "INVALID",
                "Article format must be 3-6 numeric digits.",
                job.ExportMode));
        }

        var bytes = SimpleXlsxWriter.BuildWorkbook($"{job.Brand} Scrape", exportHeaders, exportRows);

        var exportDirectory = Path.Combine(_hostEnvironment.ContentRootPath, "exports", "manual-scrapes");
        Directory.CreateDirectory(exportDirectory);

        var fileName = $"{job.Brand}_manual_scrape_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{job.JobId[..8]}.xlsx";
        var filePath = Path.Combine(exportDirectory, fileName);
        await File.WriteAllBytesAsync(filePath, bytes);

        job.ScrapedCount = scraped.Count;
        job.MissingCount = missingArticles.Count;
        job.FileName = fileName;
        job.FilePath = filePath;
        job.DownloadUrl = $"/api/admin/liquimoly/scrape-by-articles/jobs/{job.JobId}/download";
        job.Status = ManualProductScrapeJobStatus.Completed;
        job.CompletedAtUtc = DateTime.UtcNow;

        _logger.LogInformation(
            "Manual scrape queued job completed | JobId={JobId} | Brand={Brand} | Requested={Requested} | Scraped={Scraped} | Missing={Missing}",
            job.JobId, job.Brand, job.RequestedArticles.Count, job.ScrapedCount, job.MissingCount);
    }

    private static ManualProductScrapeJobSnapshot ToSnapshot(ManualProductScrapeJobState job)
    {
        return new ManualProductScrapeJobSnapshot
        {
            JobId = job.JobId,
            Status = job.Status.ToString().ToUpperInvariant(),
            Brand = job.Brand,
            ExportMode = job.ExportMode,
            CreatedAtUtc = job.CreatedAtUtc,
            StartedAtUtc = job.StartedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            Requested = job.RequestedArticles.Count,
            ValidRequested = job.ValidArticles.Count,
            InvalidRequested = job.InvalidArticles.Count,
            Scraped = job.ScrapedCount,
            Missing = job.MissingCount,
            FileName = job.FileName,
            DownloadUrl = job.DownloadUrl,
            Error = job.Error
        };
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

    private static string? NormalizeExportMode(string? exportModeRaw)
    {
        if (string.IsNullOrWhiteSpace(exportModeRaw))
            return "SUMMARY";

        var normalized = exportModeRaw.Trim().ToUpperInvariant();
        return normalized switch
        {
            "SUMMARY" => "SUMMARY",
            "FULL" => "FULL",
            _ => null
        };
    }

    private static IReadOnlyList<string> BuildExportHeaders(string exportMode)
    {
        if (string.Equals(exportMode, "FULL", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                "Brand",
                "Status",
                "Message",
                "ArticleNumber",
                "Name",
                "Category",
                "SubCategory",
                "Description",
                "SpecGrade",
                "PackagingSize",
                "ImageUrl",
                "ProductUrl",
                "IsActive",
                "ScrapedAt",
                "AllPackagingSizes",
                "AllImageUrls",
                "Approvals",
                "Specifications",
                "ProductInfoPdfUrl",
                "SafetyDataSheetPdfUrl",
                "Liter",
                "OverviewProperties",
                "Application",
                "LiquiMolyRecommendations",
                "SpecificationItems",
                "PrimaryBarcode",
                "PrimaryBarcodeUomCode",
                "PrimaryBarcodeUomName",
                "PrimaryBarcodeUomEntry",
                "PrimaryBarcodeBaseQtyInGroup",
                "HasUnitBarcode",
                "BarcodeResolutionStatus",
                "BarcodeResolutionNote",
                "AllBarcodes",
                "SapUomInfo"
            };
        }

        return new[]
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
    }

    private static IReadOnlyList<string?> BuildExportRow(
        LiquiMolyProductDto product,
        string brand,
        string status,
        string? message,
        string exportMode,
        DateTime scrapedAtUtc)
    {
        if (string.Equals(exportMode, "FULL", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string?>
            {
                brand,
                status,
                message,
                product.ArticleNumber,
                product.Name,
                product.Category,
                product.SubCategory,
                product.Description,
                product.SpecGrade,
                product.PackagingSize,
                product.ImageUrl,
                product.ProductUrl,
                "1",
                scrapedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                SerializeJson(product.AllPackagingSizes),
                SerializeJson(product.AllImageUrls),
                SerializeJson(product.Approvals),
                SerializeJson(product.Specifications),
                product.ProductInfoPdfUrl,
                product.SafetyDataSheetPdfUrl,
                product.Liter?.ToString(CultureInfo.InvariantCulture),
                SerializeJson(product.OverviewProperties),
                product.Application,
                SerializeJson(product.LiquiMolyRecommendations),
                SerializeJson(product.SpecificationItems),
                product.PrimaryBarcode,
                product.PrimaryBarcodeUomCode,
                product.PrimaryBarcodeUomName,
                product.PrimaryBarcodeUomEntry?.ToString(CultureInfo.InvariantCulture),
                product.PrimaryBarcodeBaseQtyInGroup?.ToString(CultureInfo.InvariantCulture),
                product.HasUnitBarcode ? "1" : "0",
                product.BarcodeResolutionStatus,
                product.BarcodeResolutionNote,
                SerializeJson(product.AllBarcodes),
                SerializeJson(product.SapUomInfo)
            };
        }

        return new List<string?>
        {
            brand,
            product.ArticleNumber,
            status,
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
            message
        };
    }

    private static IReadOnlyList<string?> BuildMissingOrInvalidRow(
        string articleNumber,
        string brand,
        string status,
        string message,
        string exportMode)
    {
        if (string.Equals(exportMode, "FULL", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string?>
            {
                brand,
                status,
                message,
                articleNumber,
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
                null
            };
        }

        return new List<string?>
        {
            brand,
            articleNumber,
            status,
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
            message
        };
    }

    private static string? SerializeJson<T>(T value)
    {
        if (value == null) return null;

        if (value is string s && string.IsNullOrWhiteSpace(s))
            return null;

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            var hasAny = false;
            foreach (var _ in enumerable)
            {
                hasAny = true;
                break;
            }

            if (!hasAny)
                return null;
        }

        return JsonSerializer.Serialize(value);
    }

    private enum ManualProductScrapeJobStatus
    {
        Queued,
        Running,
        Completed,
        Failed
    }

    private sealed class ManualProductScrapeJobState
    {
        public string JobId { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string ExportMode { get; set; } = "SUMMARY";
        public List<string> RequestedArticles { get; set; } = new();
        public List<string> ValidArticles { get; set; } = new();
        public List<string> InvalidArticles { get; set; } = new();

        public ManualProductScrapeJobStatus Status { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }

        public int ScrapedCount { get; set; }
        public int MissingCount { get; set; }

        public string? FileName { get; set; }
        public string? FilePath { get; set; }
        public string? DownloadUrl { get; set; }
        public string? Error { get; set; }
    }
}

public sealed class ManualProductScrapeJobSnapshot
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = "QUEUED";
    public string Brand { get; set; } = string.Empty;
    public string ExportMode { get; set; } = "SUMMARY";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int Requested { get; set; }
    public int ValidRequested { get; set; }
    public int InvalidRequested { get; set; }
    public int Scraped { get; set; }
    public int Missing { get; set; }
    public string? FileName { get; set; }
    public string? DownloadUrl { get; set; }
    public string? Error { get; set; }
}
