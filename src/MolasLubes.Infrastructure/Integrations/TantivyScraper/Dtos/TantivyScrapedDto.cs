namespace MolasLubes.Infrastructure.Integrations.TantivyScraper.Dtos;

public class TantivyScrapedDto
{
    public string  ItemCode         { get; set; } = string.Empty;
    public string? PartName         { get; set; }
    public string? Specifications   { get; set; } // JSON
    public string? ReferenceNumbers { get; set; } // JSON
    public string? Applications     { get; set; } // JSON
    public string? ProductUrl       { get; set; }
    public string? ImageUrl         { get; set; }
}
