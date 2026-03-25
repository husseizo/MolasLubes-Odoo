namespace MolasLubes.Infrastructure.Integrations.Germax.Dtos;

public class GermaxSeedDto
{
    public string  ItemCode      { get; set; } = string.Empty;
    public string  ItemName      { get; set; } = string.Empty;
    public string? EngineCode    { get; set; }
    public string  ItemGroupCode { get; set; } = string.Empty;
    public string  ItemGroupName { get; set; } = string.Empty;
}
