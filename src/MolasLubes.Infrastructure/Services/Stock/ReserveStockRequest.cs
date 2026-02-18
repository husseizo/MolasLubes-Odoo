namespace MolasLubes.Infrastructure.Services.Stock;

public class ReserveStockRequest
{
    public string ItemCode { get; set; } = null!;
    public string WarehouseCode { get; set; } = null!;
    public decimal Quantity { get; set; }
    public string? Reference { get; set; }
}