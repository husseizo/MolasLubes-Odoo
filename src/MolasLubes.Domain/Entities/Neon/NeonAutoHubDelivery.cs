namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubDelivery
{
    public int      DocEntry    { get; set; }
    public int      DocNum      { get; set; }
    public string   CardCode    { get; set; } = null!;
    public string?  CardName    { get; set; }
    public DateTime DocDate     { get; set; }
    public string   DocStatus   { get; set; } = null!;
    public bool     IsCancelled { get; set; }
    public string?  Comments    { get; set; }
    public DateTime? UpdatedInSap { get; set; }
    public DateTime SyncedAt    { get; set; }
    public List<NeonAutoHubDeliveryLine> Lines { get; set; } = new();
}
