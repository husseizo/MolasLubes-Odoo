namespace MolasLubes.Application.Invoices;

public class CreateInvoiceDto
{
    public int DeliveryDocEntry { get; set; }        // required
    public string CardCode { get; set; } = null!;    // required
    public DateTime DocDate { get; set; }            // required
    public string? NumAtCard { get; set; }           // external reference (optional)
    public string? OdooInvoiceId { get; set; }       // optional UDF if you want
}