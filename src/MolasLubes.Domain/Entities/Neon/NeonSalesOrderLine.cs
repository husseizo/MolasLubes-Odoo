namespace MolasLubes.Domain.Entities.Neon;

public class NeonSalesOrderLine
{
    public long Id { get; set; }

    // 🔗 FK → NeonSalesOrders.SapDocEntry
    public int SalesOrderEntry { get; set; }

    public NeonSalesOrder? SalesOrder { get; set; }

    // =========================
    // PRODUCT
    // =========================
    public string ItemCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal LineTotal { get; set; }

    // =========================
    // 🔗 ODOO LINE UDF
    // =========================
    public string? OdooSalesOrderLineId { get; set; }
}