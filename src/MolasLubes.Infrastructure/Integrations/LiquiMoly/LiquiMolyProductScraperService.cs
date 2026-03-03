using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MolasLubes.Infrastructure.Integrations.LiquiMoly;

/// <summary>
/// Scrapes the Liqui-Moly public product catalog (Magento 2) and returns a flat list of
/// <see cref="LiquiMolyProductDto"/> objects.
///
/// Strategy — two-phase approach that avoids the broken site search
/// (all queries redirect to the oil-guide SPA):
///
/// Phase 1 — Category scan (no detail-page visits):
///   Walks each category listing page and notes stubs whose article number is in the
///   target set.  Magento 2 JSON-LD embeds a <c>Product</c> entry per product; the
///   <c>offers</c> array inside it contains one entry per packaging variant whose
///   <c>sku</c> field is the 4-digit variant article number (= CacheProducts ItemCode).
///   Pagination is followed via the static "Next" link (?p=N query string).
///
/// Phase 2 — Targeted detail enrichment (randomised + human-like delays):
///   Visits each matched product's detail page exactly once (multiple variants
///   sharing the same product URL are enriched in a single visit).
///   Packaging sizes come from the Magento 2 x-magento-init swatch-renderer config.
///   Description and approvals/specs from server-rendered tab divs.
///   Images from gallery containers and JSON-LD.
///   Variable inter-request delay (base ± 50 % jitter) plus occasional "reading" pauses.
/// </summary>
public class LiquiMolyProductScraperService
{
    private readonly HttpClient                              _http;
    private readonly LiquiMolyScraperSettings                _settings;
    private readonly ILogger<LiquiMolyProductScraperService> _logger;

    // 4-6 digit bare number = packaging variant SKU (e.g. "1035", "20465")
    private static readonly Regex _variantSkuPattern = new(@"^\d{4,6}$", RegexOptions.Compiled);

