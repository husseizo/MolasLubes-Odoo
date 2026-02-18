namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapPaymentDto
{
    // SAP HEADER
    public int DocEntry { get; set; }
    public int DocNum { get; set; }

    public string CardCode { get; set; } = null!;
    public DateTime DocDate { get; set; }

    public decimal CashSum { get; set; }
    public decimal TransferSum { get; set; }
    public decimal CheckSum { get; set; }

    public decimal TotalPaid => CashSum + TransferSum + CheckSum;

    // 🔗 ODOO HEADER UDFS (ORCT)
    public string? OdooPaymentId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }

    // LINES
    public List<SapPaymentInvoiceDto> Invoices { get; set; } = new();
}

public class SapPaymentInvoiceDto
{
    public int InvoiceDocEntry { get; set; }
    public decimal SumApplied { get; set; }

    // 🔗 ODOO LINE UDF (RCT2)
    public string? OdooPaymentLineId { get; set; }
}