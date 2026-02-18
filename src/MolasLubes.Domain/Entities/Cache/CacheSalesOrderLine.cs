namespace MolasLubes.Domain.Entities.Cache;

public class CacheSalesOrderLine
{
    public int Id { get; set; }

    // 🔗 FK → CacheSalesOrders.SapDocEntry
    public int SapDocEntry { get; set; }

    public CacheSalesOrder? SalesOrder { get; set; }

    public string ItemCode { get; set; } = null!;
    public decimal Quantity { get; set; }

    // =========================
    // 🔗 ODOO LINE UDF
    // =========================
    public string? OdooSalesOrderLineId { get; set; }
}