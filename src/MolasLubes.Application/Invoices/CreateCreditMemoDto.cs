namespace MolasLubes.Application.Invoices;

/// <summary>
/// Creates a credit note (ORIN) based on an existing invoice.
/// If Lines is null/empty, all lines from the base invoice are credited.
/// If Lines is provided, only those lines (by BaseLine index) are credited.
/// </summary>
public class CreateCreditMemoDto
{
    public int InvoiceDocEntry { get; set; }    // required — base OINV
    public string CardCode { get; set; } = null!;
    public DateTime DocDate { get; set; }
    public string? Comments { get; set; }
    public string? OdooCreditMemoId { get; set; }

    // Optional: partial credit by line. If empty, credit all lines from invoice.
    public List<CreditMemoLineDto>? Lines { get; set; }
}

public class CreditMemoLineDto
{
    public int BaseLine { get; set; }    // 0-based line index in the source invoice
    public decimal Quantity { get; set; }
}
