namespace MolasLubes.Infrastructure.Integrations.LiquiMoly;

public class LiquiMolyProductDto
{
    // ─── Identity ──────────────────────────────────────────────────────────────
    public string  ArticleNumber { get; set; } = null!;
    public string  Name          { get; set; } = null!;
    public string? ProductUrl    { get; set; }

    // ─── Classification ────────────────────────────────────────────────────────
    public string? Category      { get; set; }
    public string? SubCategory   { get; set; }

    // ─── Product detail ────────────────────────────────────────────────────────
    public string? Description   { get; set; }

    /// <summary>Primary packaging size extracted from the product name (e.g. "5 L").</summary>
    public string? PackagingSize { get; set; }

    /// <summary>All available packaging/volume variants scraped from the product page.</summary>
    public List<string> AllPackagingSizes { get; set; } = new();

    /// <summary>Volume in litres parsed from PackagingSize (e.g. 5.0, 0.5 for 500 ml). Null for weight-only units.</summary>
    public decimal? Liter { get; set; }

    /// <summary>Primary spec grade extracted from the product name (e.g. "5W-30").</summary>
    public string? SpecGrade { get; set; }

    // ─── Media ─────────────────────────────────────────────────────────────────
    /// <summary>Primary product image URL.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>All product image URLs scraped from the product detail page gallery.</summary>
    public List<string> AllImageUrls { get; set; } = new();

    // ─── Approvals & Specifications ────────────────────────────────────────────
    /// <summary>
    /// Full list of OEM / industry approvals (e.g. "BMW Longlife-04", "MB 229.51").
    /// Each entry is one approval text as listed on the product page.
    /// </summary>
    public List<string> Approvals { get; set; } = new();

    /// <summary>
    /// Key-value technical specifications as shown in the "Specifications" table
    /// on the product page (e.g. "Viscosity class" → "SAE 5W-30").
    /// </summary>
    public Dictionary<string, string> Specifications { get; set; } = new();

    // ─── Downloads ─────────────────────────────────────────────────────────────
    /// <summary>Direct URL to the English Production / Product Information PDF.</summary>
    public string? ProductInfoPdfUrl { get; set; }

    /// <summary>Direct URL to the English Safety Data Sheet PDF.</summary>
    public string? SafetyDataSheetPdfUrl { get; set; }

    // Optional if you want:
    public string? SpecificationsText { get; set; }


}
