namespace MolasLubes.Infrastructure.Integrations.LiquiMoly;

public class LiquiMolyProductDto
{
    public string  ArticleNumber { get; set; } = null!;
    public string  Name          { get; set; } = null!;
    public string? Category      { get; set; }
    public string? SubCategory   { get; set; }
    public string? Description   { get; set; }
    public string? SpecGrade     { get; set; }
    public string? PackagingSize { get; set; }
    public string? ImageUrl      { get; set; }
    public string? ProductUrl    { get; set; }
}
