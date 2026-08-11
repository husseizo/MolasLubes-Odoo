namespace MolasLubes.Domain.Entities.Neon;

public class NeonTantivyScraped
{
    public string    ItemCode         { get; set; } = null!;
    public string?   ArticleNo        { get; set; }
    public string?   Brand            { get; set; }  // VIKA | BORSEHUNG | DPA
    public string?   PartName         { get; set; }
    public string?   Specifications   { get; set; }  // JSON array of strings
    public string?   ReferenceNumbers { get; set; }  // JSON array of OEM refs
    public string?   Applications     { get; set; }  // JSON array of arrays (vehicle rows)
    public string?   ProductUrl       { get; set; }
    public string?   ImageUrl         { get; set; }
    public string    ScrapeStatus     { get; set; } = "PENDING"; // PENDING|SCRAPED|NO_MATCH|ERROR
    public string?   ScrapeError      { get; set; }
    public DateTime? ScrapedAt        { get; set; }
    public DateTime  LastSeedAt       { get; set; }
}
