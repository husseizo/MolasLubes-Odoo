namespace MolasLubes.Domain.Entities.Cache;

public class CacheProduct
{
    // =============================
    // 🔑 IDENTITY
    // =============================
    public string ItemCode { get; set; } = null!;
    public string WarehouseCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;

    // =============================
    // STOCK SNAPSHOT (SAP)
    // =============================
    public decimal OnHandSap { get; set; }


    public string? Barcode { get; set; }   // ✅ NEW

    // =============================
    // STOCK BRAIN (CACHE)
    // =============================
    public decimal AvailableCache { get; set; }

    public bool IsActive { get; set; }

    // =============================
    // PRICE LIST SNAPSHOT (OPTIONAL)
    // =============================
    public decimal? PriceList_1 { get; set; }
    public decimal? PriceList_2 { get; set; }
    public decimal? PriceList_3 { get; set; }

    // =============================
    // 🔗 ODOO ↔ SAP TRACEABILITY (UDF MIRROR)
    // =============================
    public string? OdooProductId { get; set; }     // U_Odoo_Product_ID
    public string? OdooPricelistId { get; set; }
    public string? OdooStatus { get; set; }        // U_Odoo_Status
    public string? OdooErrorMsg { get; set; }      // U_Odoo_ErrorMsg
    public DateTime? OdooLastSync { get; set; }    // U_Odoo_LastSync
    public string? OdooSyncDir { get; set; }       // TO_SAP | FROM_SAP | BIDIR

    // =============================
    // SYNC TRACKING
    // =============================
    public DateTime LastSapSyncAt { get; set; }

    // =============================
    // CONCURRENCY (IMPORTANT 🔥)
    // =============================
    public byte[] RowVersion { get; set; } = null!;
}