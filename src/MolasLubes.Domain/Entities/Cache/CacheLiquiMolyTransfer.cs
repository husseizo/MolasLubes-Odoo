namespace MolasLubes.Domain.Entities.Cache;

/// <summary>
/// Audit header for a Liqui Moly internal stock transfer.
/// One transfer = one Goods Issue in source DB + one Goods Receipt in target DB.
/// </summary>
public class CacheLiquiMolyTransfer
{
    public int    Id           { get; set; }
    public string TransferRef  { get; set; } = null!;  // TRF-YYYYMMDD-NNNNNN

    public string SourceProfile   { get; set; } = null!;
    public string TargetProfile   { get; set; } = null!;
    public string SourceWarehouse { get; set; } = null!;
    public string TargetWarehouse { get; set; } = null!;

    public string? Comments { get; set; }

    // SAP document references
    public int?   GoodsIssueDocEntry  { get; set; }
    public string? GoodsIssueDocNum   { get; set; }
    public int?   GoodsReceiptDocEntry { get; set; }
    public string? GoodsReceiptDocNum  { get; set; }

    public string  Status       { get; set; } = "PENDING";
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public List<CacheLiquiMolyTransferLine> Lines { get; set; } = new();
}
