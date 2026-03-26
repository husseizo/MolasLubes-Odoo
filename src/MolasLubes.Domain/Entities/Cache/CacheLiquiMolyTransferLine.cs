namespace MolasLubes.Domain.Entities.Cache;

public class CacheLiquiMolyTransferLine
{
    public int    Id          { get; set; }
    public int    TransferId  { get; set; }

    public string SourceItemCode { get; set; } = null!;
    public string TargetItemCode { get; set; } = null!;
    public string ArticleNumber  { get; set; } = null!;  // Liqui Moly article no. (U_Item_Name)

    public string? SourceItemName { get; set; }
    public string? TargetItemName { get; set; }

    public decimal Quantity { get; set; }

    public string  Status       { get; set; } = "PENDING";
    public string? ErrorMessage { get; set; }

    public CacheLiquiMolyTransfer Transfer { get; set; } = null!;
}
