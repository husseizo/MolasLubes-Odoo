namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubInvoice
{
    public int      DocEntry    { get; set; }
    public int      DocNum      { get; set; }
    public string   CardCode    { get; set; } = null!;
    public string?  CardName    { get; set; }
    public DateTime DocDate     { get; set; }
    public string   DocStatus   { get; set; } = null!;
    public decimal  DocTotal    { get; set; }
    public decimal  VatSum      { get; set; }
    public decimal  PaidToDate  { get; set; }
    public string?  Comments    { get; set; }
    public DateTime? UpdatedInSap { get; set; }
    public DateTime SyncedAt    { get; set; }
    public List<NeonAutoHubInvoiceLine> Lines { get; set; } = new();
}
