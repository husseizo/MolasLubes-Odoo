namespace MolasLubes.Application.Payments;

public class CreatePaymentDto
{
    public int InvoiceDocEntry { get; set; }           // required
    public string CardCode { get; set; } = null!;      // required
    public DateTime DocDate { get; set; }              // required
    public string Currency { get; set; } = "TZS";

    public decimal Amount { get; set; }                // required

    // Means (choose one or mix)
    public decimal CashAmount { get; set; }            // optional
    public decimal TransferAmount { get; set; }        // optional
    public string? TransferAccount { get; set; }       // required if TransferAmount > 0
    public decimal CardAmount { get; set; }            // optional
    public string? CardName { get; set; }              // optional

    public string? ExternalRef { get; set; }           // optional (if you want to show in SAP)
}