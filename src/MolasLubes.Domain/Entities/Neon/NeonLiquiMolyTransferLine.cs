namespace MolasLubes.Domain.Entities.Neon;

/// <summary>
/// One line of a <see cref="NeonLiquiMolyTransferHeader"/> (SAP WTR1 or OWTQ1 row).
/// </summary>
public class NeonLiquiMolyTransferLine
{
    public long   Id          { get; set; }   // serial surrogate PK

    public int    DocEntry    { get; set; }   // FK part 1 → header
    public string DocType     { get; set; } = null!;  // FK part 2 → header

    public int    LineNum     { get; set; }

    public string  ItemCode    { get; set; } = null!;
    public string? Description { get; set; }   // Dscription in SAP

    public decimal Quantity    { get; set; }
    public decimal OpenQty     { get; set; }   // remaining unfulfilled qty (0 for OWTR, real for OWTQ)

    public string? UomCode     { get; set; }
    public string? FromWhsCode { get; set; }
    public string? ToWhsCode   { get; set; }

    public decimal? Price      { get; set; }   // unit price
    public decimal? LineTotal  { get; set; }   // line total value

    // Base document link (for OWTR that fulfils an OWTQ)
    public int? BaseType  { get; set; }   // SAP object type of the base doc (67 = OWTQ)
    public int? BaseEntry { get; set; }   // DocEntry of the OWTQ
    public int? BaseLine  { get; set; }   // LineNum in the OWTQ

    public NeonLiquiMolyTransferHeader? Transfer { get; set; }
}
