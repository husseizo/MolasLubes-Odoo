using System.ComponentModel.DataAnnotations;

namespace MolasLubes.Domain.Entities.Cache;

public class CacheDeliveryLine
{
    public int Id { get; set; }

    // 🔗 FK → CacheDeliveries.SapDocEntry
    public int SapDocEntry { get; set; }

    public CacheDelivery? Delivery { get; set; }

    // SAP DLN1 natural position — used as merge key
    public int LineNum { get; set; }

    // =========================
    // PRODUCT
    // =========================
    public string ItemCode { get; set; } = null!;
    public string Description { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public decimal GrossBuyPr { get; set; }

    public int BaseEntry { get; set; }   // ORDR.DocEntry
    public int BaseLine { get; set; }

    // =========================
    // 🔗 ODOO LINE UDFs (DLN1)
    // =========================
    public string? OdooMoveId { get; set; }          // U_Odoo_Move_ID
    public string? OdooSalesOrderLineId { get; set; } // U_Odoo_SOLine_ID
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
}
