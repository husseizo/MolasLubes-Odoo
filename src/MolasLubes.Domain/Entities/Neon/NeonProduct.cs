namespace MolasLubes.Domain.Entities.Neon;

public class NeonProduct
{
    // =============================
    // 🔑 IDENTITY
    // =============================
    public string ItemCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public string? ForeignName { get; set; }

    // =============================
    // CLASSIFICATION
    // =============================
    public int? ItemGroupCode { get; set; }
    public string? ItemGroupName { get; set; }
    public string? Brand { get; set; }

    public string? UoM { get; set; }
    public string? DefaultWarehouse { get; set; }

    // =============================
    // STATUS
    // =============================
    public bool IsInventoryItem { get; set; }
    public bool IsActive { get; set; }

    // =============================
    // STOCK SNAPSHOT
    // =============================
    public decimal OnHandSap { get; set; }
    public decimal AvailableCache { get; set; }

    public string? Barcode { get; set; }

    // =============================
    // 🔗 ODOO ↔ SAP TRACEABILITY (UDF MIRROR)
    // =============================
    public string? OdooProductId { get; set; }     // U_Odoo_Product_ID
    public string? OdooStatus { get; set; }        // U_Odoo_Status
    public string? OdooErrorMsg { get; set; }      // U_Odoo_ErrorMsg
    public DateTime? OdooLastSync { get; set; }    // U_Odoo_LastSync
    public string? OdooSyncDir { get; set; }       // U_Odoo_SyncDir

    // =============================
    // SYSTEM
    // =============================
    public DateTime SyncedAt { get; set; }
}