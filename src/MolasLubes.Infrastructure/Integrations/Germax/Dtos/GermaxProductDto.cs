namespace MolasLubes.Infrastructure.Integrations.Germax.Dtos;

public class GermaxProductDto
{
    public string   ItemCode            { get; set; } = string.Empty;
    public string?  GermaxArticleNumber { get; set; }
    public string?  OemPartNumber       { get; set; }
    public string?  FitForAuto          { get; set; }
    public string?  Description         { get; set; }
    public string?  ProductUrl          { get; set; }
    public string?  ImageUrl            { get; set; }
    public string?  AllImageUrls        { get; set; }   // JSON array string
    public string?  MatchMethod         { get; set; }
    public decimal  MatchScore          { get; set; }
}
