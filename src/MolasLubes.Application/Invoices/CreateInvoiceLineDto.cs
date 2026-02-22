namespace MolasLubes.Application.Invoices;

/// <summary>
/// A single line for standalone invoice creation (no base delivery).
/// </summary>
public class CreateInvoiceLineDto
{
    public string ItemCode { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Description { get; set; }
    public string? WarehouseCode { get; set; }
    public string? TaxCode { get; set; }
}
