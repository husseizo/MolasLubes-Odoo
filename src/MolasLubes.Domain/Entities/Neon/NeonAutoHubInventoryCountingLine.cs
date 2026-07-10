namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubInventoryCountingLine
{
    public long    Id         { get; set; }
    public int     DocEntry   { get; set; }
    public int     LineNum    { get; set; }
    public string  ItemCode   { get; set; } = null!;
    public string? WhsCode    { get; set; }
    public decimal CountedQty { get; set; }
    public NeonAutoHubInventoryCounting? InventoryCounting { get; set; }
}
