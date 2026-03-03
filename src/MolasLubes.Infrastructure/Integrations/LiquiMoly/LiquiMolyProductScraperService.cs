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
/// Strategy (listing page):
///   1. JSON-LD structured data embedded in each page's &lt;script type="application/ld+json"&gt;
///      blocks — most reliable when present (Shopware 6 injects it for SEO).
///   2. HTML product cards — Shopware 6 renders each product in a
///      &lt;div class="product-box"&gt; container with well-known child elements.
///   3. Generic fallback selectors for headings and data-attributes.
///
/// After collecting the product list each product's detail page is fetched to
/// extract the full set of:
///   - All gallery images
///   - All packaging/volume variants
///   - Approvals &amp; Specifications
///   - Download PDFs (Product Information EN + Safety Data Sheet EN)
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

    /// <summary>
    /// Searches Liqui-Moly for each supplied product one at a time, behaving
    /// like a human browsing the site:
    /// <list type="bullet">
    ///   <item>Randomises the order before starting.</item>
    ///   <item>Derives a clean search term from <c>ItemName</c> (strips the
    ///         leading article-number prefix and the trailing packaging-size
    ///         suffix so the text search finds the right product family).</item>
    ///   <item>Inserts a randomised delay between every request.</item>
    ///   <item>Adds an occasional longer "reading" pause every 8-12 items.</item>
    /// </list>
    ///
    /// Because the site-search endpoint does not accept bare numeric queries
    /// (they redirect to the oil guide), the scraper uses the product name as the
    /// search term.  On the search-results page it looks for an exact article-
    /// number match in the JSON-LD / HTML; if none is found it inspects the top
    /// candidate detail pages to locate the matching packaging-variant SKU.
    /// </summary>
    /// <param name="items">
    ///   Pairs of (ArticleNumber, ItemName) — e.g. ("1035", "1035-Hypoid Gear Oil (GL5) 85W90-1L").
    /// </param>
    public async Task<List<LiquiMolyProductDto>> ScrapeByArticleNumbersAsync(
        IEnumerable<(string ArticleNumber, string ItemName)> items,
        CancellationToken cancellationToken = default)
    {
        // De-duplicate, then shuffle to avoid predictable sequential patterns
        var work = items
            .Where(i => !string.IsNullOrWhiteSpace(i.ArticleNumber))
            .DistinctBy(i => i.ArticleNumber, StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        _logger.LogInformation(
            "[LiquiMoly] Starting human-like scrape for {Count} products", work.Count);

        var results    = new List<LiquiMolyProductDto>();
        int pauseEvery = Random.Shared.Next(8, 13); // longer pause every 8-12 items

        for (int i = 0; i < work.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var (articleNumber, itemName) = work[i];

            try
            {
                var dto = await ScrapeOneByNameAsync(articleNumber, itemName, cancellationToken);
                if (dto != null)
                {
                    results.Add(dto);
                    _logger.LogInformation(
                        "[LiquiMoly] [{Done}/{Total}] {Article}: {Name}",
                        results.Count, work.Count, dto.ArticleNumber, dto.Name);
                }
                else
                {
                    _logger.LogDebug(
                        "[LiquiMoly] [{I}/{Total}] No match found for {Article}",
                        i + 1, work.Count, articleNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[LiquiMoly] Error scraping article {Article}", articleNumber);
            }

            // Human-like variable delay: base ± 50 %
            int jitter = Random.Shared.Next(0, _settings.DelayBetweenRequestsMs / 2);
            await DelayAsync(_settings.DelayBetweenRequestsMs + jitter, cancellationToken);

            // Occasional longer "reading" pause
            if ((i + 1) % pauseEvery == 0)
            {
                pauseEvery = Random.Shared.Next(8, 13); // reset interval
                int reading = _settings.DelayBetweenCategoriesMs
                              + Random.Shared.Next(0, 2000);
                _logger.LogDebug("[LiquiMoly] Reading pause {Ms} ms", reading);
                await DelayAsync(reading, cancellationToken);
            }
        }

        _logger.LogInformation(
            "[LiquiMoly] Scrape complete: {Found}/{Total} matched",
            results.Count, work.Count);

        return results;
    }

    // ------------------------------------------------------------------
    // SINGLE PRODUCT LOOKUP  (search-by-name → detail page)
    // ------------------------------------------------------------------

    private async Task<LiquiMolyProductDto?> ScrapeOneByNameAsync(
        string articleNumber, string itemName, CancellationToken ct)
    {
        var term      = BuildSearchTerm(articleNumber, itemName);
        var searchUrl = BuildAbsolute("/en/search?q=" + Uri.EscapeDataString(term));

        var html = await FetchHtmlAsync(searchUrl, ct);
        if (string.IsNullOrWhiteSpace(html)) return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var candidates = ExtractFromJsonLd(doc, "Liqui-Moly", searchUrl);
        if (candidates.Count == 0)
            candidates = ExtractFromHtml(doc, "Liqui-Moly", searchUrl);

        if (candidates.Count == 0) return null;

        // ── Exact match in listing ──────────────────────────────────────
        var dto = candidates.FirstOrDefault(p =>
            string.Equals(p.ArticleNumber, articleNumber, StringComparison.OrdinalIgnoreCase));

        if (dto != null)
        {
            if (!string.IsNullOrWhiteSpace(dto.ProductUrl))
            {
                await DelayAsync(
                    _settings.DelayBetweenRequestsMs
                    + Random.Shared.Next(0, 800), ct);
                await EnrichFromDetailPageAsync(dto, ct);
            }
            return dto;
        }

        // ── No exact match: check top candidate detail pages for variant SKU ─
        foreach (var candidate in candidates.Take(3))
        {
            if (string.IsNullOrWhiteSpace(candidate.ProductUrl)) continue;

            await DelayAsync(
                _settings.DelayBetweenRequestsMs
                + Random.Shared.Next(0, 800), ct);

            var detailHtml = await FetchHtmlAsync(candidate.ProductUrl, ct);
            if (string.IsNullOrWhiteSpace(detailHtml)) continue;

            var detailDoc = new HtmlDocument();
            detailDoc.LoadHtml(detailHtml);

            if (!PageContainsVariantSku(detailDoc, articleNumber)) continue;

            // Found our variant on this product page — enrich in-place
            candidate.ArticleNumber = articleNumber;
            EnrichImages(candidate, detailDoc);
            EnrichPackagingSizes(candidate, detailDoc);
            EnrichDescription(candidate, detailDoc);
            EnrichApprovalsAndSpecs(candidate, detailDoc);
            EnrichDownloads(candidate, detailDoc);
            return candidate;
        }

        return null;
    }

    // ------------------------------------------------------------------
    // HELPERS — SEARCH TERM + VARIANT SKU CHECK
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds a clean text search term from the SAP ItemName.
    /// e.g. "1035-Hypoid Gear Oil (GL5) 85W90-1L"  →  "Hypoid Gear Oil GL5 85W90"
    /// </summary>
    private static string BuildSearchTerm(string articleNumber, string itemName)
    {
        var name = string.IsNullOrWhiteSpace(itemName) ? articleNumber : itemName.Trim();

        // Strip leading "{code}-" prefix
        if (name.StartsWith(articleNumber + "-", StringComparison.OrdinalIgnoreCase))
            name = name[(articleNumber.Length + 1)..].Trim();

        // Strip trailing packaging-size suffix  e.g. "-1L", "- 20 l", "-500ml"
        name = Regex.Replace(
            name,
            @"\s*[-–]\s*\d+(?:[.,]\d+)?\s*(?:ml|l|L|kg|g)\s*$",
            string.Empty,
            RegexOptions.IgnoreCase).Trim();

        // Remove brackets so the search engine treats contents as keywords
        name = name.Replace("(", " ").Replace(")", " ");
        name = Regex.Replace(name, @"\s{2,}", " ").Trim();

        return name;
    }

    /// <summary>
    /// Returns true when the product detail page references <paramref name="sku"/>
    /// as a standalone token — i.e. it appears surrounded by non-digit characters.
    /// Checks both JSON-LD structured data and the plain-text page content.
    /// </summary>
    private static bool PageContainsVariantSku(HtmlDocument doc, string sku)
    {
        // JSON-LD first (fastest + most precise)
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts != null)
        {
            foreach (var script in scripts)
            {
                try
                {
                    using var root = JsonDocument.Parse(script.InnerText.Trim());
                    if (JsonLdContainsSku(root.RootElement, sku)) return true;
                }
                catch { /* malformed JSON — skip */ }
            }
        }

        // Word-boundary check in visible page text
        var pattern = $@"(?<!\d){Regex.Escape(sku)}(?!\d)";
        return Regex.IsMatch(doc.DocumentNode.InnerText, pattern);
    }

    private static bool JsonLdContainsSku(JsonElement el, string sku)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                if (el.TryGetProperty("sku", out var skuEl) &&
                    string.Equals(skuEl.GetString(), sku, StringComparison.OrdinalIgnoreCase))
                    return true;
                foreach (var prop in el.EnumerateObject())
                    if (JsonLdContainsSku(prop.Value, sku)) return true;
                break;

            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    if (JsonLdContainsSku(item, sku)) return true;
                break;
        }
        return false;
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

        // ── Enrich each product with detail-page data ─────────────────
        for (int i = 0; i < products.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (!string.IsNullOrWhiteSpace(products[i].ProductUrl))
            {
                try
                {
                    await EnrichFromDetailPageAsync(products[i], cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[LiquiMoly] Failed to enrich product {Article} from detail page",
                        products[i].ArticleNumber);
                }
            }

            await DelayAsync(_settings.DelayBetweenRequestsMs, cancellationToken);
        }

        return products;
    }

    // ------------------------------------------------------------------
    // PRODUCT DETAIL PAGE ENRICHMENT
    // ------------------------------------------------------------------

    /// <summary>
    /// Fetches the individual product page and populates:
    ///   - AllImageUrls        (full gallery)
    ///   - AllPackagingSizes   (all volume variants)
    ///   - Approvals           (OEM / industry approvals list)
    ///   - Specifications      (key-value table)
    ///   - ProductInfoPdfUrl   (English Production Information PDF)
    ///   - SafetyDataSheetPdfUrl (English Safety Data Sheet PDF)
    ///   - Description         (full description, overrides listing-page teaser)
    /// </summary>
    private async Task EnrichFromDetailPageAsync(
        LiquiMolyProductDto dto,
        CancellationToken ct)
    {
        var html = await FetchHtmlAsync(dto.ProductUrl!, ct);
        if (string.IsNullOrWhiteSpace(html)) return;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        EnrichImages(dto, doc);
        EnrichPackagingSizes(dto, doc);
        EnrichDescription(dto, doc);
        EnrichApprovalsAndSpecs(dto, doc);
        EnrichDownloads(dto, doc);
    }

    // ── Gallery images ────────────────────────────────────────────────

    private void EnrichImages(LiquiMolyProductDto dto, HtmlDocument doc)
    {
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var images = new List<string>();

        // Shopware 6 gallery thumbnails / slider images
        var imgNodes = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'gallery')]//img" +
            "|//div[contains(@class,'product-image')]//img" +
            "|//div[contains(@class,'cms-image-container')]//img" +
            "|//div[contains(@class,'product-detail-media')]//img");

        if (imgNodes != null)
        {
            foreach (var img in imgNodes)
            {
                var src = img.GetAttributeValue("src",       null)
                       ?? img.GetAttributeValue("data-src",  null)
                       ?? img.GetAttributeValue("data-lazy", null);

                var url = BuildAbsoluteOrNull(src);
                if (url != null && seen.Add(url))
                    images.Add(url);
            }
        }

        // Fallback: JSON-LD image array on the detail page
        var scriptNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scriptNodes != null)
        {
            foreach (var script in scriptNodes)
            {
                try
                {
                    using var root = JsonDocument.Parse(script.InnerText.Trim());
                    ExtractJsonLdImages(root.RootElement, seen, images);
                }
                catch { /* malformed JSON – skip */ }
            }
        }

        if (images.Count > 0)
        {
            dto.AllImageUrls = images;
            if (string.IsNullOrWhiteSpace(dto.ImageUrl))
                dto.ImageUrl = images[0];
        }
    }

    private void ExtractJsonLdImages(JsonElement el, HashSet<string> seen, List<string> images)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                ExtractJsonLdImages(item, seen, images);
            return;
        }

        if (el.ValueKind != JsonValueKind.Object) return;

        if (el.TryGetProperty("image", out var imgEl))
        {
            if (imgEl.ValueKind == JsonValueKind.String)
            {
                var url = BuildAbsoluteOrNull(imgEl.GetString());
                if (url != null && seen.Add(url)) images.Add(url);
            }
            else if (imgEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in imgEl.EnumerateArray())
                {
                    var url = item.ValueKind == JsonValueKind.String
                        ? BuildAbsoluteOrNull(item.GetString())
                        : item.TryGetProperty("url", out var u)
                            ? BuildAbsoluteOrNull(u.GetString())
                            : null;

                    if (url != null && seen.Add(url)) images.Add(url);
                }
            }
        }

        if (el.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
            foreach (var g in graph.EnumerateArray())
                ExtractJsonLdImages(g, seen, images);
    }

    // ── Packaging / volume variants ───────────────────────────────────

    private void EnrichPackagingSizes(LiquiMolyProductDto dto, HtmlDocument doc)
    {
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sizes = new List<string>();

        // Variant selector buttons / options (common Shopware 6 patterns)
        var variantNodes = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'variant-configurator')]//button" +
            "|//div[contains(@class,'product-configurator')]//input[@type='radio']" +
            "|//ul[contains(@class,'configurator-options')]//li" +
            "|//select[contains(@class,'variant-select')]//option" +
            "|//div[contains(@class,'product-detail-configurator')]//label");

        if (variantNodes != null)
        {
            foreach (var node in variantNodes)
            {
                var text = node.InnerText?.Trim() ?? string.Empty;
                var m    = _sizePattern.Match(text);
                if (m.Success)
                {
                    var size = m.Groups[1].Value.Trim();
                    if (seen.Add(size)) sizes.Add(size);
                }
            }
        }

        // Also scan for data-attributes used by JS-driven selectors
        var attrNodes = doc.DocumentNode.SelectNodes(
            "//*[@data-volume or @data-size or @data-packaging]");

        if (attrNodes != null)
        {
            foreach (var node in attrNodes)
            {
                var raw = node.GetAttributeValue("data-volume",    null)
                       ?? node.GetAttributeValue("data-size",      null)
                       ?? node.GetAttributeValue("data-packaging",  null);

                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var m = _sizePattern.Match(raw);
                    if (m.Success)
                    {
                        var size = m.Groups[1].Value.Trim();
                        if (seen.Add(size)) sizes.Add(size);
                    }
                }
            }
        }

        if (sizes.Count > 0)
        {
            dto.AllPackagingSizes = sizes;
            if (string.IsNullOrWhiteSpace(dto.PackagingSize))
                dto.PackagingSize = sizes[0];
        }
    }

    // ── Description ───────────────────────────────────────────────────

    private static void EnrichDescription(LiquiMolyProductDto dto, HtmlDocument doc)
    {
        // Full description block (overrides the short teaser from listing page)
        var descNode =
            doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class,'product-detail-description')]") ??
            doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class,'product-description-content')]") ??
            doc.DocumentNode.SelectSingleNode(
                "//div[@itemprop='description']") ??
            doc.DocumentNode.SelectSingleNode(
                "//section[contains(@class,'description')]");

        if (descNode != null)
        {
            var text = HtmlEntity.DeEntitize(descNode.InnerText).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                dto.Description = text;
        }
    }

    // ── Approvals & Specifications ────────────────────────────────────

    private static void EnrichApprovalsAndSpecs(LiquiMolyProductDto dto, HtmlDocument doc)
    {
        // ── Specifications table (dl/dt/dd or table rows) ─────────────
        var specs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var specsSectionNode =
            doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class,'product-specifications')]" +
                "|//section[contains(@class,'specifications')]" +
                "|//div[contains(@class,'technical-data')]" +
                "|//div[contains(@class,'product-detail-properties')]");

        if (specsSectionNode != null)
        {
            // dl / dt dd pattern
            var dtNodes = specsSectionNode.SelectNodes(".//dt");
            var ddNodes = specsSectionNode.SelectNodes(".//dd");
            if (dtNodes != null && ddNodes != null)
            {
                int len = Math.Min(dtNodes.Count, ddNodes.Count);
                for (int i = 0; i < len; i++)
                {
                    var key   = HtmlEntity.DeEntitize(dtNodes[i].InnerText).Trim();
                    var value = HtmlEntity.DeEntitize(ddNodes[i].InnerText).Trim();
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                        specs[key] = value;
                }
            }

            // table row pattern (th/td pairs)
            var rows = specsSectionNode.SelectNodes(".//tr");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes(".//th|.//td");
                    if (cells is { Count: >= 2 })
                    {
                        var key   = HtmlEntity.DeEntitize(cells[0].InnerText).Trim();
                        var value = HtmlEntity.DeEntitize(cells[1].InnerText).Trim();
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                            specs[key] = value;
                    }
                }
            }
        }

        if (specs.Count > 0)
            dto.Specifications = specs;

        // ── Approvals list ────────────────────────────────────────────
        var approvals = new List<string>();

        var approvalsSectionNode =
            doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class,'product-approvals')]" +
                "|//section[contains(@class,'approvals')]" +
                "|//div[contains(@class,'approvals-certifications')]" +
                "|//div[contains(@class,'product-detail-approvals')]");

        // Approvals can be: a list of <li>, a series of <span>, or a flat text block
        if (approvalsSectionNode != null)
        {
            var liNodes = approvalsSectionNode.SelectNodes(".//li");
            if (liNodes != null)
            {
                foreach (var li in liNodes)
                {
                    var text = HtmlEntity.DeEntitize(li.InnerText).Trim();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length < 200)
                        approvals.Add(text);
                }
            }

            if (approvals.Count == 0)
            {
                // Flat text split by comma / semicolon / newline
                var raw = HtmlEntity.DeEntitize(approvalsSectionNode.InnerText).Trim();
                approvals.AddRange(
                    raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0 && s.Length < 200));
            }
        }

        if (approvals.Count > 0)
            dto.Approvals = approvals;
    }

    // ── Downloads (PDFs) ──────────────────────────────────────────────

    private void EnrichDownloads(LiquiMolyProductDto dto, HtmlDocument doc)
    {
        // Find all links to PDFs on the page
        var pdfLinks = doc.DocumentNode.SelectNodes(
            "//a[contains(@href,'.pdf')]" +
            "|//a[contains(@href,'download')]");

        if (pdfLinks == null) return;

        foreach (var link in pdfLinks)
        {
            var href = link.GetAttributeValue("href", null);
            if (string.IsNullOrWhiteSpace(href)) continue;

            var url = BuildAbsoluteOrNull(href);
            if (url == null) continue;

            var label = (link.GetAttributeValue("title", null)
                      ?? link.InnerText
                      ?? string.Empty).Trim().ToLowerInvariant();

            // Production / Product Information PDF (English)
            if (dto.ProductInfoPdfUrl == null &&
                (label.Contains("product information") ||
                 label.Contains("pi en")               ||
                 label.Contains("prod info")           ||
                 label.Contains("technical data")      ||
                 label.Contains("data sheet")          ||
                 (href.Contains("pi") && href.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))))
            {
                if (label.Contains("en") || href.Contains("/en/") || href.Contains("_en"))
                    dto.ProductInfoPdfUrl = url;
                else
                    dto.ProductInfoPdfUrl ??= url; // fallback: first match
            }

            // Safety Data Sheet PDF (English)
            if (dto.SafetyDataSheetPdfUrl == null &&
                (label.Contains("safety data sheet") ||
                 label.Contains("sds")               ||
                 label.Contains("msds")              ||
                 label.Contains("safety sheet")      ||
                 (href.Contains("sds") && href.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))))
            {
                if (label.Contains("en") || href.Contains("/en/") || href.Contains("_en"))
                    dto.SafetyDataSheetPdfUrl = url;
                else
                    dto.SafetyDataSheetPdfUrl ??= url;
            }
        }
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
            (el.TryGetProperty("sku",       out var skuEl) ? skuEl.GetString()?.Trim() : null) ??
            (el.TryGetProperty("productID", out var pidEl) ? pidEl.GetString()?.Trim() : null) ??
            (el.TryGetProperty("mpn",       out var mpnEl) ? mpnEl.GetString()?.Trim() : null);

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
