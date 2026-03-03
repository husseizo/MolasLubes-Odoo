using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MolasLubes.Infrastructure.Integrations.LiquiMoly;

/// <summary>
/// Scrapes the Liqui-Moly public product catalog and returns a flat list of
/// <see cref="LiquiMolyProductDto"/> objects.
///
/// Strategy (in order of preference):
///   1. JSON-LD structured data embedded in each page's &lt;script type="application/ld+json"&gt;
///      blocks — most reliable when present (Shopware 6 injects it for SEO).
///   2. HTML product cards — Shopware 6 renders each product in a
///      &lt;div class="product-box"&gt; container with well-known child elements.
///   3. Generic fallback selectors for headings and data-attributes.
///
/// The scraper is intentionally polite: it inserts a configurable delay
/// between every page request to avoid hammering the server.
/// </summary>
public class LiquiMolyProductScraperService
{
    private readonly HttpClient                           _http;
    private readonly LiquiMolyScraperSettings             _settings;
    private readonly ILogger<LiquiMolyProductScraperService> _logger;

    // Regex patterns shared across all calls
    private static readonly Regex _articlePattern   = new(@"(?:art(?:icle)?\.?\s*(?:no\.?|number)?\s*[:#]?\s*)(\d{4,6})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _specGradePattern = new(@"\b\d{1,2}W[-–]\d{2,3}\b|\bSAE\s+\d+\b|\bDEXRON\b|\bMERCON\b|\bATF\b|\bDOT\s*[3456]\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _sizePattern      = new(@"\b(\d+(?:[.,]\d+)?\s*(?:ml|l|L|kg|g|oz|lb))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _digitOnlyPattern = new(@"^\d{4,6}$", RegexOptions.Compiled);

    public LiquiMolyProductScraperService(
        HttpClient httpClient,
        IOptions<LiquiMolyScraperSettings> settings,
        ILogger<LiquiMolyProductScraperService> logger)
    {
        _http     = httpClient;
        _settings = settings.Value;
        _logger   = logger;
    }

    // ------------------------------------------------------------------
    // PUBLIC API
    // ------------------------------------------------------------------

    /// <summary>Scrapes all configured category pages and returns every product found.</summary>
    public async Task<List<LiquiMolyProductDto>> ScrapeAllProductsAsync(
        CancellationToken cancellationToken = default)
    {
        var all = new List<LiquiMolyProductDto>();

        foreach (var (path, category) in _settings.CategoryPaths)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var products = await ScrapeCategoryAsync(path, category, cancellationToken);
                all.AddRange(products);
                _logger.LogInformation(
                    "[LiquiMoly] Category '{Category}' → {Count} products",
                    category, products.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LiquiMoly] Failed to scrape category '{Category}'", category);
            }

            await DelayAsync(_settings.DelayBetweenCategoriesMs, cancellationToken);
        }

        _logger.LogInformation("[LiquiMoly] Total products scraped: {Total}", all.Count);
        return all;
    }

    // ------------------------------------------------------------------
    // CATEGORY PAGINATION
    // ------------------------------------------------------------------

    private async Task<List<LiquiMolyProductDto>> ScrapeCategoryAsync(
        string relativePath,
        string category,
        CancellationToken cancellationToken)
    {
        var products   = new List<LiquiMolyProductDto>();
        var nextUrl    = BuildAbsolute(relativePath);

        while (!string.IsNullOrEmpty(nextUrl) && !cancellationToken.IsCancellationRequested)
        {
            var html = await FetchHtmlAsync(nextUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(html)) break;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // 1. Try JSON-LD first (fastest + most reliable)
            var jsonLdProducts = ExtractFromJsonLd(doc, category, nextUrl);
            if (jsonLdProducts.Count > 0)
            {
                products.AddRange(jsonLdProducts);
            }
            else
            {
                // 2. Fall back to HTML parsing
                var htmlProducts = ExtractFromHtml(doc, category, nextUrl);
                products.AddRange(htmlProducts);
            }

            nextUrl = FindNextPageUrl(doc);

            if (!string.IsNullOrEmpty(nextUrl))
                await DelayAsync(_settings.DelayBetweenRequestsMs, cancellationToken);
        }

        return products;
    }

    // ------------------------------------------------------------------
    // JSON-LD EXTRACTION  (Shopware 6 / Google structured data)
    // ------------------------------------------------------------------

