namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapSalesOrderLineDto
{
    public int LineNum { get; set; }
    public string ItemCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal LineTotal { get; set; }

    // 🏢 WAREHOUSE
    public string? WarehouseCode { get; set; }

    // 🔗 ODOO LINE
    public string? OdooSalesOrderLineId { get; set; }
}