using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MolasLubes.Infrastructure.Integrations.LiquiMoly;

public class LiquiMolyProductScraperService
{
    private readonly HttpClient _http;
    private readonly LiquiMolyScraperSettings _settings;
    private readonly ILogger<LiquiMolyProductScraperService> _logger;

    // Shared across instances; rebuilt when older than 23 h so daily jobs
    // always pick up newly-added Liqui-Moly products.
    private static Dictionary<string, string>? _cachedIndex;
    private static DateTimeOffset _cacheBuiltAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(23);

    // Valid numeric SKU: 3–6 digits (matches both the DB filter and the URL SKU extraction range)
    private static readonly Regex ValidSkuPattern =
        new(@"^\d{3,6}$", RegexOptions.Compiled);

    private static readonly Regex SizePattern =
        new(@"\b(\d+(?:[.,]\d+)?\s*(?:ml|l|kg|g))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SpecGradePattern =
        new(@"\b\d{1,2}W[-–]\d{2,3}\b|\bSAE\s+\d+\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LiquiMolyProductScraperService(
        HttpClient httpClient,
        IOptions<LiquiMolyScraperSettings> settings,
        ILogger<LiquiMolyProductScraperService> logger)
    {
        _http = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    // ======================================================
    // MAIN ENTRY
    // ======================================================

    public async Task<List<LiquiMolyProductDto>> ScrapeByArticleNumbersAsync(
        IEnumerable<string> articleNumbers,
        CancellationToken ct = default)
    {
        var raw = articleNumbers?.ToList() ?? new List<string>();

        _logger.LogInformation("[LiquiMoly] Raw SKU input count: {Count}", raw.Count);

        if (raw.Count > 0)
            _logger.LogInformation("[LiquiMoly] First 10 raw SKUs: {Skus}",
                string.Join(", ", raw.Take(10)));

        // Filter to valid numeric SKUs only
        var targets = new HashSet<string>(
            raw.Where(x => !string.IsNullOrWhiteSpace(x))
               .Select(x => x.Trim())
               .Where(x => ValidSkuPattern.IsMatch(x)),
            StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("[LiquiMoly] Valid numeric SKUs after filtering: {Count}", targets.Count);

        if (targets.Count > 0)
            _logger.LogInformation("[LiquiMoly] First 10 cleaned SKUs: {Skus}",
                string.Join(", ", targets.Take(10)));

        var index = await GetOrBuildIndexAsync(ct);

        _logger.LogInformation("[LiquiMoly] Product index size: {Count}", index.Count);

        var results = new ConcurrentBag<LiquiMolyProductDto>();
        int found = 0, missing = 0;

        await ForEachBoundedAsync(targets, _settings.MaxParallelRequests, async sku =>
        {
            if (!index.TryGetValue(sku, out var url))
            {
                Interlocked.Increment(ref missing);
                _logger.LogWarning("[LiquiMoly] SKU {Sku} not found in index", sku);
                return;
            }

            Interlocked.Increment(ref found);
            _logger.LogDebug("[LiquiMoly] SKU {Sku} resolved → {Url}", sku, url);

            var dto = await ScrapeProductPageForSkuAsync(sku, url, ct);
            if (dto != null)
                results.Add(dto);

        }, ct);

        _logger.LogInformation(
            "[LiquiMoly] Scrape summary | Requested={Requested} | FoundInIndex={Found} | Missing={Missing} | Scraped={Scraped}",
            targets.Count, found, missing, results.Count);

        return results.ToList();
    }

    // ======================================================
    // INDEX — GET CACHED OR REBUILD
    // ======================================================

    private async Task<Dictionary<string, string>> GetOrBuildIndexAsync(CancellationToken ct)
    {
        if (_cachedIndex != null
            && _cachedIndex.Count > 0
            && DateTimeOffset.UtcNow - _cacheBuiltAt < CacheLifetime)
        {
            _logger.LogInformation("[LiquiMoly] Using cached index | {Count} SKUs", _cachedIndex.Count);
            return _cachedIndex;
        }

        _logger.LogInformation("[LiquiMoly] Building product index...");
        return await BuildProductIndexAsync(ct);
    }

    private async Task<Dictionary<string, string>> BuildProductIndexAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var sitemapUrl = $"{_settings.BaseUrl}/sitemap.xml";

        _logger.LogInformation("[LiquiMoly] Loading sitemap index {Url}", sitemapUrl);

        string xml;
        try
        {
            xml = await _http.GetStringAsync(sitemapUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LiquiMoly] Failed loading sitemap");
            return map;
        }

        if (string.IsNullOrWhiteSpace(xml))
        {
            _logger.LogWarning("[LiquiMoly] Sitemap XML empty");
            return map;
        }

        var locMatches = Regex.Matches(xml, @"<loc>(.*?)</loc>", RegexOptions.IgnoreCase);
        var locUrls = locMatches.Cast<Match>().Select(m => m.Groups[1].Value).ToList();

        _logger.LogInformation("[LiquiMoly] Sitemap LOC entries found: {Count}", locUrls.Count);

        var sitemapUrls = locUrls.Where(x => x.Contains("sitemap")).ToList();

        if (sitemapUrls.Count > 0)
        {
            _logger.LogInformation("[LiquiMoly] Found {Count} child sitemaps", sitemapUrls.Count);

            foreach (var sm in sitemapUrls)
            {
                try
                {
                    var smXml = await _http.GetStringAsync(sm, ct);

                    var urls = Regex.Matches(
                            smXml,
                            @"https://(?:www\.)?liqui-moly\.com/en/[^<]+\.html",
                            RegexOptions.IgnoreCase)
                        .Cast<Match>()
                        .Select(m => m.Value);

                    foreach (var url in urls)
                    {
                        var sku = ExtractSku(url);
                        if (sku != null && !map.ContainsKey(sku))
                            map[sku] = url;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[LiquiMoly] Failed reading child sitemap {Url}", sm);
                }
            }
        }
        else
        {
            _logger.LogWarning("[LiquiMoly] No child sitemaps detected → scanning direct LOC entries");

            foreach (var url in locUrls.Where(u => u.Contains("/en/") && u.EndsWith(".html")))
            {
                var sku = ExtractSku(url);
                if (sku != null && !map.ContainsKey(sku))
                    map[sku] = url;
            }
        }

        _logger.LogInformation("[LiquiMoly] Index complete | SKUs={Count}", map.Count);

        if (map.Count > 0)
        {
            _logger.LogInformation("[LiquiMoly] Sample SKUs: {Skus}",
                string.Join(", ", map.Keys.Take(10)));

            _cachedIndex = map;
            _cacheBuiltAt = DateTimeOffset.UtcNow;
        }
        else
        {
            _logger.LogError("[LiquiMoly] Product index EMPTY → sitemap structure likely changed");
        }

        return map;
    }

    // ======================================================
    // SKU EXTRACTION FROM URL
    //
    // Two patterns tried in order:
    //   1. Standard Liqui-Moly: …-p{sku}.html or …/p{sku}.html (leading zeros stripped)
    //   2. Trailing-number fallback: …-{3–6 digits}.html
    //
    // Using a word-boundary before 'p' avoids false matches when the
    // product slug itself contains 'p' followed by digits (e.g. "p5w30-…-p20001.html").
    // ======================================================

    private static string? ExtractSku(string url)
    {
        // Primary: explicit product-number segment "-p{digits}.html" or "/p{digits}.html"
        var m = Regex.Match(url, @"[-/]p0*(\d{3,6})\.html", RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Groups[1].Value;

        // Fallback: URL ends with "-{3–6 digits}.html" (no leading "p")
        var m2 = Regex.Match(url, @"-(\d{3,6})\.html$", RegexOptions.IgnoreCase);
        if (m2.Success)
            return m2.Groups[1].Value;

        return null;
    }

    // ======================================================
    // SCRAPE PRODUCT PAGE
    // ======================================================

    private async Task<LiquiMolyProductDto?> ScrapeProductPageForSkuAsync(
        string requestedSku,
        string productUrl,
        CancellationToken ct)
    {
        var html = await FetchHtmlAsync(productUrl, ct);
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // JSON-LD structured data — richer and more reliable than HTML scraping
        var jsonLd = TryParseJsonLd(doc);

        var name  = ExtractName(doc, jsonLd);
        var desc  = ExtractDescription(doc, jsonLd);
        var images = ExtractAllImages(doc, jsonLd);
        var (category, subCategory) = ExtractCategories(doc);
        var specs    = ExtractSpecifications(doc);
        var approvals = ExtractApprovals(doc, specs);
        var sizes    = ExtractAllPackagingSizes(doc, name, desc);
        var (pdfUrl, sdsUrl) = ExtractDownloads(doc);

        // SpecGrade: product name → description → Specifications dict
        var specGrade = ExtractSpecGrade(name ?? "")
            ?? ExtractSpecGrade(desc ?? "")
            ?? (specs.TryGetValue("Viscosity class", out var vc) ? ExtractSpecGrade(vc) : null)
            ?? (specs.TryGetValue("Viscosity", out var vs) ? ExtractSpecGrade(vs) : null);

        return new LiquiMolyProductDto
        {
            ArticleNumber        = requestedSku,
            Name                 = name ?? requestedSku,
            Description          = desc,
            ProductUrl           = productUrl,
            ImageUrl             = images.FirstOrDefault(),
            AllImageUrls         = images,
            PackagingSize        = sizes.FirstOrDefault(),
            AllPackagingSizes    = sizes,
            Category             = category,
            SubCategory          = subCategory,
            Specifications       = specs,
            Approvals            = approvals,
            SpecGrade            = specGrade,
            ProductInfoPdfUrl    = pdfUrl,
            SafetyDataSheetPdfUrl = sdsUrl,
        };
    }

    // ======================================================
    // JSON-LD STRUCTURED DATA
    // ======================================================

    /// <summary>
    /// Parses the first <c>&lt;script type="application/ld+json"&gt;</c> block whose
    /// <c>@type</c> is "Product" and returns its properties as cloned <see cref="JsonElement"/>
    /// values that outlive the parsed document.
    /// Returns <c>null</c> if no Product JSON-LD block is found.
    /// </summary>
    private static Dictionary<string, JsonElement>? TryParseJsonLd(HtmlDocument doc)
    {
        var scripts = doc.DocumentNode
            .SelectNodes("//script[@type='application/ld+json']");

        if (scripts == null) return null;

        foreach (var script in scripts)
        {
            try
            {
                using var jdoc = JsonDocument.Parse(script.InnerText);
                var root = jdoc.RootElement;

                if (!root.TryGetProperty("@type", out var typeEl)) continue;
                if (!typeEl.GetString()!.Contains("Product", StringComparison.OrdinalIgnoreCase)) continue;

                // Clone all properties so they survive jdoc disposal
                return root.EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.Clone());
            }
            catch
            {
                // malformed JSON-LD — skip and try next script block
            }
        }

        return null;
    }

    // ======================================================
    // EXTRACTION HELPERS
    // ======================================================

    private static string? ExtractName(HtmlDocument doc, Dictionary<string, JsonElement>? jsonLd)
    {
        if (jsonLd != null && jsonLd.TryGetValue("name", out var n))
            return HtmlEntity.DeEntitize(n.GetString()?.Trim());

        var node = doc.DocumentNode.SelectSingleNode("//h1[@itemprop='name']")
                ?? doc.DocumentNode.SelectSingleNode("//h1[contains(@class,'product')]")
                ?? doc.DocumentNode.SelectSingleNode("//h1");

        return node == null ? null : HtmlEntity.DeEntitize(node.InnerText.Trim());
    }

    private static string? ExtractDescription(HtmlDocument doc, Dictionary<string, JsonElement>? jsonLd)
    {
        if (jsonLd != null && jsonLd.TryGetValue("description", out var d))
            return HtmlEntity.DeEntitize(d.GetString()?.Trim());

        var node = doc.DocumentNode.SelectSingleNode("//div[@itemprop='description']")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'description')]");

        return node == null ? null : HtmlEntity.DeEntitize(node.InnerText.Trim());
    }

    private List<string> ExtractAllImages(HtmlDocument doc, Dictionary<string, JsonElement>? jsonLd)
    {
        var urls = new List<string>();

        // JSON-LD: "image" can be a string or array
        if (jsonLd != null && jsonLd.TryGetValue("image", out var imgEl))
        {
            if (imgEl.ValueKind == JsonValueKind.String)
            {
                var abs = BuildAbsoluteOrNull(imgEl.GetString());
                if (abs != null) urls.Add(abs);
            }
            else if (imgEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in imgEl.EnumerateArray())
                {
                    var abs = BuildAbsoluteOrNull(el.GetString());
                    if (abs != null && !urls.Contains(abs)) urls.Add(abs);
                }
            }
        }

        // itemprop="image" elements
        foreach (var node in doc.DocumentNode.SelectNodes("//img[@itemprop='image']")
                              ?? Enumerable.Empty<HtmlNode>())
        {
            var src = node.GetAttributeValue("src", null)
                   ?? node.GetAttributeValue("data-src", null);
            var abs = BuildAbsoluteOrNull(src);
            if (abs != null && !urls.Contains(abs)) urls.Add(abs);
        }

        // Fallback: any img with "product" in its class
        if (urls.Count == 0)
        {
            foreach (var node in doc.DocumentNode.SelectNodes("//img[contains(@class,'product')]")
                                  ?? Enumerable.Empty<HtmlNode>())
            {
                var src = node.GetAttributeValue("src", null)
                       ?? node.GetAttributeValue("data-src", null);
                var abs = BuildAbsoluteOrNull(src);
                if (abs != null && !urls.Contains(abs)) urls.Add(abs);
            }
        }

        return urls;
    }

