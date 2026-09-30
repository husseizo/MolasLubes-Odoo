namespace MolasLubes.Domain.Entities.Cache;

public class CacheLiquiMolyProduct
{
    // =============================
    // IDENTITY
    // =============================
    public string ArticleNumber { get; set; } = null!;   // PK — Liqui-Moly article no. e.g. "20001"
    public string Name          { get; set; } = null!;

    // =============================
    // CLASSIFICATION
    // =============================
    public string? Category     { get; set; }             // e.g. "Engine Oils"
    public string? SubCategory  { get; set; }             // e.g. "Leichtlauf"
    public string? Description  { get; set; }
    public string? SpecGrade    { get; set; }             // e.g. "5W-30", "SAE 80W-90"

    // =============================
    // PACKAGING / SIZES
    // =============================
    /// <summary>Primary packaging size (e.g. "5 L").</summary>
    public string? PackagingSize    { get; set; }

    /// <summary>All available sizes as a JSON array (e.g. ["1 L","5 L","20 L"]).</summary>
    public string? AllPackagingSizes { get; set; }        // JSON-serialised List<string>

    /// <summary>Volume in litres (e.g. 5.0, 0.5 for 500 ml). Null for weight-only units.</summary>
    public decimal? Liter { get; set; }

    // =============================
    // MEDIA
    // =============================
    /// <summary>Primary product image URL.</summary>
    public string? ImageUrl     { get; set; }

    /// <summary>All product images as a JSON array (gallery from the detail page).</summary>
    public string? AllImageUrls { get; set; }             // JSON-serialised List<string>

    /// <summary>
    /// Article number whose image was selected as the primary.
    /// Equals <see cref="ArticleNumber"/> when confirmed; equals the sibling SKU for explicit
    /// cross-variant fallbacks; null when no image was found.
    /// </summary>
    public string? ImageSourceArticleNumber { get; set; }

    /// <summary>True when <see cref="ImageUrl"/> was selected through a fallback rather than a direct SKU match.</summary>
    public bool ImageFallbackUsed { get; set; }

    /// <summary>Human-readable explanation set when <see cref="ImageFallbackUsed"/> is true.</summary>
    public string? ImageFallbackReason { get; set; }

    // =============================
    // BARCODES / SAP UOM
    // =============================
    public string? PrimaryBarcode { get; set; }
    public string? PrimaryBarcodeUomCode { get; set; }
    public string? PrimaryBarcodeUomName { get; set; }
    public int? PrimaryBarcodeUomEntry { get; set; }
    public decimal? PrimaryBarcodeBaseQtyInGroup { get; set; }
    public bool HasUnitBarcode { get; set; }
    public string? EanCode { get; set; }
    public string? BarcodeResolutionStatus { get; set; }
    public string? BarcodeResolutionNote { get; set; }
    public string? AllBarcodes { get; set; }              // JSON-serialised List<LiquiMolyBarcodeRowDto>
    public string? SapUomInfo { get; set; }               // JSON-serialised LiquiMolySapUomInfoDto

    // =============================
    // APPROVALS & SPECIFICATIONS
    // =============================
    /// <summary>OEM / industry approvals as a JSON array (e.g. ["BMW Longlife-04","MB 229.51"]).</summary>
    public string? Approvals       { get; set; }          // JSON-serialised List<string>

    /// <summary>Technical specifications as a JSON object (e.g. {"Viscosity class":"SAE 5W-30"}).</summary>
    public string? Specifications  { get; set; }          // JSON-serialised Dictionary<string,string>

    /// <summary>Specification items as a JSON array (e.g. ["ACEA C3","API SP"]).</summary>
    public string? SpecificationItems { get; set; }       // JSON-serialised List<string>

    /// <summary>Overview properties / benefits as a JSON array.</summary>
    public string? OverviewProperties { get; set; }       // JSON-serialised List<string>

    /// <summary>Application / usage instructions as plain text.</summary>
    public string? Application { get; set; }

    /// <summary>LIQUI MOLY recommendations as a JSON array.</summary>
    public string? LiquiMolyRecommendations { get; set; } // JSON-serialised List<string>

    // =============================
    // DOWNLOADS
    // =============================
    /// <summary>URL to the English Production / Product Information PDF.</summary>
    public string? ProductInfoPdfUrl      { get; set; }

    /// <summary>URL to the English Safety Data Sheet PDF.</summary>
    public string? SafetyDataSheetPdfUrl  { get; set; }

    // =============================
    // EXTERNAL REFS
    // =============================
    public string? ProductUrl   { get; set; }

    // =============================
    // STATUS
    // =============================
    public bool    IsActive     { get; set; } = true;

    // =============================
    // SYSTEM
    // =============================
    public DateTime ScrapedAt   { get; set; }
}
