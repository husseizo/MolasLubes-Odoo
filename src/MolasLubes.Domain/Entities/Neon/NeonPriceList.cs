namespace MolasLubes.Domain.Entities.Neon;

public class NeonPriceList
{
    public long Id { get; set; }

    public string ItemCode { get; set; } = null!;
    public int PriceList { get; set; }

    public decimal Price { get; set; }

    // =========================
    // 🔗 ODOO UDFS (PRICE LIST)
    // =========================
    public string? OdooPricelistId { get; set; }   // U_Odoo_Pricelist_ID
    public string? OdooStatus { get; set; }        // U_Odoo_Status
    public string? OdooErrorMsg { get; set; }      // U_Odoo_ErrorMsg
    public string? OdooSyncDir { get; set; }       // U_Odoo_SyncDir
    public DateTime? OdooLastSync { get; set; }    // U_Odoo_LastSync

    // =========================
    // SYSTEM
    // =========================
    public DateTime SyncedAt { get; set; }
}