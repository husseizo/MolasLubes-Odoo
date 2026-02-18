namespace MolasLubes.Domain.Entities.Cache;

public class CacheStockReservation
{
    public long Id { get; set; }

    public string ItemCode { get; set; } = null!;

    public decimal Quantity { get; set; }

    public string WarehouseCode { get; set; } = null!;

    public string? Reference { get; set; } // e.g. OrderNo / User / Odoo draft id
    public DateTime CreatedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }

    public string? OdooSalesOrderId { get; set; }

    public bool IsCommitted { get; set; }
    public int? SapDocEntry { get; set; }
}