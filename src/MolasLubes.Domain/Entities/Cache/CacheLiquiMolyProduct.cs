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
    public string? PackagingSize{ get; set; }             // e.g. "1 L", "5 L", "500 ml"

    // =============================
    // EXTERNAL REFS
    // =============================
    public string? ImageUrl     { get; set; }
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
