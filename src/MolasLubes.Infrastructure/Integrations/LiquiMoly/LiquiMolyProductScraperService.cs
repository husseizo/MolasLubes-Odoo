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


    private static Dictionary<string, string>? _cachedIndex;

    private Dictionary<string, string>? _productIndex;

    private static readonly Regex VariantSkuPattern =
        new(@"^\d{4,6}$", RegexOptions.Compiled);

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

        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0");
    }

    // ======================================================
    // MAIN ENTRY
    // ======================================================

    public async Task<List<LiquiMolyProductDto>> ScrapeByArticleNumbersAsync(
     IEnumerable<string> articleNumbers,
     CancellationToken ct = default)
    {
        var raw = articleNumbers?.ToList() ?? new List<string>();

        _logger.LogInformation(
            "[LiquiMoly] Raw SKU input count: {Count}",
            raw.Count);

        if (raw.Count > 0)
        {
            _logger.LogInformation(
                "[LiquiMoly] First 10 raw SKUs: {Skus}",
                string.Join(", ", raw.Take(10)));
        }

        // Filter only valid numeric SKUs
        var targets = new HashSet<string>(
            raw
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => Regex.IsMatch(x, @"^\d{3,6}$")),
            StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(
            "[LiquiMoly] Valid numeric SKUs after filtering: {Count}",
            targets.Count);

        if (targets.Count > 0)
        {
            _logger.LogInformation(
                "[LiquiMoly] First 10 cleaned SKUs: {Skus}",
                string.Join(", ", targets.Take(10)));
        }
        if (_productIndex == null || _productIndex.Count == 0)
        {
            _logger.LogInformation("[LiquiMoly] Building product index...");
            _productIndex = await BuildProductIndexAsync(ct);
        }

        _logger.LogInformation(
            "[LiquiMoly] Product index size: {Count}",
            _productIndex.Count);

        var results = new ConcurrentBag<LiquiMolyProductDto>();

        int found = 0;
        int missing = 0;

        await ForEachBoundedAsync(
            targets,
            _settings.MaxParallelRequests,
            async sku =>
            {
                if (!_productIndex.TryGetValue(sku, out var url))
                {
                    Interlocked.Increment(ref missing);

                    _logger.LogWarning(
                        "[LiquiMoly] SKU {Sku} not found in index",
                        sku);

                    return;
                }

                Interlocked.Increment(ref found);

                _logger.LogDebug(
                    "[LiquiMoly] SKU {Sku} resolved → {Url}",
                    sku,
                    url);

                var dto = await ScrapeProductPageForSkuAsync(sku, url, ct);

                if (dto != null)
                    results.Add(dto);

            }, ct);

        _logger.LogInformation(
            "[LiquiMoly] Scrape summary | Requested={Requested} | FoundInIndex={Found} | Missing={Missing} | Scraped={Scraped}",
            targets.Count,
            found,
            missing,
            results.Count);

        return results.ToList();
    }

    // ======================================================
    // BUILD PRODUCT INDEX FROM SITEMAP
    // ======================================================

    private async Task<Dictionary<string, string>> BuildProductIndexAsync(CancellationToken ct)
    {
        if (_cachedIndex != null && _cachedIndex.Count > 0)
        {
            _logger.LogInformation(
                "[LiquiMoly] Using cached index | {Count} SKUs",
                _cachedIndex.Count);

            return _cachedIndex;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var sitemapUrl = $"{_settings.BaseUrl}/sitemap.xml";

        _logger.LogInformation(
            "[LiquiMoly] Loading sitemap index {Url}",
            sitemapUrl);

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

        var locUrls = locMatches
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .ToList();

        _logger.LogInformation(
            "[LiquiMoly] Sitemap LOC entries found: {Count}",
            locUrls.Count);

        var sitemapUrls = locUrls
            .Where(x => x.Contains("sitemap"))
            .ToList();

        if (sitemapUrls.Count > 0)
        {
            _logger.LogInformation(
                "[LiquiMoly] Found {Count} child sitemaps",
                sitemapUrls.Count);

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
                    _logger.LogWarning(ex,
                        "[LiquiMoly] Failed reading child sitemap {Url}",
                        sm);
                }
            }
        }
        else
        {
            _logger.LogWarning(
                "[LiquiMoly] No child sitemaps detected → assuming product URLs directly");

            foreach (var url in locUrls)
            {
                if (!url.Contains("/en/"))
                    continue;

                if (!url.EndsWith(".html"))
                    continue;

                var sku = ExtractSku(url);

                if (sku != null && !map.ContainsKey(sku))
                    map[sku] = url;
            }
        }

        _logger.LogInformation(
            "[LiquiMoly] Index complete | SKUs={Count}",
            map.Count);

        if (map.Count > 0)
        {
            _logger.LogInformation(
                "[LiquiMoly] Sample SKUs: {Skus}",
                string.Join(", ", map.Keys.Take(10)));

            _cachedIndex = map;
        }
        else
        {
            _logger.LogError(
                "[LiquiMoly] Product index EMPTY → sitemap structure likely changed");
        }

        return map;
    }




    private string? ExtractSku(string url)
    {
        var match = Regex.Match(url, @"p0*(\d+)\.html", RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        return match.Groups[1].Value;
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

        var name = ExtractName(doc);
        var desc = ExtractDescription(doc);

        var img =
            doc.DocumentNode.SelectSingleNode("//img[contains(@class,'product')]");

        var dto = new LiquiMolyProductDto
        {
            ArticleNumber = requestedSku,
            Name = name,
            Description = desc,
            ProductUrl = productUrl,
            ImageUrl = BuildAbsoluteOrNull(
                img?.GetAttributeValue("src", null))
        };

        var sizeMatch = SizePattern.Match(name ?? "");

        if (sizeMatch.Success)
            dto.PackagingSize = sizeMatch.Groups[1].Value;

        dto.SpecGrade = ExtractSpecGrade(name ?? "");

        return dto;
    }

    // ======================================================
    // EXTRACTION HELPERS
    // ======================================================

    private static string? ExtractName(HtmlDocument doc)
    {
        var node = doc.DocumentNode.SelectSingleNode("//h1");
        return HtmlEntity.DeEntitize(node?.InnerText.Trim() ?? "");
    }

    private static string ExtractDescription(HtmlDocument doc)
    {
        var node =
            doc.DocumentNode.SelectSingleNode("//div[@itemprop='description']");
        return HtmlEntity.DeEntitize(node?.InnerText.Trim() ?? "");
    }

    private static string? ExtractSpecGrade(string text)
    {
        var m = SpecGradePattern.Match(text ?? "");
        return m.Success ? m.Value : null;
    }

    // ======================================================
    // HTTP
    // ======================================================

    private async Task<string> FetchHtmlAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";

        try
        {
            var resp = await _http.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode)
                return "";

            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return "";
        }
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
        if (string.IsNullOrWhiteSpace(path))
            return null;

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