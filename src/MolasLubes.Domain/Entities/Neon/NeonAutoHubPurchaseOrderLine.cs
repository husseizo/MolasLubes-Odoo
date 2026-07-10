namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubPurchaseOrderLine
{
    public long    Id          { get; set; }
    public int     DocEntry    { get; set; }
    public int     LineNum     { get; set; }
    public string  ItemCode    { get; set; } = null!;
    public string? Description { get; set; }
    public decimal Quantity    { get; set; }
    public decimal OpenQty     { get; set; }
    public decimal Price       { get; set; }
    public decimal LineTotal   { get; set; }
    public string? WhsCode     { get; set; }
    public NeonAutoHubPurchaseOrder? PurchaseOrder { get; set; }
}
