namespace MolasLubes.Application.LiquiMolyReplenishment;

/// <summary>
/// One item row from the replenishment analyzer.
/// Contains the demand snapshot, trend classification, and suggested transfer quantity.
/// </summary>
public class LiquiMolyRecommendationRow
{
    // Item identity
    public string  SourceItemCode { get; init; } = null!;  // LUB1000xx in MolasLubes
    public string  TargetItemCode { get; init; } = null!;  // 3682 in AutoHub
    public string  ArticleNumber  { get; init; } = null!;
    public string? ItemName       { get; init; }

    // Demand snapshot (from AutoHub / MOLAS_Live_2021)
    public decimal CurrentStockTarget { get; init; }
    public decimal QtySold30d         { get; init; }
    public decimal QtySold60d         { get; init; }
    public decimal QtySold90d         { get; init; }
    public decimal AvgDailySales30d   { get; init; }

    // Derived metrics
    public decimal DaysOfStock  { get; init; }
    public decimal SuggestedQty { get; init; }

    // Classification
    public string TrendCategory { get; init; } = LiquiMolyTrendCategory.Inactive;

    /// <summary>
    /// Lower = higher priority for replenishment action.
    /// 1=LowStock, 2=FastMoving, 3=Normal, 4=SlowMoving, 5=DeadStock, 6=Inactive
    /// </summary>
    public int Priority { get; init; }
}
