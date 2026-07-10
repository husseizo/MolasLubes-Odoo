namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubPurchaseOrder
{
    public int      DocEntry    { get; set; }
    public int      DocNum      { get; set; }
    public string   CardCode    { get; set; } = null!;
    public string?  CardName    { get; set; }
    public DateTime DocDate     { get; set; }
    public DateTime? DocDueDate { get; set; }
    public string   DocStatus   { get; set; } = null!;
    public decimal  DocTotal    { get; set; }
    public string?  Comments    { get; set; }
    public DateTime? UpdatedInSap { get; set; }
    public DateTime SyncedAt    { get; set; }
    public List<NeonAutoHubPurchaseOrderLine> Lines { get; set; } = new();
}
