# AutoHub — Germax Scraper Design

## Overview

The Germax scraper is a deterministic, conservative, single-threaded HTTP scraper that enriches SAP items with product data from germaxparts.com.

It is **not** a general crawler. It operates on a bounded seed set from SAP (Land Rover and Volvo groups only) and matches each seed item against a Germax product page using a scored, ordered search strategy.

---

## Input: SAP Seed

The scraper receives a list of `GermaxSeedDto` from `SapAutoHubSeedReader`.

```csharp
public class GermaxSeedDto
{
    public string  ItemCode      { get; set; }   // e.g. "GL2787"
    public string  ItemName      { get; set; }   // e.g. "Turbocharger TD6"
    public string? EngineCode    { get; set; }   // SAP U_Engine_Code UDF
    public string  ItemGroupCode { get; set; }   // SAP ItmsGrpCod
    public string  ItemGroupName { get; set; }   // "Land Rover" or "Volvo"
}
```

---

## SAP Seed Query

### Full seed (all active items)

```sql
SELECT
    T0.ItemCode,
    T0.ItemName,
    T0.U_Engine_Code,
    T1.ItmsGrpCod,
    T1.ItmsGrpNam,
    T0.UpdateDate,
    T0.frozenFor
FROM OITM T0
INNER JOIN OITB T1
    ON T0.ItmsGrpCod = T1.ItmsGrpCod
WHERE
    T0.frozenFor = 'N'
    AND T1.ItmsGrpNam IN ('Land Rover', 'Volvo')
ORDER BY
    T1.ItmsGrpNam,
    T0.ItemCode;
```

### Delta seed (changed rows since watermark)

```sql
SELECT
    T0.ItemCode,
    T0.ItemName,
    T0.U_Engine_Code,
    T1.ItmsGrpCod,
    T1.ItmsGrpNam,
    T0.UpdateDate,
    T0.frozenFor
FROM OITM T0
INNER JOIN OITB T1
    ON T0.ItmsGrpCod = T1.ItmsGrpCod
WHERE
    T0.frozenFor = 'N'
    AND T1.ItmsGrpNam IN ('Land Rover', 'Volvo')
    AND T0.UpdateDate >= @Watermark
ORDER BY
    T0.UpdateDate,
    T0.ItemCode;
```

> **Note:** If the item group names differ in your SAP instance, filter by `ItmsGrpCod` instead of `ItmsGrpNam` to avoid missed rows.

---

## Search Strategy

Run strategies in order. Stop at the first strategy that returns at least one candidate above the score threshold.

| Strategy key | Search term built from | When to use |
|---|---|---|
| `item_code` | `ItemCode` directly | SAP code matches Germax article number (e.g. `GL2787`) |
| `item_name_engine_code` | `ItemName + " " + EngineCode` | SAP code is not Germax code, but name+engine is distinctive |
| `item_name_only` | `ItemName` only | Fallback |

Strategy order is configurable via `GermaxScraper.SearchStrategyOrder` in `appsettings.json`.

---

## Candidate Scoring

After fetching search results, score each candidate:

| Signal | Points |
|---|---|
| `GermaxArticleNumber` exactly equals `ItemCode` | +100 |
| Product title contains `EngineCode` | +30 |
| Product title contains `ItemName` tokens (≥ 2 tokens) | +20 |
| Product category matches item group (`Land Rover`) | +20 |
| OEM part number contains SAP code | +15 |

**Minimum threshold:** 60 points. Candidates below 60 are rejected.

If no candidate meets the threshold, set `ScrapeStatus = 'NO_MATCH'`.

---

## What to Scrape from a Product Page

The Germax product page exposes structured labels. Extract:

| Field | CSS / DOM target | Maps to |
|---|---|---|
| Article number | Label "Germax number" | `GermaxArticleNumber` |
| Product name / title | `<h1>` or product title | (stored in DTO) |
| OEM part number | Label "OEM Part Number" | `OemPartNumber` |
| Fit for auto | Label "Fit for Auto" | `FitForAuto` |
| Description | Product description block | `Description` |
| Canonical URL | `<link rel="canonical">` | `ProductUrl` |
| Primary image | First `<img>` in product gallery | `ImageUrl` |
| All images | All `<img>` in product gallery | `AllImageUrls` (JSON array) |

