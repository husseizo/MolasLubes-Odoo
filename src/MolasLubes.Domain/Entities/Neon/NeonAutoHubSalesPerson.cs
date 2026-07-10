namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubSalesPerson
{
    public int SalesPersonCode { get; set; }
    public string SalesPersonName { get; set; } = null!;
    public bool IsActive { get; set; }
    public string? Email { get; set; }
    public DateTime SyncedAt { get; set; }
}