    /// <summary>
    /// Extracts category and sub-category from the breadcrumb navigation.
    /// Breadcrumb links are expected to be: Home &gt; Category &gt; [SubCategory] &gt; Product
    /// The first link after "Home" is the category; the second is the sub-category.
    /// </summary>
    private static (string? category, string? subCategory) ExtractCategories(HtmlDocument doc)
    {
        var selectors = new[]
        {
            "//ol[contains(@class,'breadcrumb')]//li/a",
            "//ul[contains(@class,'breadcrumb')]//li/a",
            "//nav[@aria-label='breadcrumb']//a",
            "//div[contains(@class,'breadcrumb')]//a",
        };

        foreach (var selector in selectors)
        {
            var nodes = doc.DocumentNode.SelectNodes(selector)?.ToList();
            if (nodes == null || nodes.Count == 0) continue;

            var crumbs = nodes
                .Select(n => HtmlEntity.DeEntitize(n.InnerText.Trim()))
                .Where(t => !string.IsNullOrWhiteSpace(t)
                         && !t.Equals("Home", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (crumbs.Count == 0) continue;

            return (
                crumbs[0],
                crumbs.Count >= 2 ? crumbs[1] : null
            );
        }

        return (null, null);
    }

    /// <summary>
    /// Extracts the technical specifications table into a key/value dictionary.
    /// Tries &lt;table&gt; rows first (th/td pairs), then &lt;dl&gt; dt/dd pairs.
    /// </summary>
    private static Dictionary<string, string> ExtractSpecifications(HtmlDocument doc)
    {
        var specs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Table-based specs: <tr><th>Key</th><td>Value</td></tr>
        var tableSelectors = new[]
        {
            "//table[contains(@class,'spec')]//tr",
            "//table[contains(@class,'technical')]//tr",
            "//div[contains(@class,'spec')]//table//tr",
            "//section[contains(@class,'spec')]//tr",
        };

        foreach (var selector in tableSelectors)
        {
            var rows = doc.DocumentNode.SelectNodes(selector);
            if (rows == null) continue;

            foreach (var row in rows)
            {
                var cells = row.SelectNodes("td|th")?.ToList();
                if (cells == null || cells.Count < 2) continue;

                var key = HtmlEntity.DeEntitize(cells[0].InnerText.Trim().TrimEnd(':'));
                var val = HtmlEntity.DeEntitize(cells[1].InnerText.Trim());

                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                    specs.TryAdd(key, val);
            }

            if (specs.Count > 0) break;
        }

        // Definition-list fallback: <dl><dt>Key</dt><dd>Value</dd></dl>
        if (specs.Count == 0)
        {
            var dlSelectors = new[]
            {
                "//dl[contains(@class,'spec')]",
                "//dl[contains(@class,'technical')]",
                "//dl",
            };

            foreach (var sel in dlSelectors)
            {
                var dl = doc.DocumentNode.SelectSingleNode(sel);
                if (dl == null) continue;

                var dts = dl.SelectNodes("dt")?.ToList() ?? new List<HtmlNode>();
                var dds = dl.SelectNodes("dd")?.ToList() ?? new List<HtmlNode>();

                for (int i = 0; i < Math.Min(dts.Count, dds.Count); i++)
                {
                    var key = HtmlEntity.DeEntitize(dts[i].InnerText.Trim().TrimEnd(':'));
                    var val = HtmlEntity.DeEntitize(dds[i].InnerText.Trim());

                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(val))
                        specs.TryAdd(key, val);
                }

                if (specs.Count > 0) break;
            }
        }

        return specs;
    }

    /// <summary>
    /// Extracts OEM / industry approvals.
    /// First checks the Specifications dict for approval keys; then falls back to
    /// dedicated approval sections in the HTML.
    /// </summary>
    private static List<string> ExtractApprovals(HtmlDocument doc, Dictionary<string, string> specs)
    {
        var approvals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check specs dictionary for common approval keys
        var approvalKeys = new[] { "Approvals", "Approval", "Standards", "OEM Approvals", "Meets" };
        foreach (var key in approvalKeys)
        {
            if (!specs.TryGetValue(key, out var val) || string.IsNullOrWhiteSpace(val)) continue;

            foreach (var part in val.Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    approvals.Add(trimmed);
            }
        }

        // Dedicated approvals list in HTML
        if (approvals.Count == 0)
        {
            var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'approval')]//li")
                     ?? doc.DocumentNode.SelectNodes("//ul[contains(@class,'approval')]//li")
                     ?? doc.DocumentNode.SelectNodes("//section[contains(@class,'approval')]//li");

            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var text = HtmlEntity.DeEntitize(node.InnerText.Trim());
                    if (!string.IsNullOrWhiteSpace(text))
                        approvals.Add(text);
                }
            }
        }

        return approvals.ToList();
    }

    /// <summary>
    /// Extracts all packaging/volume sizes.
    /// Searches the product name, description, and any variant/packaging sections on the page.
    /// </summary>
    private static List<string> ExtractAllPackagingSizes(HtmlDocument doc, string? name, string? desc)
    {
        var sizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(name))
            foreach (Match m in SizePattern.Matches(name))
                sizes.Add(m.Groups[1].Value);

        if (!string.IsNullOrWhiteSpace(desc))
            foreach (Match m in SizePattern.Matches(desc))
                sizes.Add(m.Groups[1].Value);

        // Product page variant buttons / packaging list
        var packNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'pack')]//li")
                     ?? doc.DocumentNode.SelectNodes("//ul[contains(@class,'variant')]//li")
                     ?? doc.DocumentNode.SelectNodes("//div[contains(@class,'variant')]//button");

        if (packNodes != null)
        {
            foreach (var node in packNodes)
            {
                var text = HtmlEntity.DeEntitize(node.InnerText.Trim());
                foreach (Match m in SizePattern.Matches(text))
                    sizes.Add(m.Groups[1].Value);
            }
        }

        return sizes.ToList();
    }

    /// <summary>
    /// Extracts PDF download links from the page.
    /// Safety Data Sheet (SDS/MSDS) links are identified by URL/text keywords;
    /// the first non-SDS PDF is treated as the product information sheet.
    /// </summary>
    private (string? pdfUrl, string? sdsUrl) ExtractDownloads(HtmlDocument doc)
    {
        string? pdfUrl = null;
        string? sdsUrl = null;

        var links = doc.DocumentNode.SelectNodes("//a[contains(@href,'.pdf')]");
        if (links == null) return (null, null);

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", null);
            if (string.IsNullOrWhiteSpace(href)) continue;

            var abs = BuildAbsoluteOrNull(href);
            var text = link.InnerText.ToLowerInvariant();
            var hrefLower = href.ToLowerInvariant();

            bool isSds = text.Contains("safety") || text.Contains("sds") || text.Contains("msds")
                      || hrefLower.Contains("safety") || hrefLower.Contains("sds");

            if (isSds && sdsUrl == null)
                sdsUrl = abs;
            else if (!isSds && pdfUrl == null)
                pdfUrl = abs;

            if (pdfUrl != null && sdsUrl != null) break;
        }

        return (pdfUrl, sdsUrl);
    }

    private static string? ExtractSpecGrade(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = SpecGradePattern.Match(text);
        return m.Success ? m.Value : null;
    }

    // ======================================================
    // HTTP  —  with simple exponential-backoff retry
    // ======================================================

    private async Task<string> FetchHtmlAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        const int maxRetries = 2;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var resp = await _http.GetAsync(url, ct);

                if (resp.IsSuccessStatusCode)
                    return await resp.Content.ReadAsStringAsync(ct);

                _logger.LogWarning(
                    "[LiquiMoly] HTTP {Status} for {Url} (attempt {Attempt}/{Max})",
                    (int)resp.StatusCode, url, attempt + 1, maxRetries + 1);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < maxRetries)
            {
                _logger.LogWarning(ex,
                    "[LiquiMoly] Fetch error for {Url} (attempt {Attempt}/{Max})",
                    url, attempt + 1, maxRetries + 1);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                continue;
            }
            catch
            {
                break;
            }
        }

        return string.Empty;
    }

    // ======================================================
    // HELPERS
    // ======================================================

    private string BuildAbsolute(string path)
    {
        if (path.StartsWith("http"))
            return path;

        return _settings.BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private string? BuildAbsoluteOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return BuildAbsolute(path);
    }

    private static async Task ForEachBoundedAsync<T>(
        IEnumerable<T> items,
        int maxParallel,
        Func<T, Task> action,
        CancellationToken ct)
    {
        using var sem = new SemaphoreSlim(maxParallel);

        var tasks = items.Select(async item =>
        {
            await sem.WaitAsync(ct);
            try { await action(item); }
            finally { sem.Release(); }
        });

        await Task.WhenAll(tasks);
    }
}
