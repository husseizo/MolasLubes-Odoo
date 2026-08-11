namespace MolasLubes.Infrastructure.Integrations.TantivyScraper.Dtos;

public class TantivySeedDto
{
    public string  ItemCode   { get; set; } = string.Empty;
    public string  ItemName   { get; set; } = string.Empty;
    public string  Brand      { get; set; } = string.Empty; // VIKA | BORSEHUNG | DPA
    public string? ArticleNo  { get; set; }
    public string? EngineCode { get; set; }
}
