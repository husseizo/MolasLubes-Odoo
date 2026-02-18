namespace MolasLubes.Domain.Entities.Neon;

public class NeonDelivery
{
    public int SapDocEntry { get; set; }
    public int SapDocNum { get; set; }
    public bool IsCancelled { get; set; }

    // =========================
    // 🔗 ODOO / SAP SYNC
    // =========================
    public string? OdooDeliveryId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
    public string? OdooSyncDir { get; set; }

    public int SalesOrderEntry { get; set; }

    public string CardCode { get; set; } = null!;
    public DateTime DeliveryDate { get; set; }

    public int? BaseOrderEntry { get; set; }
    public decimal DeliveredQty { get; set; }
    public DateTime SyncedAt { get; set; }
}