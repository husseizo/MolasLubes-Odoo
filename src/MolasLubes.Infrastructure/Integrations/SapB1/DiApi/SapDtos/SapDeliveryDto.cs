namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapDeliveryDto
{
    // =========================
    // SAP HEADER (ODLN)
    // =========================
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
    public string CardCode { get; set; } = default!;
    public DateTime DocDate { get; set; }

    /// <summary>ODLN.UpdateDate — used for delta watermarking</summary>
    public DateTime SapUpdateDate { get; set; }

    /// <summary>ODLN.CANCELED = 'Y'</summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Convenience: BaseEntry of the first line (ORDR.DocEntry).
    /// Populated from Lines after reading; kept for backward compat.
    /// </summary>
    public int BaseOrderEntry { get; set; }

    /// <summary>Sum of all line quantities — computed from Lines.</summary>
    public decimal DeliveredQuantity { get; set; }

    /// <summary>
    /// Parent sales order's U_Odoo_SO_ID (from ORDR).
    /// Extracted to enable fallback lookup when marking orders as delivered.
    /// </summary>
    public string? OdooParentSalesOrderId { get; set; }

    // =========================
    // 🔗 ODOO HEADER UDFs (ODLN)
    // =========================
    public string? OdooDeliveryId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }

    // =========================
    // LINES (DLN1)
    // =========================
    public List<SapDeliveryLineDto> Lines { get; set; } = new();
}

public class SapDeliveryLineDto
{
    public int LineNum { get; set; }

    public string ItemCode { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public decimal GrossBuyPr { get; set; }

    public int BaseEntry { get; set; }   // ORDR.DocEntry
    public int BaseLine { get; set; }

    // 🔗 ODOO LINE UDFs (DLN1)
    public string? OdooMoveId { get; set; }          // U_Odoo_Move_ID
    public string? OdooSalesOrderLineId { get; set; } // U_Odoo_SOLine_ID
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
}
