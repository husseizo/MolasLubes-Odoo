namespace MolasLubes.Domain.Entities.Cache;

/// <summary>
/// One item line in a Liqui Moly replenishment request.
/// Stores both the analyzer snapshot and the approval/execution outcome.
/// </summary>
public class CacheLiquiMolyReplenishmentRequestLine
{
    public int Id        { get; set; }
    public int RequestId { get; set; }

    // Item identity
    public string  SourceItemCode { get; set; } = null!;  // LUB1000xx in Molas_Lubes_LTD
    public string  TargetItemCode { get; set; } = null!;  // 3682 in MOLAS_Live_2021
    public string  ArticleNumber  { get; set; } = null!;  // same as TargetItemCode
    public string? ItemName       { get; set; }

    // Demand snapshot at draft time
    public decimal CurrentStockTarget    { get; set; }  // AutoHub stock at draft time
    public decimal AvailableSupplierStock { get; set; } // MolasLubes available at draft time
    public decimal QtySold30d            { get; set; }
    public decimal QtySold60d         { get; set; }
    public decimal QtySold90d         { get; set; }
    public decimal AvgDailySales30d   { get; set; }
    public decimal DaysOfStock        { get; set; }
    public decimal SuggestedQty       { get; set; }
    public string? TrendCategory      { get; set; }
    public int     Priority           { get; set; }

    // Approval override (null = use SuggestedQty)
    public decimal? ApprovedQty { get; set; }

    // Execution outcome per line
    public string  ExecutionStatus  { get; set; } = "PENDING";
    public string? ExecutionMessage { get; set; }

    public CacheLiquiMolyReplenishmentRequest Request { get; set; } = null!;
}
