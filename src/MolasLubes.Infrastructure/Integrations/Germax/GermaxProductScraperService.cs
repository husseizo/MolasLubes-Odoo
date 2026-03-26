using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.Germax.Dtos;

namespace MolasLubes.Infrastructure.Integrations.Germax;

/// <summary>
/// Scrapes germaxparts.com to enrich SAP seed items with product data.
/// Conservative and sequential: MaxConcurrency=1, delay between each request.
/// Operates in three search strategies in order: item_code →
/// item_name_engine_code → item_name_only.  Stops at the first strategy
/// that produces a candidate above the minimum score threshold.
/// </summary>
public class GermaxProductScraperService
{
    // Minimum score to accept a candidate as a match
    private const decimal MinScore = 60m;

    // Germax article number pattern as seen in product titles, e.g. "GL2787"
    private static readonly Regex ArticlePattern =
        new(@"\b([A-Z]{1,3}\d{3,6})\b", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly GermaxScraperSettings _settings;
    private readonly ILogger<GermaxProductScraperService> _logger;

    public GermaxProductScraperService(
        HttpClient httpClient,
        IOptions<GermaxScraperSettings> settings,
        ILogger<GermaxProductScraperService> logger)
    {
        _http     = httpClient;
        _settings = settings.Value;
        _logger   = logger;
    }

    // =====================================================
    // PUBLIC ENTRY — try to enrich one seed item
    // =====================================================

    /// <summary>
    /// Attempts to find and scrape a Germax product page for the given seed.
    /// Returns an enriched <see cref="GermaxProductDto"/> on success,
    /// or null when no candidate meets the score threshold or an error occurs.
    /// The caller is responsible for setting ScrapeStatus on the cache row.
    /// </summary>
    public async Task<GermaxProductDto?> TryEnrichAsync(
        GermaxSeedDto seed,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "GermaxScraper: enriching | ItemCode={Code} | Group={Group}",
            seed.ItemCode, seed.ItemGroupName);

        var strategies = _settings.SearchStrategyOrder.Count > 0
            ? _settings.SearchStrategyOrder
            : new List<string> { "item_code", "item_name_engine_code", "item_name_only" };

        // Tracks whether at least one search HTTP round-trip completed (even if it
        // returned zero results).  If every attempt fails to reach Germax we throw
        // so the job records ERROR instead of NO_MATCH, preserving the item for retry.
        var anySearchReached = false;

        foreach (var strategy in strategies)
        {
            // Each strategy may expand into multiple search terms (one per OEM alias).
            // item_name_only for "LR016962/LR026221/ADJ134204" tries all three in
            // order and accepts the first candidate that scores above MinScore.
            var terms = GetSearchTerms(seed, strategy);

            foreach (var term in terms)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(term))
                    continue;

                _logger.LogDebug(
                    "GermaxScraper: strategy={Strategy} | term={Term}", strategy, term);

                List<GermaxCandidateDto> candidates;
                try
                {
                    candidates = await SearchCandidatesAsync(term, strategy, ct);
                    anySearchReached = true; // HTTP round-trip completed (results may be empty)
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "GermaxScraper: search failed | strategy={Strategy} | term={Term}",
                        strategy, term);
                    continue;
                }

                if (candidates.Count == 0)
                {
                    _logger.LogDebug(
                        "GermaxScraper: no candidates | strategy={Strategy} | term={Term}",
                        strategy, term);
                    continue;
                }

                var best = ResolveBestCandidate(seed, candidates);
                if (best == null)
                {
                    _logger.LogDebug(
                        "GermaxScraper: no candidate above threshold | strategy={Strategy} | term={Term}",
                        strategy, term);
                    continue;
                }

                _logger.LogInformation(
                    "GermaxScraper: match found | strategy={Strategy} | term={Term} | score={Score:F1} | url={Url}",
                    strategy, term, best.Score, best.ProductUrl);

