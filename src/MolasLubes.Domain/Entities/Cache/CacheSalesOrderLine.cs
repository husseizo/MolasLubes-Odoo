namespace MolasLubes.Domain.Entities.Cache;

public class CacheSalesOrderLine
{
    public int Id { get; set; }

    // 🔗 FK → CacheSalesOrders.SapDocEntry
    public int SapDocEntry { get; set; }

    public CacheSalesOrder? SalesOrder { get; set; }

    public int LineNum { get; set; }

    public string ItemCode { get; set; } = null!;

    public string? ItemName { get; set; }

    public decimal Quantity { get; set; }

    public decimal Price { get; set; }

    public decimal LineTotal { get; set; }

    // =========================
    // 🏢 WAREHOUSE
    // =========================
    public string? WarehouseCode { get; set; }

    // =========================
    // 🔗 ODOO LINE UDF
    // =========================
    public string? OdooSalesOrderLineId { get; set; }
}
