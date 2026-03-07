using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
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

    // Valid numeric SKU: 3–6 digits
    private static readonly Regex ValidSkuPattern =
        new(@"^\d{3,6}$", RegexOptions.Compiled);

    private static readonly Regex SizePattern =
        new(@"\b(\d+(?:[.,]\d+)?\s*(?:ml|l|kg|g))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SpecGradePattern =
        new(@"\b\d{1,2}W[-–]\d{2,3}\b|\bSAE\s+\d+\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Safety limit on paginated category pages to prevent runaway fetching
    private const int MaxCategoryPages = 50;

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

        _logger.LogInformation("[LiquiMoly] Building product index from category pages...");
        return await BuildProductIndexAsync(ct);
    }

    /// <summary>
    /// Builds the SKU → product URL index.
    ///
    /// The Liqui-Moly site now embeds SKUs directly in the URL fragment of every
    /// variant link on category listing pages:
    ///   <c>&lt;a class="product-variation ..." href="...product-url.html#1024"&gt;</c>
    ///
    /// This means the entire index can be built in a single category-page crawl —
    /// no separate product-page visits are needed for variant discovery.
    ///
    /// Process:
    ///   1. Fetch each category from <see cref="LiquiMolyScraperSettings.CategoryPaths"/>
    ///      (all pagination pages).
    ///   2. Parse every <c>a.product-variation</c> link; extract SKU from the URL
    ///      fragment (e.g. <c>#1024</c>) and map it directly to the href.
    ///   3. For any product URL whose fragment is absent or non-numeric (rare
    ///      single-variant products), fetch the product page and extract the SKU
    ///      from <c>&lt;span itemprop="sku"&gt;</c>.
    /// </summary>
    private async Task<Dictionary<string, string>> BuildProductIndexAsync(CancellationToken ct)
    {
        var map = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var needsProductFetch = new ConcurrentBag<string>();

        // Phase 1 — crawl category pages; extract SKU→URL directly from href fragments
        foreach (var (path, categoryName) in _settings.CategoryPaths)
        {
            if (ct.IsCancellationRequested) break;

            await CollectSkuUrlsFromCategoryAsync(
                _settings.BaseUrl.TrimEnd('/') + path, categoryName, map, needsProductFetch, ct);

            await Task.Delay(_settings.DelayBetweenCategoriesMs, ct);
        }

        _logger.LogInformation(
            "[LiquiMoly] Category crawl complete | Direct SKU mappings={Direct} | Need product page fetch={Fetch}",
            map.Count, needsProductFetch.Count);

        if (map.Count == 0 && needsProductFetch.IsEmpty)
        {
            _logger.LogError(
                "[LiquiMoly] No product URLs found — category pages may be blocked or have changed structure");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // Phase 2 (rare) — for products whose URL had no numeric SKU fragment,
        // fetch the product page and read <span itemprop="sku">
        if (!needsProductFetch.IsEmpty)
        {
            await ForEachBoundedAsync(needsProductFetch, _settings.MaxParallelRequests, async productUrl =>
            {
                try
                {
                    var html = await FetchHtmlAsync(productUrl, ct);
                    if (string.IsNullOrWhiteSpace(html)) return;

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    var pageSku = ExtractSkuFromPage(doc);
                    if (!string.IsNullOrWhiteSpace(pageSku))
                        map.TryAdd(pageSku, productUrl + "#" + pageSku);

                    await Task.Delay(_settings.DelayBetweenRequestsMs, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[LiquiMoly] Failed extracting SKU from {Url}", productUrl);
                }
            }, ct);
        }

        var result = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("[LiquiMoly] Index complete | SKUs={Count}", result.Count);

        if (result.Count > 0)
        {
            _logger.LogInformation("[LiquiMoly] Sample SKUs: {Skus}",
                string.Join(", ", result.Keys.Take(10)));

            _cachedIndex = result;
            _cacheBuiltAt = DateTimeOffset.UtcNow;
        }
        else
        {
            _logger.LogError(
                "[LiquiMoly] Product index EMPTY — check category page structure or HTML class names");
        }

        return result;
    }

    // ======================================================
    // CATEGORY PAGE COLLECTION
    // ======================================================

    /// <summary>
    /// Crawls all paginated pages of a category and populates <paramref name="map"/>
    /// with SKU → URL entries extracted directly from
    /// <c>&lt;a class="product-variation" href="...url.html#SKU"&gt;</c> links.
    ///
    /// If a product link has no numeric SKU fragment (rare single-variant products),
    /// the base URL is added to <paramref name="needsProductFetch"/> for a follow-up
    /// product-page fetch.
    /// </summary>
    private async Task CollectSkuUrlsFromCategoryAsync(
        string categoryUrl,
        string categoryName,
        ConcurrentDictionary<string, string> map,
        ConcurrentBag<string> needsProductFetch,
        CancellationToken ct)
    {
        var firstHtml = await FetchHtmlAsync(categoryUrl, ct);
        if (string.IsNullOrWhiteSpace(firstHtml))
        {
            _logger.LogWarning("[LiquiMoly] Category '{Category}' returned empty response", categoryName);
            return;
        }

        var firstDoc = new HtmlDocument();
        firstDoc.LoadHtml(firstHtml);
        ExtractSkuMappingsFromPage(firstDoc, map, needsProductFetch);

        int totalPages = Math.Min(ExtractTotalPages(firstDoc), MaxCategoryPages);

        _logger.LogInformation(
            "[LiquiMoly] Category '{Category}' has {Pages} page(s)",
            categoryName, totalPages);

        if (totalPages > 1)
        {
            await ForEachBoundedAsync(
                Enumerable.Range(2, totalPages - 1),
                _settings.MaxParallelRequests,
                async page =>
                {
                    await Task.Delay(_settings.DelayBetweenRequestsMs * (page - 1), ct);

                    var html = await FetchHtmlAsync($"{categoryUrl}?p={page}", ct);
                    if (string.IsNullOrWhiteSpace(html)) return;

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    ExtractSkuMappingsFromPage(doc, map, needsProductFetch);
                }, ct);
        }

        _logger.LogInformation(
            "[LiquiMoly] Category '{Category}' → index now has {Count} SKU mappings",
            categoryName, map.Count);
    }

    /// <summary>
    /// Parses all <c>a.product-variation</c> links on a listing page.
    /// Each href is expected to look like <c>https://…/product-name-pNNNNNN.html#SKU</c>
    /// where the fragment is the numeric article number.
    /// </summary>
    private static void ExtractSkuMappingsFromPage(
        HtmlDocument doc,
        ConcurrentDictionary<string, string> map,
        ConcurrentBag<string> needsProductFetch)
    {
        var links = doc.DocumentNode.SelectNodes("//a[contains(@class,'product-variation')]");
        if (links == null) return;

        var seenBaseUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", null)?.Trim();
            if (string.IsNullOrWhiteSpace(href) || !href.StartsWith("http")) continue;

            var hashIdx = href.IndexOf('#');
            if (hashIdx > 0 && hashIdx < href.Length - 1)
            {
                var fragment = href[(hashIdx + 1)..];
                if (ValidSkuPattern.IsMatch(fragment))
                {
                    // Fragment IS the SKU — map it directly
                    map.TryAdd(fragment, href);
                    continue;
                }
            }

            // No numeric SKU fragment — queue the base product page for a separate fetch
            var baseUrl = hashIdx > 0 ? href[..hashIdx] : href;
            if (baseUrl.Contains(".html") && seenBaseUrls.Add(baseUrl))
                needsProductFetch.Add(baseUrl);
        }
    }

    private static int ExtractTotalPages(HtmlDocument doc)
    {
        // Magento 2 pagination: <a href="...?p=N">N</a>
        var pageLinks = doc.DocumentNode.SelectNodes("//a[contains(@href,'?p=')]");
        if (pageLinks == null) return 1;

        int max = 1;
        foreach (var link in pageLinks)
        {
            var href = link.GetAttributeValue("href", "");
            var m = Regex.Match(href, @"\?p=(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var p) && p > max)
                max = p;
        }

        return max;
    }

    // ======================================================
    // SCRAPE PRODUCT PAGE
    // ======================================================

    private async Task<LiquiMolyProductDto?> ScrapeProductPageForSkuAsync(
        string requestedSku,
        string productUrlWithHash,
        CancellationToken ct)
    {
        // The hash (#sku) is handled client-side by Magento's JS — strip it before fetching
        var pageUrl = productUrlWithHash.Contains('#')
            ? productUrlWithHash[..productUrlWithHash.IndexOf('#')]
            : productUrlWithHash;

        var html = await FetchHtmlAsync(pageUrl, ct);
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var name        = ExtractName(doc);
        var desc        = ExtractDescription(doc);
        var images      = ExtractAllImages(doc);
        var (cat, sub)  = ExtractCategories(doc);
        var approvals   = ExtractApprovals(doc);
        var sizes       = ExtractAllPackagingSizes(doc, requestedSku, name);
        var (pdf, sds)  = ExtractDownloads(doc);

        // SpecGrade: name first, then description
        var specGrade = ExtractSpecGrade(name ?? "")
                     ?? ExtractSpecGrade(desc ?? "");

        return new LiquiMolyProductDto
        {
            ArticleNumber         = requestedSku,
            Name                  = name ?? requestedSku,
            Description           = desc,
            ProductUrl            = productUrlWithHash,
            ImageUrl              = images.FirstOrDefault(),
            AllImageUrls          = images,
            PackagingSize         = sizes.FirstOrDefault(),
            AllPackagingSizes     = sizes,
            Category              = cat,
            SubCategory           = sub,
            Specifications        = new Dictionary<string, string>(),
            Approvals             = approvals,
            SpecGrade             = specGrade,
            ProductInfoPdfUrl     = pdf,
            SafetyDataSheetPdfUrl = sds,
        };
    }

    // ======================================================
    // EXTRACTION HELPERS  (all Magento 2 / Liqui-Moly specific)
    // ======================================================

    /// <summary>Product name from &lt;h1 class="page-title"&gt; or any &lt;h1&gt;.</summary>
    private static string? ExtractName(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//h1[contains(@class,'page-title')]")
                ?? doc.DocumentNode.SelectSingleNode("//h1");

        return node == null ? null : HtmlEntity.DeEntitize(node.InnerText.Trim());
    }

    /// <summary>
    /// Product description from the Description tab section.
    /// Magento 2 uses <c>div.product-info-description</c> or <c>div[@itemprop='description']</c>.
    /// </summary>
    private static string? ExtractDescription(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'product-info-description')]")
                ?? doc.DocumentNode.SelectSingleNode("//div[@itemprop='description']")
                ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'description')]");

        return node == null ? null : HtmlEntity.DeEntitize(node.InnerText.Trim());
    }

    /// <summary>
    /// All product images.
    /// Liqui-Moly serves images through <c>liquimoly.cloudimg.io</c>; falls back to
    /// Magento gallery and itemprop selectors.
    /// </summary>
    private List<string> ExtractAllImages(HtmlDocument doc)
    {
        var urls = new List<string>();

        // Images go through liquimoly.cloudimg.io CDN
        var nodes = doc.DocumentNode.SelectNodes("//img[contains(@src,'cloudimg.io')]")
                 ?? doc.DocumentNode.SelectNodes("//img[contains(@src,'liqui-moly.com')]")
                 ?? doc.DocumentNode.SelectNodes("//div[contains(@class,'gallery')]//img")
                 ?? doc.DocumentNode.SelectNodes("//img[contains(@class,'gallery-placeholder__image')]");

        if (nodes != null)
        {
            foreach (var node in nodes)
            {
                var src = node.GetAttributeValue("src", null)
                       ?? node.GetAttributeValue("data-src", null);
                var abs = BuildAbsoluteOrNull(src);
                if (abs != null && !urls.Contains(abs))
                    urls.Add(abs);
            }
        }

        return urls;
    }

    /// <summary>
    /// Category and sub-category from the Magento 2 breadcrumb navigation.
    ///
    /// Page breadcrumb: Home | Products | Oils | Top Tec 4200 5W-30 New Generation
    /// → Category = "Oils", SubCategory = null (only one intermediate crumb)
    ///
    /// If deeper: Home | Products | Oils | Motor Oils | Product Name
    /// → Category = "Oils", SubCategory = "Motor Oils"
    /// </summary>
    private static (string? category, string? subCategory) ExtractCategories(HtmlDocument doc)
    {
        // Magento 2 confirmed structure: <ol class="breadcrumb"><li><a>...</a></li></ol>
        var ol = doc.DocumentNode.SelectSingleNode("//ol[contains(@class,'breadcrumb')]")
              ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'breadcrumbs')]//ul");

        if (ol == null) return (null, null);

        var crumbs = ol.SelectNodes("li/a")
            ?.Select(n => HtmlEntity.DeEntitize(n.InnerText.Trim()))
            .Where(t => !string.IsNullOrWhiteSpace(t)
                     && !t.Equals("Home", StringComparison.OrdinalIgnoreCase)
                     && !t.Equals("Products", StringComparison.OrdinalIgnoreCase))
            .ToList()
            ?? new List<string>();

        return (
            crumbs.Count >= 1 ? crumbs[0] : null,
            crumbs.Count >= 2 ? crumbs[1] : null
        );
    }

    /// <summary>
    /// Extracts OEM/industry approvals from the "Approvals &amp; Specifications" tab.
    ///
    /// Liqui-Moly uses plain comma-separated text under a "Specifications / Approvals"
    /// bold heading, e.g.:
    ///   ACEA C3, API SQ, BMW Longlife-04, MB-Approval 229.31/229.51/229.52, ...
    /// </summary>
    private static List<string> ExtractApprovals(HtmlDocument doc)
    {
        // Try the approvals tab content div (Magento tab ID)
        var approvalDivSelectors = new[]
        {
            "//div[@id='tab-detail-approvalsandspecifications']",
            "//div[contains(@class,'approvals')]",
            "//div[contains(@class,'approval')]",
            "//section[contains(@class,'approval')]",
        };

        foreach (var sel in approvalDivSelectors)
        {
            var node = doc.DocumentNode.SelectSingleNode(sel);
            if (node == null) continue;

            var items = ParseApprovalText(HtmlEntity.DeEntitize(node.InnerText));
            if (items.Count > 0) return items;
        }

        // Fallback: find the paragraph after "Specifications / Approvals" bold heading
        var boldHeadings = doc.DocumentNode
            .SelectNodes("//strong | //b")
            ?.Where(n => n.InnerText.Contains("Specifications", StringComparison.OrdinalIgnoreCase)
                      && n.InnerText.Contains("Approvals", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (boldHeadings != null)
        {
            foreach (var heading in boldHeadings)
            {
                // Collect text from the parent element that contains the heading
                var parent = heading.ParentNode;
                if (parent == null) continue;

                var fullText = HtmlEntity.DeEntitize(parent.InnerText);
                // Strip the heading label itself and parse what remains
                var colonIdx = fullText.IndexOf(':', StringComparison.Ordinal);
                if (colonIdx >= 0)
                {
                    var items = ParseApprovalText(fullText[(colonIdx + 1)..]);
                    if (items.Count > 0) return items;
                }
            }
        }

        return new List<string>();
    }

    private static List<string> ParseApprovalText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        return text
            .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 2 && p.Length < 120) // skip blanks and runaway paragraphs
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Extracts all available packaging sizes.
    ///
    /// Magento 2 swatch renderer shows container contents (gebindeinhalt) as
    /// <c>div.swatch-option.text</c> elements with <c>option-label="1 l"</c> etc.
    /// Also checks the current variant's <c>variantswitch-sku-{sku}</c> div and
    /// falls back to the product name.
    /// </summary>
    private static List<string> ExtractAllPackagingSizes(
        HtmlDocument doc,
        string requestedSku,
        string? name)
    {
        var sizes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Magento 2 swatch text options (container contents attribute)
        var swatchOpts = doc.DocumentNode
            .SelectNodes("//div[contains(@class,'swatch-option') and contains(@class,'text')]")
         ?? doc.DocumentNode.SelectNodes("//div[contains(@class,'swatch-option')]");

        if (swatchOpts != null)
        {
            foreach (var opt in swatchOpts)
            {
                var label = opt.GetAttributeValue("option-label", null)
                         ?? opt.GetAttributeValue("data-option-label", null)
                         ?? HtmlEntity.DeEntitize(opt.InnerText.Trim());

                if (!string.IsNullOrWhiteSpace(label))
                    foreach (Match m in SizePattern.Matches(label))
                        sizes.Add(m.Groups[1].Value);
            }
        }

        // Current variant's dedicated section
        var variantDiv = doc.DocumentNode
            .SelectSingleNode($"//div[contains(@class,'variantswitch-sku-{requestedSku}')]");

        if (variantDiv != null)
        {
            foreach (Match m in SizePattern.Matches(HtmlEntity.DeEntitize(variantDiv.InnerText)))
                sizes.Add(m.Groups[1].Value);
        }

        // Fallback: product name
        if (sizes.Count == 0 && !string.IsNullOrWhiteSpace(name))
            foreach (Match m in SizePattern.Matches(name))
                sizes.Add(m.Groups[1].Value);

        return sizes.ToList();
    }

    /// <summary>
    /// Extracts PDF download links (product information sheet and safety data sheet).
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

            if (isSds && sdsUrl == null) sdsUrl = abs;
            else if (!isSds && pdfUrl == null) pdfUrl = abs;

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

    /// <summary>
    /// Tries to extract the displayed SKU from a single-variant Magento 2 product page.
    /// Looks for <c>&lt;span itemprop="sku"&gt;</c>.
    /// </summary>
    private static string? ExtractSkuFromPage(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//span[@itemprop='sku']")
                ?? doc.DocumentNode.SelectSingleNode(
                       "//div[contains(@class,'product-info-stock-sku')]//span[@class='value']");

        if (node == null) return null;
        var text = HtmlEntity.DeEntitize(node.InnerText.Trim());
        return ValidSkuPattern.IsMatch(text) ? text : null;
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
                var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

                if (resp.IsSuccessStatusCode)
                    return await resp.Content.ReadAsStringAsync(ct);

                _logger.LogWarning(
                    "[LiquiMoly] HTTP {Status} for {Url} (attempt {A}/{Max})",
                    (int)resp.StatusCode, url, attempt + 1, maxRetries + 1);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < maxRetries)
            {
                _logger.LogWarning(ex,
                    "[LiquiMoly] Fetch error for {Url} (attempt {A}/{Max})",
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
        if (path.StartsWith("http")) return path;
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
