namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapInvoiceDto
{
    // SAP HEADER
    public int DocEntry { get; set; }
    public int DocNum { get; set; }

    public string CardCode { get; set; } = null!;
    public string CardName { get; set; } = null!;

    public DateTime DocDate { get; set; }
    public decimal DocTotal { get; set; }
    public decimal VatSum { get; set; }

    // 🔗 ODOO HEADER UDFS (OINV)
    public string? OdooInvoiceId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }

    // LINES
    public List<SapInvoiceLineDto> Lines { get; set; } = new();
}

public class SapInvoiceLineDto
{
    public string ItemCode { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public decimal GrossBuyPr { get; set; }

    public int BaseEntry { get; set; }   // ODLN / ORDR DocEntry
    public int BaseLine { get; set; }

    /// <summary>
    /// Parent sales order's U_Odoo_SO_ID (from ORDR when BaseEntry references ODLN).
    /// Extracted to enable fallback lookup when marking orders as delivered.
    /// </summary>
    public string? OdooParentSalesOrderId { get; set; }

    // 🔗 ODOO LINE UDFs (INV1)
    public string? OdooInvoiceLineId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
}