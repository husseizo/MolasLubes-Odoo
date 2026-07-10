namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubStockTransfer
{
    public int      DocEntry  { get; set; }
    public int      DocNum    { get; set; }
    public DateTime DocDate   { get; set; }
    public string?  Comments  { get; set; }
    public DateTime? UpdatedInSap { get; set; }
    public DateTime SyncedAt  { get; set; }
    public List<NeonAutoHubStockTransferLine> Lines { get; set; } = new();
}