    private List<LiquiMolyProductDto> ExtractFromJsonLd(
        HtmlDocument doc, string category, string pageUrl)
    {
        var results = new List<LiquiMolyProductDto>();

        var scriptNodes = doc.DocumentNode
            .SelectNodes("//script[@type='application/ld+json']");

        if (scriptNodes == null) return results;

        foreach (var script in scriptNodes)
        {
            try
            {
                var json   = script.InnerText.Trim();
                using var root = JsonDocument.Parse(json);
                var el         = root.RootElement;

                // May be a single product or an ItemList/BreadcrumbList
                if (el.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in el.EnumerateArray())
                        TryAddJsonLdProduct(item, category, pageUrl, results);
                }
                else
                {
                    TryAddJsonLdProduct(el, category, pageUrl, results);

                    // @graph array
                    if (el.TryGetProperty("@graph", out var graph) &&
                        graph.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in graph.EnumerateArray())
                            TryAddJsonLdProduct(item, category, pageUrl, results);
                    }

                    // ItemListElement  (BreadcrumbList / OfferCatalog)
                    if (el.TryGetProperty("itemListElement", out var list) &&
                        list.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in list.EnumerateArray())
                            TryAddJsonLdProduct(item, category, pageUrl, results);
                    }
                }
            }
            catch
            {
                // Malformed JSON — skip this block
            }
        }

        return results;
    }

    private void TryAddJsonLdProduct(
        JsonElement el, string category, string pageUrl,
        List<LiquiMolyProductDto> results)
    {
        // Only process @type == "Product"
        if (!el.TryGetProperty("@type", out var typeEl)) return;
        if (!typeEl.GetString()?.Equals("Product", StringComparison.OrdinalIgnoreCase) ?? true) return;

        var name = el.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(name)) return;

        // Article number from "sku" or "productID" or derived from URL
        var articleNumber =
            (el.TryGetProperty("sku",       out var skuEl)       ? skuEl.GetString()?.Trim()       : null) ??
            (el.TryGetProperty("productID", out var pidEl)        ? pidEl.GetString()?.Trim()       : null) ??
            (el.TryGetProperty("mpn",       out var mpnEl)        ? mpnEl.GetString()?.Trim()       : null);

        if (string.IsNullOrWhiteSpace(articleNumber))
            articleNumber = ExtractArticleFromText(name ?? "");

        if (string.IsNullOrWhiteSpace(articleNumber)) return;

        var description = el.TryGetProperty("description", out var descEl)
            ? HtmlEntity.DeEntitize(descEl.GetString()?.Trim() ?? "")
            : null;

        var imageUrl = el.TryGetProperty("image", out var imgEl)
            ? (imgEl.ValueKind == JsonValueKind.String
                ? imgEl.GetString()
                : imgEl.TryGetProperty("url", out var imgUrlEl) ? imgUrlEl.GetString() : null)
            : null;

        var productUrl = el.TryGetProperty("url", out var urlEl)
            ? urlEl.GetString()
            : pageUrl;

        results.Add(new LiquiMolyProductDto
        {
            ArticleNumber = articleNumber!,
            Name          = HtmlEntity.DeEntitize(name),
            Category      = category,
            Description   = description,
            SpecGrade     = ExtractSpecGrade(name),
            PackagingSize = ExtractPackagingSize(name),
            ImageUrl      = imageUrl,
            ProductUrl    = productUrl,
        });
    }

    // ------------------------------------------------------------------
    // HTML EXTRACTION  (Shopware 6 product-box layout + fallbacks)
    // ------------------------------------------------------------------

    private List<LiquiMolyProductDto> ExtractFromHtml(
        HtmlDocument doc, string category, string pageUrl)
    {
        var results = new List<LiquiMolyProductDto>();

        // ── Primary: Shopware 6 product-box cards ──────────────────────
        var cards = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'product-box')]" +
            "|//article[contains(@class,'product-item')]" +
            "|//li[contains(@class,'product-item')]");

        if (cards != null)
        {
            foreach (var card in cards)
            {
                var dto = TryExtractFromCard(card, category, pageUrl);
                if (dto != null) results.Add(dto);
            }
        }

        // ── Secondary: any element with data-article-number ──────────
        if (results.Count == 0)
        {
            var dataNodes = doc.DocumentNode.SelectNodes(
                "//*[@data-article-number or @data-product-number or @data-sku]");

            if (dataNodes != null)
            {
                foreach (var node in dataNodes)
                {
                    var dto = TryExtractFromDataAttr(node, category, pageUrl);
                    if (dto != null) results.Add(dto);
                }
            }
        }

        return results;
    }

    private LiquiMolyProductDto? TryExtractFromCard(
        HtmlNode card, string category, string pageUrl)
    {
        // Article number — several possible locations
        var articleNumber =
            card.GetAttributeValue("data-article-number", null) ??
            card.GetAttributeValue("data-product-number", null) ??
            card.GetAttributeValue("data-sku",            null) ??
            card.SelectSingleNode(".//*[@data-article-number]")
                ?.GetAttributeValue("data-article-number", null) ??
            card.SelectSingleNode(".//*[contains(@class,'product-number') or contains(@class,'article-number')]")
                ?.InnerText?.Trim();

        if (!string.IsNullOrWhiteSpace(articleNumber))
            articleNumber = CleanArticleNumber(articleNumber);

        // Name
        var name =
            card.SelectSingleNode(".//*[contains(@class,'product-name')]//a")?.InnerText?.Trim() ??
            card.SelectSingleNode(".//*[contains(@class,'product-title')]")?.InnerText?.Trim() ??
            card.SelectSingleNode(".//h2|.//h3|.//h4")?.InnerText?.Trim();

        if (string.IsNullOrWhiteSpace(name)) return null;

        // Derive article number from name if still missing
        if (string.IsNullOrWhiteSpace(articleNumber))
            articleNumber = ExtractArticleFromText(name);

        if (string.IsNullOrWhiteSpace(articleNumber)) return null;

        // URL
        var linkHref = card.SelectSingleNode(".//*[contains(@class,'product-name')]//a")
                           ?.GetAttributeValue("href", null)
                       ?? card.SelectSingleNode(".//a[@href]")
                              ?.GetAttributeValue("href", null);
        var productUrl = BuildAbsoluteOrNull(linkHref);

        // Image
        var imgNode  = card.SelectSingleNode(".//img");
        var imageUrl = BuildAbsoluteOrNull(
            imgNode?.GetAttributeValue("src",       null) ??
            imgNode?.GetAttributeValue("data-src",  null) ??
            imgNode?.GetAttributeValue("data-lazy", null));

        // Description (teaser / short description)
        var description = card.SelectSingleNode(
                ".//*[contains(@class,'product-description') or contains(@class,'description-text')]")
            ?.InnerText?.Trim();

        return new LiquiMolyProductDto
        {
            ArticleNumber = articleNumber!,
            Name          = HtmlEntity.DeEntitize(name),
            Category      = category,
            Description   = description != null ? HtmlEntity.DeEntitize(description) : null,
            SpecGrade     = ExtractSpecGrade(name),
            PackagingSize = ExtractPackagingSize(name),
            ImageUrl      = imageUrl,
            ProductUrl    = productUrl ?? pageUrl,
        };
    }

    private LiquiMolyProductDto? TryExtractFromDataAttr(
        HtmlNode node, string category, string pageUrl)
    {
        var articleNumber = CleanArticleNumber(
            node.GetAttributeValue("data-article-number", null) ??
            node.GetAttributeValue("data-product-number", null) ??
            node.GetAttributeValue("data-sku",            null) ?? "");

        if (string.IsNullOrWhiteSpace(articleNumber)) return null;

        var name = node.SelectSingleNode(".//*[contains(@class,'name') or contains(@class,'title')]")
                       ?.InnerText?.Trim()
                   ?? node.InnerText.Trim();

        if (string.IsNullOrWhiteSpace(name)) return null;

        return new LiquiMolyProductDto
        {
            ArticleNumber = articleNumber,
            Name          = HtmlEntity.DeEntitize(name),
            Category      = category,
            SpecGrade     = ExtractSpecGrade(name),
            PackagingSize = ExtractPackagingSize(name),
            ProductUrl    = pageUrl,
        };
    }

    // ------------------------------------------------------------------
    // PAGINATION
    // ------------------------------------------------------------------

    private static string? FindNextPageUrl(HtmlDocument doc)
    {
        // <link rel="next" href="..."> (canonical pagination)
        var relNext = doc.DocumentNode.SelectSingleNode("//link[@rel='next']");
        if (relNext != null)
            return relNext.GetAttributeValue("href", null);

        // Pagination button/link
        var nextLink = doc.DocumentNode.SelectSingleNode(
            "//a[contains(@class,'pagination-next') or @rel='next' or contains(@aria-label,'Next')]");
        return nextLink?.GetAttributeValue("href", null);
    }

    // ------------------------------------------------------------------
    // HTTP
    // ------------------------------------------------------------------

    private async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/122.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LiquiMoly] Failed to fetch {Url}", url);
            return string.Empty;
        }
    }

    // ------------------------------------------------------------------
    // HELPERS
    // ------------------------------------------------------------------

    private string BuildAbsolute(string path)
        => path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : _settings.BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

    private string? BuildAbsoluteOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return BuildAbsolute(path);
    }

    private static string? CleanArticleNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var clean = _articlePattern.Match(raw);
        if (clean.Success) return clean.Groups[1].Value;
        // If it's already a bare number, keep it
        var bare = raw.Trim();
        return _digitOnlyPattern.IsMatch(bare) ? bare : null;
    }

    private static string? ExtractArticleFromText(string text)
    {
        var m = _articlePattern.Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? ExtractSpecGrade(string text)
    {
        var m = _specGradePattern.Match(text);
        return m.Success ? m.Value.Trim() : null;
    }

    private static string? ExtractPackagingSize(string text)
    {
        var m = _sizePattern.Match(text);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static Task DelayAsync(int ms, CancellationToken ct)
        => ms > 0 ? Task.Delay(ms, ct) : Task.CompletedTask;
}
