namespace MolasLubes.Application.Invoices;

public class CreateInvoiceDto
{
    // ── Delivery-based invoice (set DeliveryDocEntry > 0) ──
    public int DeliveryDocEntry { get; set; }        // 0 = standalone invoice

    // ── Standalone invoice (set DeliveryDocEntry = 0, provide Lines) ──
    public List<CreateInvoiceLineDto>? Lines { get; set; }

    // ── Required fields ──
    public string CardCode { get; set; } = null!;
    public DateTime DocDate { get; set; }

    // ── Optional header fields ──
    public DateTime? DocDueDate { get; set; }        // defaults to DocDate if null
    public int? PaymentGroupCode { get; set; }       // SAP payment terms code
    public string? Comments { get; set; }            // Remarks field
    public string? NumAtCard { get; set; }           // external reference
    public string? OdooInvoiceId { get; set; }       // Odoo UDF
}
