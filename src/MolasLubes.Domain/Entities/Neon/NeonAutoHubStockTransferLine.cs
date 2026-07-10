namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubStockTransferLine
{
    public long    Id           { get; set; }
    public int     DocEntry     { get; set; }
    public int     LineNum      { get; set; }
    public string  ItemCode     { get; set; } = null!;
    public string? Description  { get; set; }
    public decimal Quantity     { get; set; }
    public string? FromWhsCode  { get; set; }
    public string? ToWhsCode    { get; set; }
    public NeonAutoHubStockTransfer? StockTransfer { get; set; }
}
