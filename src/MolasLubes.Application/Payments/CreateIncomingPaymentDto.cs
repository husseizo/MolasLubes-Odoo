namespace MolasLubes.Application.Payments;

public class CreateIncomingPaymentDto
{
    public string CardCode { get; set; } = null!;

    // Odoo mapping
    public string? ExternalPaymentId { get; set; }  // maps to U_Odoo_Payment_ID
    public string SyncDir { get; set; } = "TO_SAP"; // TO_SAP / FROM_SAP / BIDIR

    public DateTime? DocDate { get; set; } // if null -> today
    public string? Currency { get; set; }  // optional

    // payment means
    public decimal CashSum { get; set; }
    public decimal TransferSum { get; set; }
    public string? TransferAccount { get; set; }   // required when TransferSum > 0
    public string? TransferReference { get; set; } // optional
    public decimal CardSum { get; set; }
    public string? CardName { get; set; }           // required when CardSum > 0

    // invoices to apply
    public List<CreateIncomingPaymentInvoiceDto> Invoices { get; set; } = new();
}

public class CreateIncomingPaymentInvoiceDto
{
    public int InvoiceDocEntry { get; set; }
    public decimal SumApplied { get; set; }

    // Odoo line mapping (RCT2)
    public string? ExternalPaymentLineId { get; set; } // maps to U_Odoo_PayLine_ID
}