                try
                {
                    return await ScrapeProductPageAsync(seed.ItemCode, best, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "GermaxScraper: page scrape failed | url={Url} — trying next",
                        best.ProductUrl);
                    continue; // try next alias or next strategy
                }
            }
        }

        // All strategies and aliases exhausted without a single successful HTTP response →
        // this is a transient infrastructure failure, not a genuine "no match".
        if (!anySearchReached)
            throw new InvalidOperationException(
                $"All search strategies failed to reach Germax for ItemCode={seed.ItemCode} " +
                "— possible transient outage; item will be retried");

        _logger.LogInformation(
            "GermaxScraper: no match for ItemCode={Code} after all strategies", seed.ItemCode);
        return null;
    }

    // =====================================================
    // SEARCH
    // =====================================================

    internal async Task<List<GermaxCandidateDto>> SearchCandidatesAsync(
        string term,
        string strategy,
        CancellationToken ct)
    {
        var paths = _settings.SearchPaths.Count > 0
            ? _settings.SearchPaths
            : new List<string> { "/?s={term}&post_type=product" };

        var allCandidates = new List<GermaxCandidateDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var searchPath in paths)
        {
            var url = _settings.BaseUrl.TrimEnd('/')
                      + searchPath.Replace("{term}", HttpUtility.UrlEncode(term));

            var html = await FetchAsync(url, ct);
            if (html == null) continue;

            foreach (var c in ParseSearchResults(html, strategy))
            {
                if (seen.Add(c.ProductUrl))
                    allCandidates.Add(c);
            }
        }

        return allCandidates;
    }

    private List<GermaxCandidateDto> ParseSearchResults(string html, string strategy)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var candidates = new List<GermaxCandidateDto>();

        // WooCommerce search result products: li.product inside ul.products
        var productNodes = doc.DocumentNode
            .SelectNodes("//ul[contains(@class,'products')]//li[contains(@class,'product')]");

        if (productNodes == null || productNodes.Count == 0)
        {
            // Fallback: any div with class 'product'
            productNodes = doc.DocumentNode
                .SelectNodes("//div[contains(@class,'product-inner')]");
        }

        if (productNodes == null)
            return candidates;

        foreach (var node in productNodes.Take(_settings.MaxCandidatesPerSearch))
        {
            // Primary link
            var link = node.SelectSingleNode(
                ".//a[contains(@class,'woocommerce-LoopProduct-link')]")
                ?? node.SelectSingleNode(".//a[contains(@href,'/product/')]")
                ?? node.SelectSingleNode(".//a[@href]");

            if (link == null) continue;

            var href = link.GetAttributeValue("href", string.Empty).Trim();
            if (string.IsNullOrEmpty(href) || !href.StartsWith("http")) continue;

            // Title — prefer h2 inside the node, fall back to link title attribute
            var titleNode = node.SelectSingleNode(
                ".//*[contains(@class,'woocommerce-loop-product__title')]")
                ?? node.SelectSingleNode(".//*[contains(@class,'product-title')]")
                ?? node.SelectSingleNode(".//h2")
                ?? node.SelectSingleNode(".//h3");

            var title = titleNode != null
                ? HtmlEntity.DeEntitize(titleNode.InnerText).Trim()
                : link.GetAttributeValue("title", string.Empty).Trim();

            candidates.Add(new GermaxCandidateDto
            {
                ProductUrl     = href,
                Title          = string.IsNullOrEmpty(title) ? null : title,
                ArticleNumber  = ExtractArticleNumber(title),
                SearchStrategy = strategy
            });
        }

        _logger.LogDebug(
            "GermaxScraper: parsed {Count} candidates from search results", candidates.Count);

        return candidates;
    }

    // =====================================================
    // SCORING
    // =====================================================

    internal GermaxCandidateDto? ResolveBestCandidate(
        GermaxSeedDto seed,
        IReadOnlyList<GermaxCandidateDto> candidates)
    {
        GermaxCandidateDto? best = null;

        foreach (var c in candidates)
        {
            c.Score = ComputeMatchScore(seed, c);
            _logger.LogDebug(
                "GermaxScraper: scored | url={Url} | score={Score:F1}", c.ProductUrl, c.Score);

            if (c.Score >= MinScore && (best == null || c.Score > best.Score))
                best = c;
        }

        return best;
    }

    internal decimal ComputeMatchScore(GermaxSeedDto seed, GermaxCandidateDto candidate)
    {
        var score = 0m;

        var normTitle = NormalizeText(candidate.Title ?? string.Empty);
        var normUrl   = candidate.ProductUrl.ToLowerInvariant();

        // SAP ItemName holds OEM / reference codes (e.g. "SEM500050",
        // "LR016962/LR026221/ADJ134204").  Germax uses its own GL... catalog codes.
        // Do NOT compare aliases against the Germax article number — they live in
        // different namespaces.  Instead, look for aliases in the title and URL.
        var aliases = ExtractAliases(seed.ItemName)
            .Select(NormalizeText)
            .Where(a => a.Length >= 4)
            .ToList();

        // +80: any OEM alias appears in the product title — primary match signal.
        // Germax titles typically list OEM references alongside their own GL... code.
        if (aliases.Any(a => normTitle.Contains(a)))
            score += 80m;

        // +40: any OEM alias appears in the product URL slug — secondary confirmation.
        if (aliases.Any(a => normUrl.Contains(a)))
            score += 40m;

        // +30: engine code appears in title (often null for Land Rover seeds — skipped)
        if (!string.IsNullOrEmpty(seed.EngineCode))
        {
            var normEngine = NormalizeText(seed.EngineCode);
            if (!string.IsNullOrEmpty(normEngine) && normTitle.Contains(normEngine))
                score += 30m;
        }

        // +20: item group name (e.g. "Land Rover") appears in the title
        if (!string.IsNullOrEmpty(seed.ItemGroupName))
        {
            var normGroup = NormalizeText(seed.ItemGroupName);
            if (!string.IsNullOrEmpty(normGroup) && normTitle.Contains(normGroup))
                score += 20m;
        }

        return score;
    }

    // =====================================================
    // PAGE SCRAPE
    // =====================================================

    internal async Task<GermaxProductDto?> ScrapeProductPageAsync(
        string itemCode,
        GermaxCandidateDto candidate,
        CancellationToken ct)
    {
        var html = await FetchAsync(candidate.ProductUrl, ct);
        if (html == null) return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var dto = new GermaxProductDto
        {
            ItemCode      = itemCode,
            MatchMethod   = candidate.SearchStrategy,
            MatchScore    = candidate.Score
        };

        // Canonical URL (preferred) or fall back to the URL we fetched
        var canonical = doc.DocumentNode
            .SelectSingleNode("//link[@rel='canonical']")?
            .GetAttributeValue("href", null);
        dto.ProductUrl = string.IsNullOrEmpty(canonical) ? candidate.ProductUrl : canonical;

        // Article number — from candidate title first, then attribute table
        dto.GermaxArticleNumber = candidate.ArticleNumber;

        // Attributes table (WooCommerce shop_attributes)
        var attrRows = doc.DocumentNode
            .SelectNodes("//table[contains(@class,'shop_attributes')]//tr");

        if (attrRows != null)
        {
            foreach (var row in attrRows)
            {
                var label = row.SelectSingleNode(
                    ".//th[contains(@class,'woocommerce-product-attributes-item__label')]"
                    + "|.//th")?.InnerText.Trim() ?? string.Empty;

                var value = HtmlEntity.DeEntitize(
                    row.SelectSingleNode(
                        ".//td[contains(@class,'woocommerce-product-attributes-item__value')]"
                        + "|.//td")?.InnerText.Trim() ?? string.Empty);

                var lbl = label.ToLowerInvariant();

                if (lbl.Contains("germax"))
                    dto.GermaxArticleNumber ??= value;
                else if (lbl.Contains("oem"))
                    dto.OemPartNumber = value;
                else if (lbl.Contains("fit") || lbl.Contains("vehicle"))
                    dto.FitForAuto = value;
            }
        }

        // Description — short description first, then long-form tab
        var shortDesc = doc.DocumentNode
            .SelectSingleNode(
                "//div[contains(@class,'woocommerce-product-details__short-description')]");
        var longDesc = doc.DocumentNode
            .SelectSingleNode("//div[@id='tab-description']");

        var descNode = shortDesc ?? longDesc;
        if (descNode != null)
            dto.Description = HtmlEntity.DeEntitize(descNode.InnerText).Trim();

        // Images — WooCommerce product gallery
        var imgNodes = doc.DocumentNode
            .SelectNodes(
                "//div[contains(@class,'woocommerce-product-gallery')]//img[@src]");

        var imageUrls = new List<string>();
        if (imgNodes != null)
        {
            foreach (var img in imgNodes)
            {
                var src = img.GetAttributeValue("src", string.Empty).Trim();
                if (!string.IsNullOrEmpty(src) && src.StartsWith("http"))
                    imageUrls.Add(src);
            }
        }

        if (imageUrls.Count > 0)
        {
            dto.ImageUrl    = imageUrls[0];
            dto.AllImageUrls = JsonSerializer.Serialize(imageUrls);
        }

        _logger.LogDebug(
            "GermaxScraper: scraped page | ItemCode={Code} | Germax={Art} | OEM={Oem} | Images={ImgCount}",
            itemCode, dto.GermaxArticleNumber, dto.OemPartNumber, imageUrls.Count);

        return dto;
    }

    // =====================================================
    // HELPERS
    // =====================================================

    /// <summary>
    /// Returns the ordered list of search terms to try for this strategy.
    /// Alias-based strategies (item_name_only, item_name_engine_code) expand
    /// ItemName into one term per OEM alias so that slash-joined alternates like
    /// "LR016962/LR026221/ADJ134204" are each tried independently.
    /// </summary>
    private IReadOnlyList<string> GetSearchTerms(GermaxSeedDto seed, string strategy)
    {
        switch (strategy)
        {
            case "item_code":
                // Internal SAP keys (LR100001) are kept as-is. Germax typically
                // returns nothing, and the pipeline falls through to item_name_only.
                return new[] { seed.ItemCode };

            case "item_name_engine_code":
                if (string.IsNullOrWhiteSpace(seed.EngineCode))
                    return Array.Empty<string>();
                var ec = seed.EngineCode.Trim();
                return ExtractAliases(seed.ItemName)
                    .Select(a => $"{a} {ec}")
                    .ToArray();

            case "item_name_only":
                var aliases = ExtractAliases(seed.ItemName);
                // Fall back to raw ItemName when it contains no tokenisable alias
                return aliases.Count > 0
                    ? (IReadOnlyList<string>)aliases
                    : new[] { seed.ItemName };

            default:
                return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Splits <paramref name="itemName"/> on common separators (/, whitespace,
    /// semicolons, commas) and returns distinct tokens that are at least 4 characters.
    /// E.g. "LR016962/LR026221/ADJ134204" → ["LR016962", "LR026221", "ADJ134204"].
    /// </summary>
    private static IReadOnlyList<string> ExtractAliases(string itemName) =>
        Regex.Split(itemName.Trim(), @"[/\s;,]+")
             .Where(t => t.Length >= 4)
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToList()
             .AsReadOnly();

    private static string? ExtractArticleNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var m = ArticlePattern.Match(title);
        return m.Success ? m.Groups[1].Value : null;
    }

    internal string NormalizeText(string text) =>
        Regex.Replace(text.ToLowerInvariant().Trim(), @"\s+", " ");

    private async Task<string?> FetchAsync(string url, CancellationToken ct)
    {
        await Task.Delay(_settings.DelayBetweenRequestsMs, ct);

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, ct);
        }
        catch (TaskCanceledException)
        {
            throw; // propagate cancellation
        }
        catch (Exception ex)
        {
            // Network-level failure (DNS, connection refused, timeout) — transient;
            // let it propagate so callers can distinguish it from a genuine "not found".
            _logger.LogWarning(ex, "GermaxScraper: network error | url={Url}", url);
            throw;
        }

        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync(ct);

        // 5xx: transient server error — propagate so callers record ERROR, not NO_MATCH
        if ((int)response.StatusCode >= 500)
        {
            _logger.LogWarning(
                "GermaxScraper: HTTP {Status} (transient) | url={Url}",
                (int)response.StatusCode, url);
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} for {url}", null, response.StatusCode);
        }

        // 4xx: page genuinely doesn't exist — not a transient failure
        _logger.LogWarning(
            "GermaxScraper: HTTP {Status} | url={Url}", (int)response.StatusCode, url);
        return null;
    }
}
