namespace MolasLubes.Domain.Entities.Neon;

public class NeonDeliveryLine
{
    public long Id { get; set; }

    // 🔗 FK → NeonDeliveries.SapDocEntry
    public int DeliveryEntry { get; set; }

    public NeonDelivery? Delivery { get; set; }

    // SAP DLN1 natural position
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
