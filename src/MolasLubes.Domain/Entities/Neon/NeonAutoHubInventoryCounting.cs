namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubInventoryCounting
{
    public int      DocEntry  { get; set; }
    public int      DocNum    { get; set; }
    public DateTime CountDate { get; set; }
    public string?  Remarks   { get; set; }
    public DateTime? UpdatedInSap { get; set; }
    public DateTime SyncedAt  { get; set; }
    public List<NeonAutoHubInventoryCountingLine> Lines { get; set; } = new();
}
