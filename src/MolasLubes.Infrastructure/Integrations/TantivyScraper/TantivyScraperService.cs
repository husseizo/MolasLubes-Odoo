using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using MolasLubes.Infrastructure.Integrations.TantivyScraper.Dtos;

namespace MolasLubes.Infrastructure.Integrations.TantivyScraper;

/// <summary>
/// Playwright-based scraper for VIKA (catalogue.vikadpa.com) and
/// Borsehung (parts.borsehung.de). Both sites share the same catalog
/// platform and CSS selectors.
///
/// Usage: call InitAsync() once per job run, then TryScrapeAsync() per
/// item. Dispose via DisposeAsync() when the job completes.
/// </summary>
public sealed class TantivyScraperService : IAsyncDisposable
{
    private const string SearchBoxSelector  = "#search-text";
    private const string ResultLinkSelector = "#allcontent > div.content-table.container > div a";
    private const string PartNameSelector   = ".content-header";
    private const string SpecRowSelector    = ".art-item-text table tbody tr";
    private const string OemRefSelector     = ".art-item-oe-value";
    private const string AppRowSelector     = ".container table.table-striped tbody tr";
    private const string ImageSelector      = ".art-item-image img, img.art-image, .product-image img";

    private readonly TantivyScraperSettings          _settings;
    private readonly ILogger<TantivyScraperService>  _logger;

    private IPlaywright? _playwright;
    private IBrowser?    _browser;

    public TantivyScraperService(
        IOptions<TantivyScraperSettings> settings,
        ILogger<TantivyScraperService> logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    public async Task InitAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _settings.Headless,
            Args     = ["--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu"]
        });

        _logger.LogInformation("TantivyScraperService: Playwright browser initialized (headless={H})",
            _settings.Headless);
    }

    public async Task<TantivyScrapedDto?> TryScrapeAsync(
        TantivySeedDto seed,
        CancellationToken ct = default)
    {
        if (_browser == null)
            throw new InvalidOperationException("Call InitAsync() before scraping");

        var articleNo = seed.ArticleNo?.Trim();
        if (string.IsNullOrEmpty(articleNo))
        {
            _logger.LogWarning("TantivyScraper: no ArticleNo for {Code} — skipped", seed.ItemCode);
            return null;
        }

        var searchUrl = seed.Brand.ToUpperInvariant() == "BORSEHUNG"
            ? _settings.BorsehungBaseUrl
            : _settings.VikaBaseUrl;

        _logger.LogInformation(
            "TantivyScraper: scraping | {Code} | Brand={Brand} | Article={Article}",
            seed.ItemCode, seed.Brand, articleNo);

        IPage? page = null;
        try
        {
            page = await _browser.NewPageAsync();
            page.SetDefaultTimeout(_settings.PageTimeoutMs);

            // ── 1. Navigate to catalog home ───────────────────────────────
            await page.GotoAsync(searchUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            // ── 2. Search by article number ───────────────────────────────
            await page.FillAsync(SearchBoxSelector, articleNo);
            await page.PressAsync(SearchBoxSelector, "Enter");

            // ── 3. Wait for first result link ─────────────────────────────
            string? productUrl;
            try
            {
                await page.WaitForSelectorAsync(ResultLinkSelector, new PageWaitForSelectorOptions
                {
                    Timeout = _settings.SearchTimeoutMs
                });

                var firstLink = await page.QuerySelectorAsync(ResultLinkSelector);
                productUrl = firstLink != null
                    ? await firstLink.GetAttributeAsync("href")
                    : null;
            }
            catch (TimeoutException)
            {
                _logger.LogInformation(
                    "TantivyScraper: no results | {Code} | Article={Article}",
                    seed.ItemCode, articleNo);
                return null;
            }

            if (string.IsNullOrEmpty(productUrl))
            {
                _logger.LogInformation(
                    "TantivyScraper: first result has no href | {Code}", seed.ItemCode);
                return null;
            }

            // ── 4. Navigate to product page ───────────────────────────────
            // Sites return relative hrefs (e.g. /article.cshtml?art=B18856).
            // Resolve against the origin before calling GotoAsync.
            if (productUrl.StartsWith("/"))
            {
                var origin = new Uri(searchUrl).GetLeftPart(UriPartial.Authority);
                productUrl = origin + productUrl;
            }

            await page.GotoAsync(productUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            await Task.Delay(_settings.DelayBetweenRequestsMs, ct);

            return await ScrapeProductPageAsync(seed.ItemCode, productUrl, page);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "TantivyScraper: error | {Code} | Article={Article}",
                seed.ItemCode, articleNo);
            throw;
        }
        finally
        {
            if (page != null)
                await page.CloseAsync();
        }
    }

    // =====================================================
    // PRODUCT PAGE SCRAPE
    // =====================================================

    private async Task<TantivyScrapedDto> ScrapeProductPageAsync(
        string itemCode,
        string url,
        IPage page)
    {
        var dto = new TantivyScrapedDto
        {
            ItemCode   = itemCode,
            ProductUrl = url
        };

        // Part name
        var nameEl  = await page.QuerySelectorAsync(PartNameSelector);
        dto.PartName = nameEl != null
            ? (await nameEl.InnerTextAsync()).Trim()
            : null;

        // Specifications (key: value rows)
        var specRows = await page.QuerySelectorAllAsync(SpecRowSelector);
        var specs    = new List<string>();
        foreach (var row in specRows)
        {
            var text = (await row.InnerTextAsync()).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                specs.Add(text);
        }
        if (specs.Count > 0)
            dto.Specifications = JsonSerializer.Serialize(specs);

        // OEM reference numbers
        var refEls = await page.QuerySelectorAllAsync(OemRefSelector);
        var refs   = new List<string>();
        foreach (var el in refEls)
        {
            var text = (await el.InnerTextAsync()).Replace(" ", "").Trim();
            if (!string.IsNullOrWhiteSpace(text))
                refs.Add(text);
        }
        if (refs.Count > 0)
            dto.ReferenceNumbers = JsonSerializer.Serialize(refs);

        // Vehicle application rows
        var appRows = await page.QuerySelectorAllAsync(AppRowSelector);
        var apps    = new List<List<string>>();
        foreach (var row in appRows)
        {
            var cells     = await row.QuerySelectorAllAsync("td");
            var cellTexts = new List<string>();
            foreach (var cell in cells)
                cellTexts.Add((await cell.InnerTextAsync()).Trim());

            if (cellTexts.Any(t => !string.IsNullOrWhiteSpace(t)))
                apps.Add(cellTexts);
        }
        if (apps.Count > 0)
            dto.Applications = JsonSerializer.Serialize(apps);

        // First product image
        var imgEl = await page.QuerySelectorAsync(ImageSelector);
        if (imgEl != null)
            dto.ImageUrl = await imgEl.GetAttributeAsync("src");

        _logger.LogDebug(
            "TantivyScraper: page scraped | {Code} | Name={Name} | Refs={R} | Apps={A}",
            itemCode, dto.PartName, refs.Count, apps.Count);

        return dto;
    }

    public async Task RecreateAsync()
    {
        if (_browser != null)
        {
            await _browser.DisposeAsync();
            _browser = null;
        }
        _playwright?.Dispose();
        _playwright = null;

        _logger.LogInformation("TantivyScraperService: browser disposed for recreation");
        await InitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
            await _browser.DisposeAsync();

        _playwright?.Dispose();

        _logger.LogInformation("TantivyScraperService: browser disposed");
    }
}