    private static readonly Regex _specGradePattern = new(
        @"\b\d{1,2}W[-–]\d{2,3}\b|\bSAE\s+\d+\b|\bDEXRON\b|\bMERCON\b|\bATF\b|\bDOT\s*[3456]\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _sizePattern = new(
        @"\b(\d+(?:[.,]\d+)?\s*(?:ml|l|L|kg|g|oz|lb))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches absolute PDF URLs hosted on Liqui-Moly's PIM CDN (may appear in JS strings)
    private static readonly Regex _pimPdfPattern = new(
        @"https://pim\.liqui-moly\.com/[^\s""'<>]+\.pdf",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    /// <summary>
    /// Two-phase scrape: scan category listings → enrich matched product detail pages.
    /// </summary>
    public async Task<List<LiquiMolyProductDto>> ScrapeByArticleNumbersAsync(
        IEnumerable<string> articleNumbers,
        CancellationToken cancellationToken = default)
    {
        var targets = new HashSet<string>(
            articleNumbers.Select(n => n.Trim()).Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(
            "[LiquiMoly] Phase 1 — scanning {Categories} categories for {Targets} target article numbers",
            _settings.CategoryPaths.Count, targets.Count);

        // ── Phase 1: Scan listing pages, collect matching stubs ────────
        var stubs = new List<LiquiMolyProductDto>();

        foreach (var (path, category) in _settings.CategoryPaths)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                var found = await ScanCategoryForTargetsAsync(path, category, targets, cancellationToken);
                stubs.AddRange(found);

                _logger.LogInformation(
                    "[LiquiMoly] Category '{Category}': {Found} variant stub(s) found",
                    category, found.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LiquiMoly] Failed to scan category '{Category}'", category);
            }

            await DelayAsync(
                _settings.DelayBetweenCategoriesMs + Random.Shared.Next(0, 1000),
                cancellationToken);
        }

        _logger.LogInformation(
            "[LiquiMoly] Phase 1 complete — {Found}/{Total} target(s) located across all categories",
            stubs.Count, targets.Count);

        if (stubs.Count == 0) return stubs;

        // ── Phase 2: Enrich, grouping by product URL to avoid duplicate visits ─
        _logger.LogInformation(
            "[LiquiMoly] Phase 2 — enriching detail pages ({Stubs} stubs, {Urls} unique URL(s))",
            stubs.Count,
            stubs.Select(s => s.ProductUrl ?? s.ArticleNumber)
                 .Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Group by product URL so each page is fetched only once
        var groups = stubs
            .GroupBy(s => s.ProductUrl ?? s.ArticleNumber, StringComparer.OrdinalIgnoreCase)
            .OrderBy(_ => Random.Shared.Next()) // shuffle for human-like ordering
            .ToList();

        var results    = new List<LiquiMolyProductDto>(stubs.Count);
        int pauseEvery = Random.Shared.Next(8, 13);

        for (int i = 0; i < groups.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var groupStubs     = groups[i].ToList();
            var representative = groupStubs[0]; // All stubs share the same product URL

            try
            {
                if (!string.IsNullOrWhiteSpace(representative.ProductUrl))
                    await EnrichFromDetailPageAsync(representative, cancellationToken);

                // Copy the enriched detail data to every other stub sharing this URL
                foreach (var stub in groupStubs.Skip(1))
                {
                    stub.AllImageUrls          = representative.AllImageUrls;
                    stub.AllPackagingSizes     = representative.AllPackagingSizes;
                    stub.Approvals             = representative.Approvals;
                    stub.Specifications        = representative.Specifications;
                    stub.ProductInfoPdfUrl     = representative.ProductInfoPdfUrl;
                    stub.SafetyDataSheetPdfUrl = representative.SafetyDataSheetPdfUrl;
                    stub.Description           ??= representative.Description;
                    stub.ImageUrl              ??= representative.ImageUrl;
                    if (string.IsNullOrWhiteSpace(stub.PackagingSize))
                        stub.PackagingSize = representative.PackagingSize;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[LiquiMoly] Failed to enrich detail page {Url}", representative.ProductUrl);
            }

            results.AddRange(groupStubs);

            _logger.LogInformation(
                "[LiquiMoly] [{Done}/{Total}] {Name} — {Variants} variant(s)",
                results.Count, stubs.Count, representative.Name, groupStubs.Count);

            // Variable delay: base ± 50 % jitter
            int delay = _settings.DelayBetweenRequestsMs
                      + Random.Shared.Next(0, _settings.DelayBetweenRequestsMs / 2);
            await DelayAsync(delay, cancellationToken);

            // Occasional "reading" pause every 8–12 products
            if ((i + 1) % pauseEvery == 0)
            {
                pauseEvery = Random.Shared.Next(8, 13);
                int reading = _settings.DelayBetweenCategoriesMs + Random.Shared.Next(0, 2000);
                _logger.LogDebug("[LiquiMoly] Reading pause {Ms} ms", reading);
                await DelayAsync(reading, cancellationToken);
            }
        }

        _logger.LogInformation(
            "[LiquiMoly] Phase 2 complete — {Count} product records produced", results.Count);

        return results;
    }

    // ------------------------------------------------------------------
    // PHASE 1: Scan one category, return only target stubs
    // ------------------------------------------------------------------

    private async Task<List<LiquiMolyProductDto>> ScanCategoryForTargetsAsync(
        string relativePath,
        string category,
        HashSet<string> targets,
        CancellationToken ct)
    {
        var found   = new List<LiquiMolyProductDto>();
        var nextUrl = BuildAbsolute(relativePath);

        while (!string.IsNullOrEmpty(nextUrl) && !ct.IsCancellationRequested)
        {
            var html = await FetchHtmlAsync(nextUrl, ct);
            if (string.IsNullOrWhiteSpace(html)) break;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // JSON-LD is more reliable (server-rendered for SEO); fall back to HTML cards
            var stubs = ExtractFromJsonLd(doc, category, nextUrl);
            if (stubs.Count == 0)
                stubs = ExtractFromHtml(doc, category, nextUrl);

            foreach (var stub in stubs)
            {
                if (targets.Contains(stub.ArticleNumber))
                    found.Add(stub);
            }

            nextUrl = FindNextPageUrl(doc);

            if (!string.IsNullOrEmpty(nextUrl))
                await DelayAsync(
                    _settings.DelayBetweenRequestsMs + Random.Shared.Next(0, 500), ct);
        }

        return found;
    }

    // ------------------------------------------------------------------
    // PRODUCT DETAIL PAGE ENRICHMENT
    // ------------------------------------------------------------------

    private async Task EnrichFromDetailPageAsync(
        LiquiMolyProductDto dto,
        CancellationToken ct)
    {
        var html = await FetchHtmlAsync(dto.ProductUrl!, ct);
        if (string.IsNullOrWhiteSpace(html)) return;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        EnrichImages(dto, doc);
        EnrichFromMagentoConfig(dto, doc);   // Packaging sizes from x-magento-init
        EnrichDescription(dto, doc);
        EnrichApprovalsAndSpecs(dto, doc);
        EnrichDownloads(dto, doc, html);     // Pass raw HTML for regex PDF URL extraction
    }

    // ── Gallery images ────────────────────────────────────────────────

    private void EnrichImages(LiquiMolyProductDto dto, HtmlDocument doc)
    {
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var images = new List<string>();

        // Magento 2 gallery containers
        var imgNodes = doc.DocumentNode.SelectNodes(
            "//div[@data-gallery-role='gallery']//img"             +
            "|//div[contains(@class,'fotorama')]//img"             +
            "|//div[contains(@class,'gallery-placeholder')]//img"  +
            "|//div[contains(@class,'product-image-container')]//img" +
            "|//div[contains(@class,'product-media')]//img");

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

        // Fallback: JSON-LD image array on the detail page (always server-rendered)
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

    // ── Packaging sizes from Magento 2 x-magento-init config ─────────

    private void EnrichFromMagentoConfig(LiquiMolyProductDto dto, HtmlDocument doc)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='text/x-magento-init']");
        if (scripts == null) return;

        foreach (var script in scripts)
        {
            try
            {
                using var root = JsonDocument.Parse(script.InnerText.Trim());

                // Navigate: {selector} → "Magento_Swatches/js/swatch-renderer" → "jsonConfig"
                foreach (var selector in root.RootElement.EnumerateObject())
                {
                    if (!selector.Value.TryGetProperty(
                            "Magento_Swatches/js/swatch-renderer", out var renderer)) continue;
                    if (!renderer.TryGetProperty("jsonConfig", out var jsonConfig)) continue;

                    ExtractPackagingSizesFromConfig(dto, jsonConfig);
                    return; // Done — only one swatch-renderer block expected
                }
            }
            catch { /* malformed JSON – skip */ }
        }
    }

    private void ExtractPackagingSizesFromConfig(LiquiMolyProductDto dto, JsonElement jsonConfig)
    {
        if (!jsonConfig.TryGetProperty("attributes", out var attrs) ||
            attrs.ValueKind != JsonValueKind.Object) return;

        foreach (var attr in attrs.EnumerateObject())
        {
            // Identify the packaging/volume attribute by its code or label
            var code  = attr.Value.TryGetProperty("code",  out var c) ? c.GetString()?.ToLower() ?? "" : "";
            var label = attr.Value.TryGetProperty("label", out var l) ? l.GetString()?.ToLower() ?? "" : "";

            bool isVolumeAttr =
                code.Contains("gebinde") || code.Contains("inhalt") ||
                code.Contains("volume")  || code.Contains("pack")   || code.Contains("size") ||
                label.Contains("container") || label.Contains("volume") ||
                label.Contains("pack")      || label.Contains("liter")  || label.Contains("size");

            if (!isVolumeAttr) continue;
            if (!attr.Value.TryGetProperty("options", out var opts) ||
                opts.ValueKind != JsonValueKind.Array) continue;

            var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sizes = new List<string>();

            foreach (var opt in opts.EnumerateArray())
            {
                var lbl = opt.TryGetProperty("label", out var lb) ? lb.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(lbl)) continue;

                var m  = _sizePattern.Match(lbl);
                var sz = m.Success ? m.Groups[1].Value.Trim() : lbl;
                if (seen.Add(sz)) sizes.Add(sz);
            }

            if (sizes.Count > 0)
            {
                dto.AllPackagingSizes = sizes;
                dto.PackagingSize   ??= sizes[0];
            }
            break; // Only one packaging attribute expected
        }
    }

    // ── Description ───────────────────────────────────────────────────

    private static void EnrichDescription(LiquiMolyProductDto dto, HtmlDocument doc)
    {
        var descNode =
            // Magento 2 custom tab IDs used by Liqui-Moly
            doc.DocumentNode.SelectSingleNode("//div[@id='tab-detail-description']") ??
            // Magento 2 standard selectors
            doc.DocumentNode.SelectSingleNode("//div[contains(@class,'product-info-description')]") ??
            doc.DocumentNode.SelectSingleNode("//div[contains(@class,'product.description')]")     ??
            doc.DocumentNode.SelectSingleNode("//div[@itemprop='description']");

        if (descNode == null) return;

        var text = HtmlEntity.DeEntitize(descNode.InnerText).Trim();
        if (!string.IsNullOrWhiteSpace(text))
            dto.Description = text;
    }

    // ── Approvals & Specifications ────────────────────────────────────

    private static void EnrichApprovalsAndSpecs(LiquiMolyProductDto dto, HtmlDocument doc)
    {
        var specs     = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var approvals = new List<string>();

        // Magento 2 custom: combined approvals + specs tab (Liqui-Moly)
        var combinedNode = doc.DocumentNode.SelectSingleNode(
            "//div[@id='tab-detail-approvalsandspecifications']");

        if (combinedNode != null)
        {
            // Try structured dl/dt/dd pattern
            var dtNodes = combinedNode.SelectNodes(".//dt");
            var ddNodes = combinedNode.SelectNodes(".//dd");
            if (dtNodes != null && ddNodes != null)
            {
                int len = Math.Min(dtNodes.Count, ddNodes.Count);
                for (int i = 0; i < len; i++)
                {
                    var key = HtmlEntity.DeEntitize(dtNodes[i].InnerText).Trim();
                    var val = HtmlEntity.DeEntitize(ddNodes[i].InnerText).Trim();
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                        specs[key] = val;
                }
            }

            // Try table row (th/td) pattern
            var rows = combinedNode.SelectNodes(".//tr");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var cells = row.SelectNodes(".//th|.//td");
                    if (cells is { Count: >= 2 })
                    {
                        var key = HtmlEntity.DeEntitize(cells[0].InnerText).Trim();
                        var val = HtmlEntity.DeEntitize(cells[1].InnerText).Trim();
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                            specs[key] = val;
                    }
                }
            }

            // No structured data — treat the whole block as a comma/semicolon list of approvals
            if (specs.Count == 0)
            {
                var raw = HtmlEntity.DeEntitize(combinedNode.InnerText).Trim();
                approvals.AddRange(
                    raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0 && s.Length < 200));
            }
        }
        else
        {
            // Generic fallback selectors
            var specsNode = doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class,'product-specifications')]" +
                "|//section[contains(@class,'specifications')]"    +
                "|//div[contains(@class,'technical-data')]");

            if (specsNode != null)
            {
                var dt = specsNode.SelectNodes(".//dt");
                var dd = specsNode.SelectNodes(".//dd");
                if (dt != null && dd != null)
                {
                    int len = Math.Min(dt.Count, dd.Count);
                    for (int i = 0; i < len; i++)
                    {
                        var key = HtmlEntity.DeEntitize(dt[i].InnerText).Trim();
                        var val = HtmlEntity.DeEntitize(dd[i].InnerText).Trim();
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                            specs[key] = val;
                    }
                }
            }

            var approvalsNode = doc.DocumentNode.SelectSingleNode(
                "//div[contains(@class,'product-approvals')]" +
                "|//section[contains(@class,'approvals')]");

            if (approvalsNode != null)
            {
                var liNodes = approvalsNode.SelectNodes(".//li");
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
                    var raw = HtmlEntity.DeEntitize(approvalsNode.InnerText).Trim();
                    approvals.AddRange(
                        raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim())
                           .Where(s => s.Length > 0 && s.Length < 200));
                }
            }
        }

        if (specs.Count > 0)     dto.Specifications = specs;
        if (approvals.Count > 0) dto.Approvals       = approvals;
    }

    // ── Downloads (PDFs) ──────────────────────────────────────────────

    private void EnrichDownloads(LiquiMolyProductDto dto, HtmlDocument doc, string rawHtml)
    {
        // Try direct anchor links first (works if PDFs are in static HTML)
        var pdfLinks = doc.DocumentNode.SelectNodes(
            "//a[contains(@href,'.pdf')]" +
            "|//a[contains(@href,'pim.liqui-moly.com')]");

        if (pdfLinks != null)
        {
            foreach (var link in pdfLinks)
            {
                var href = link.GetAttributeValue("href", null);
                if (string.IsNullOrWhiteSpace(href)) continue;

                var url = BuildAbsoluteOrNull(href);
                if (url == null) continue;

                var label = (link.GetAttributeValue("title", null) ?? link.InnerText ?? "")
                    .Trim().ToLowerInvariant();

                AssignPdfUrl(dto, url, label, href.ToLowerInvariant());
            }
        }

        // Fallback: scan raw HTML for pim.liqui-moly.com PDF URLs embedded in JS strings
        if (dto.ProductInfoPdfUrl == null || dto.SafetyDataSheetPdfUrl == null)
        {
            foreach (Match m in _pimPdfPattern.Matches(rawHtml))
            {
                var url   = m.Value;
                var lower = url.ToLowerInvariant();
                AssignPdfUrl(dto, url, lower, lower);
            }
        }
    }

    private static void AssignPdfUrl(
        LiquiMolyProductDto dto, string url, string label, string href)
    {
        bool isProductInfo =
            label.Contains("product information") || label.Contains("pi en")       ||
            label.Contains("prod info")           || label.Contains("technical data") ||
            (href.Contains("/pi") && (href.Contains("/en") || href.Contains("_en"))) ||
            (href.Contains("_pi") && href.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

        bool isSds =
            label.Contains("safety data") || label.Contains("sds") || label.Contains("msds") ||
            href.Contains("/sds")  || href.Contains("_sds")  ||
            href.Contains("/msds") || href.Contains("_msds");

        if (dto.ProductInfoPdfUrl    == null && isProductInfo) dto.ProductInfoPdfUrl    = url;
        if (dto.SafetyDataSheetPdfUrl == null && isSds)         dto.SafetyDataSheetPdfUrl = url;
    }

    // ------------------------------------------------------------------
    // JSON-LD EXTRACTION (Magento 2)
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
                var json = script.InnerText.Trim();
                using var root = JsonDocument.Parse(json);
                var el         = root.RootElement;

                if (el.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in el.EnumerateArray())
                        TryAddJsonLdProduct(item, category, pageUrl, results);
                }
                else
                {
                    TryAddJsonLdProduct(el, category, pageUrl, results);

                    if (el.TryGetProperty("@graph", out var graph) &&
                        graph.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in graph.EnumerateArray())
                            TryAddJsonLdProduct(item, category, pageUrl, results);
                    }
                }
            }
            catch { /* malformed JSON – skip */ }
        }

        return results;
    }

    private void TryAddJsonLdProduct(
        JsonElement el, string category, string pageUrl,
        List<LiquiMolyProductDto> results)
    {
        if (!el.TryGetProperty("@type", out var typeEl)) return;
        if (!typeEl.GetString()?.Equals("Product", StringComparison.OrdinalIgnoreCase) ?? true) return;

        var name = el.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(name)) return;

        var description = el.TryGetProperty("description", out var descEl)
            ? HtmlEntity.DeEntitize(descEl.GetString()?.Trim() ?? "")
            : null;

        var imageUrl = el.TryGetProperty("image", out var imgEl)
            ? (imgEl.ValueKind == JsonValueKind.String
                ? BuildAbsoluteOrNull(imgEl.GetString())
                : imgEl.TryGetProperty("url", out var imgUrlEl)
                    ? BuildAbsoluteOrNull(imgUrlEl.GetString())
                    : null)
            : null;

        var productUrl = el.TryGetProperty("url", out var urlEl)
            ? urlEl.GetString() ?? pageUrl
            : pageUrl;

        // Magento 2: the offers[] array has one entry per packaging variant;
        // each offer.sku is a 4-digit variant article number (= CacheProducts ItemCode)
        if (el.TryGetProperty("offers", out var offersEl) &&
            offersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var offer in offersEl.EnumerateArray())
            {
                var offerSku = offer.TryGetProperty("sku", out var oSkuEl)
                    ? oSkuEl.GetString()?.Trim()
                    : null;

                if (string.IsNullOrWhiteSpace(offerSku) || !_variantSkuPattern.IsMatch(offerSku))
                    continue;

                var offerUrl = offer.TryGetProperty("url", out var oUrlEl)
                    ? oUrlEl.GetString() ?? productUrl
                    : productUrl;

                results.Add(new LiquiMolyProductDto
                {
                    ArticleNumber = offerSku,
                    Name          = HtmlEntity.DeEntitize(name),
                    Category      = category,
                    Description   = description,
                    SpecGrade     = ExtractSpecGrade(name),
                    ImageUrl      = imageUrl,
                    ProductUrl    = offerUrl,
                });
            }
            return; // Offers processed — do not also add the product-level P-code
        }

        // Fallback: no offers array — only add if the top-level sku looks like a variant code
        var topSku =
            (el.TryGetProperty("sku",       out var skuEl) ? skuEl.GetString()?.Trim() : null) ??
            (el.TryGetProperty("productID", out var pidEl) ? pidEl.GetString()?.Trim() : null) ??
            (el.TryGetProperty("mpn",       out var mpnEl) ? mpnEl.GetString()?.Trim() : null);

        if (!string.IsNullOrWhiteSpace(topSku) && _variantSkuPattern.IsMatch(topSku))
        {
            results.Add(new LiquiMolyProductDto
            {
                ArticleNumber = topSku,
                Name          = HtmlEntity.DeEntitize(name),
                Category      = category,
                Description   = description,
                SpecGrade     = ExtractSpecGrade(name),
                ImageUrl      = imageUrl,
                ProductUrl    = productUrl,
            });
        }
    }

    // ------------------------------------------------------------------
    // HTML EXTRACTION (Magento 2 product cards — fallback)
    // ------------------------------------------------------------------

    private List<LiquiMolyProductDto> ExtractFromHtml(
        HtmlDocument doc, string category, string pageUrl)
    {
        var results = new List<LiquiMolyProductDto>();

        // Magento 2 standard product listing items
        var cards = doc.DocumentNode.SelectNodes(
            "//li[contains(@class,'product-item')]" +
            "|//div[contains(@class,'product-item')]");

        if (cards == null) return results;

        foreach (var card in cards)
        {
            var dto = TryExtractFromCard(card, category, pageUrl);
            if (dto != null) results.Add(dto);
        }

        return results;
    }

    private LiquiMolyProductDto? TryExtractFromCard(
        HtmlNode card, string category, string pageUrl)
    {
        // Article number must come from a data attribute — can't safely derive from text
        var rawSku =
            card.GetAttributeValue("data-article-number", null) ??
            card.GetAttributeValue("data-product-number", null) ??
            card.GetAttributeValue("data-sku",            null) ??
            card.SelectSingleNode(".//*[@data-article-number]")
                ?.GetAttributeValue("data-article-number", null);

        var articleNumber = rawSku?.Trim();
        if (string.IsNullOrWhiteSpace(articleNumber) || !_variantSkuPattern.IsMatch(articleNumber))
            return null;

        // Magento 2 product card name selectors
        var nameNode =
            card.SelectSingleNode(".//*[contains(@class,'product-item-name')]//a") ??
            card.SelectSingleNode(".//*[contains(@class,'product-item-link')]")    ??
            card.SelectSingleNode(".//h2|.//h3|.//h4");

        var name = nameNode?.InnerText?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;

        var linkHref   = nameNode?.GetAttributeValue("href", null)
                      ?? card.SelectSingleNode(".//a[@href]")?.GetAttributeValue("href", null);
        var productUrl = BuildAbsoluteOrNull(linkHref);

        var imgNode  = card.SelectSingleNode(".//*[contains(@class,'product-image-photo')]")
                    ?? card.SelectSingleNode(".//img");
        var imageUrl = BuildAbsoluteOrNull(
            imgNode?.GetAttributeValue("src",      null) ??
            imgNode?.GetAttributeValue("data-src", null));

        return new LiquiMolyProductDto
        {
            ArticleNumber = articleNumber,
            Name          = HtmlEntity.DeEntitize(name),
            Category      = category,
            SpecGrade     = ExtractSpecGrade(name),
            ImageUrl      = imageUrl,
            ProductUrl    = productUrl ?? pageUrl,
        };
    }

    // ------------------------------------------------------------------
    // PAGINATION
    // ------------------------------------------------------------------

    private static string? FindNextPageUrl(HtmlDocument doc)
    {
        // Magento 2: pagination uses ?p=N with a visible "Next" link (no <link rel="next">)
        var nextLink =
            doc.DocumentNode.SelectSingleNode("//a[normalize-space(text())='Next']")             ??
            doc.DocumentNode.SelectSingleNode("//a[@title='Next' or @aria-label='Next']")        ??
            doc.DocumentNode.SelectSingleNode("//li[contains(@class,'pages-item-next')]//a");

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
            req.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            req.Headers.TryAddWithoutValidation("Referer", _settings.BaseUrl);

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

    private static string? ExtractSpecGrade(string text)
    {
        var m = _specGradePattern.Match(text);
        return m.Success ? m.Value.Trim() : null;
    }

    private static Task DelayAsync(int ms, CancellationToken ct)
        => ms > 0 ? Task.Delay(ms, ct) : Task.CompletedTask;
}
