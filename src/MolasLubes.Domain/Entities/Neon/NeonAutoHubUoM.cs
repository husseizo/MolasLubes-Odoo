namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubUoM
{
    public int     UomEntry  { get; set; }
    public string  UomCode   { get; set; } = null!;
    public string? UomName   { get; set; }
    public int?    GroupEntry { get; set; }
}
