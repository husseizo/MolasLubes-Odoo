namespace MolasLubes.Domain.Entities.Neon;

/// <summary>
/// Cache of a SAP inventory transfer or transfer request synced from Molas_Lubes_LTD.
/// DocType = "OWTR" → completed transfer (Goods Issue + Goods Receipt already posted).
/// DocType = "OWTQ" → transfer request (created on replenishment approval, awaiting fulfilment).
/// </summary>
public class NeonLiquiMolyTransferHeader
{
    public int    DocEntry      { get; set; }   // SAP DocEntry
    public string DocType       { get; set; } = null!;  // "OWTR" | "OWTQ"

    public int    DocNum        { get; set; }   // SAP human-readable number
    public string SourceProfile { get; set; } = null!;  // "MolasLubes" | "AutoHub"

    public DateTime  DocDate    { get; set; }   // document date
    public DateTime? TaxDate    { get; set; }   // posting date (TaxDate in SAP)
    public DateTime? DocDueDate { get; set; }   // required-by date

    public string  FromWhsCode  { get; set; } = null!;
    public string  ToWhsCode    { get; set; } = null!;

    /// <summary>SAP Remarks field — never omit on sync.</summary>
    public string? Comments     { get; set; }

    /// <summary>"O" = Open, "C" = Closed.</summary>
    public string  DocStatus    { get; set; } = "O";

    public int?    UserSign     { get; set; }   // SAP user who created the document
    public decimal DocTotal     { get; set; }   // total document value

    /// <summary>Our internal replenishment reference (REP-YYYYMMDD-NNNNNN) that triggered this document.</summary>
    public string? ReplenishmentRef { get; set; }

    public DateTime SyncedAt    { get; set; }

    public List<NeonLiquiMolyTransferLine> Lines { get; set; } = new();
}