---

## GermaxProductScraperService — Method Contract

```csharp
public class GermaxProductScraperService
{
    // 1. Run search for a seed using the configured strategy order
    //    Returns up to MaxCandidatesPerSearch candidates
    Task<IReadOnlyList<GermaxCandidateDto>> SearchCandidatesAsync(
        GermaxSeedDto seed,
        CancellationToken ct = default);

    // 2. Score all candidates and return the best one above threshold
    //    Returns null if no candidate meets the minimum score
    Task<GermaxCandidateDto?> ResolveBestCandidateAsync(
        GermaxSeedDto seed,
        IReadOnlyList<GermaxCandidateDto> candidates);

    // 3. Fully scrape a product page and return structured data
    Task<GermaxProductDto?> ScrapeProductPageAsync(
        string url,
        CancellationToken ct = default);

    // 4. Pure scoring function — testable without HTTP
    decimal ComputeMatchScore(GermaxSeedDto seed, GermaxCandidateDto candidate);

    // 5. Lowercase, trim, collapse whitespace, remove punctuation
    string NormalizeText(string text);
}
```

---

## DTO Reference

```csharp
public class GermaxCandidateDto
{
    public string  ProductUrl           { get; set; } = string.Empty;
    public string? Title                { get; set; }
    public string? GermaxArticleNumber  { get; set; }
    public string? Category             { get; set; }
    public decimal Score                { get; set; }
    public string  SearchStrategy       { get; set; } = string.Empty;
}

public class GermaxProductDto
{
    public string  ItemCode             { get; set; } = string.Empty;
    public string? GermaxArticleNumber  { get; set; }
    public string? OemPartNumber        { get; set; }
    public string? FitForAuto           { get; set; }
    public string? Description          { get; set; }
    public string? ProductUrl           { get; set; }
    public string? ImageUrl             { get; set; }
    public string? AllImageUrls         { get; set; }  // JSON array string
    public string? MatchMethod          { get; set; }
    public decimal MatchScore           { get; set; }
}
```

---

## Anti-Blocking Controls

| Control | Value | Notes |
|---|---|---|
| `MaxConcurrency` | `1` | Single worker always |
| `DelayBetweenRequestsMs` | `1000–2000` | Jitter recommended |
| Retry policy | Network timeout or HTTP 429 only | Do not retry 404 or parse failures |
| Daily cap | Optional hard limit on requests/day | Configure if needed |
| User-Agent | Set a real browser UA | Avoids trivial bot blocks |
| Logging | Log every rejected match with reason | Audit trail |

---

## Volvo Caveat

Germax clearly covers Land Rover / Jaguar products. Volvo coverage is not confirmed on the live site.

**Recommended approach:**

| Phase | Action |
|---|---|
| Phase 1–5 | Seed Volvo items, attempt search, store `ScrapeStatus = 'NO_MATCH'` |
| After Phase 5 | Review 50–100 Volvo samples manually |
| Phase 6 | Only expand Volvo logic if real match rate is confirmed |

Do not write Volvo-specific scoring rules until match-rate evidence exists.

---

## Error Handling

| Scenario | Action |
|---|---|
| HTTP timeout | Set `ScrapeStatus = 'ERROR'`, store message in `ScrapeError`, retry via `GermaxRetryFailedJob` |
| HTTP 429 | Back off, retry with longer delay |
| HTTP 404 | Set `ScrapeStatus = 'NO_MATCH'` |
| Parse failure (missing DOM element) | Store partial data, set `ScrapeStatus = 'ERROR'` |
| Score below threshold | Set `ScrapeStatus = 'NO_MATCH'`, do not store Germax fields |
| SAP item frozen mid-run | Set `IsActive = false`, skip scraping |
