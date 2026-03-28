namespace MolasLubes.Domain.Entities.Cache;

public class CacheGermaxProduct
{
    // =============================
    // IDENTITY (from SAP seed)
    // =============================
    public string  ItemCode       { get; set; } = null!;
    public string  ItemName       { get; set; } = null!;
    public string? ItemGroupName  { get; set; }
    public string? EngineCode     { get; set; }

    // =============================
    // GERMAX ENRICHMENT
    // =============================
    public string? GermaxArticleNumber { get; set; }
    public string? OemPartNumber       { get; set; }
    public string? PartsCatalog        { get; set; }   // JSON array of all OEM part numbers
    public string? FitForAuto          { get; set; }
    public string? Description         { get; set; }
    public string? ImageUrl            { get; set; }
    public string? AllImageUrls        { get; set; }   // JSON array of strings
    public string? ProductUrl          { get; set; }

    // =============================
    // MATCH METADATA
    // =============================
    public string?  MatchMethod  { get; set; }   // item_code | item_name_engine_code | item_name_only
    public decimal? MatchScore   { get; set; }

    // =============================
    // TIMESTAMPS
    // =============================
    public DateTime? ScrapedAt    { get; set; }
    public DateTime  LastSapSeedAt { get; set; }

    // =============================
    // STATUS
    // =============================
    public bool    IsActive     { get; set; } = true;
    public string? ScrapeStatus { get; set; }   // null | PENDING | SCRAPED | NO_MATCH | ERROR
    public string? ScrapeError  { get; set; }
}
