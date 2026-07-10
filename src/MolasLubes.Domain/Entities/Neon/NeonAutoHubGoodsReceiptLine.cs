namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubGoodsReceiptLine
{
    public long    Id          { get; set; }
    public int     DocEntry    { get; set; }
    public int     LineNum     { get; set; }
    public string  ItemCode    { get; set; } = null!;
    public string? Description { get; set; }
    public decimal Quantity    { get; set; }
    public string? WhsCode     { get; set; }
    public NeonAutoHubGoodsReceipt? GoodsReceipt { get; set; }
}
