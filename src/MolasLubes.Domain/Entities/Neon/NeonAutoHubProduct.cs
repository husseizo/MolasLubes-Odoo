namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubProduct
{
    public string ItemCode  { get; set; } = null!;
    public string ItemName  { get; set; } = null!;
    public decimal OnHandSap      { get; set; }
    public decimal AvailableCache { get; set; }
    public bool   IsActive  { get; set; }
    public DateTime SyncedAt { get; set; }
}
