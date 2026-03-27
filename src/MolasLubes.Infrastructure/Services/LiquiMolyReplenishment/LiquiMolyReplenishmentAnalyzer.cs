using Microsoft.Extensions.Logging;
using MolasLubes.Application.LiquiMolyReplenishment;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

namespace MolasLubes.Infrastructure.Services.LiquiMolyReplenishment;

/// <summary>
/// Produces replenishment recommendations by combining:
///   - AutoHub (MOLAS_Live_2021) demand metrics (stock + sales history)
///   - MolasLubes (Molas_Lubes_LTD) article→ItemCode reverse map
///
/// Only items that have a corresponding source item in MolasLubes are included.
/// Items with SuggestedQty = 0 are still included in the full report but excluded
/// from draft request lines.
/// </summary>
public class LiquiMolyReplenishmentAnalyzer
{
    private readonly SapLiquiMolyDemandReader    _demandReader;
    private readonly SapLiquiMolySourceMapReader _sourceMapReader;
    private readonly ILogger<LiquiMolyReplenishmentAnalyzer> _logger;

    // Replenishment formula config
    private const int    TargetDaysDefault     = 30;
    private const decimal FastMovingThreshold  = 1m;  // avg daily units considered "fast"
    private const decimal SlowMovingThreshold  = 0.1m;

    public LiquiMolyReplenishmentAnalyzer(
        SapLiquiMolyDemandReader    demandReader,
        SapLiquiMolySourceMapReader sourceMapReader,
        ILogger<LiquiMolyReplenishmentAnalyzer> logger)
    {
        _demandReader    = demandReader;
        _sourceMapReader = sourceMapReader;
        _logger          = logger;
    }

    /// <summary>
    /// Analyzes demand in the target company and produces recommendation rows.
    /// </summary>
    /// <param name="sourceProfile">MolasLubes (supplier) profile key.</param>
    /// <param name="targetProfile">AutoHub (consumer) profile key.</param>
    /// <param name="targetWarehouse">AutoHub warehouse to check stock against.</param>
    /// <param name="targetDays">Number of coverage days for the suggested quantity formula.</param>
    public IReadOnlyList<LiquiMolyRecommendationRow> Analyze(
        string sourceProfile,
        string targetProfile,
        string targetWarehouse,
        int    targetDays = TargetDaysDefault)
    {
        // 1. Read all LM demand items from AutoHub
        var demandItems = _demandReader.ReadDemandItems(targetProfile, targetWarehouse);

        // 2. Read article-number → MolasLubes ItemCode map
        var sourceMap = _sourceMapReader.ReadArticleMap(sourceProfile);

        var rows = new List<LiquiMolyRecommendationRow>();

        foreach (var item in demandItems)
        {
            // In AutoHub, ItemCode IS the article number (e.g. "3682")
            var articleNumber = item.ItemCode;

            if (!sourceMap.TryGetValue(articleNumber, out var sourceItemCode))
            {
                _logger.LogDebug(
                    "Analyzer: skipping '{Article}' — no source item in {Profile}",
                    articleNumber, sourceProfile);
                continue;
            }

            var avgDailySales = item.QtySold30d / 30m;
            var daysOfStock   = avgDailySales > 0
                ? Math.Round(item.Available / avgDailySales, 2)
                : (item.Available > 0 ? 999m : 0m);

            var suggestedQty = avgDailySales > 0
                ? Math.Max(0m, Math.Ceiling(targetDays * avgDailySales - item.Available))
                : 0m;

            var (trend, priority) = Classify(
                item.Available, item.QtySold30d, item.QtySold60d, item.QtySold90d,
                avgDailySales, daysOfStock);

            rows.Add(new LiquiMolyRecommendationRow
            {
                SourceItemCode    = sourceItemCode,
                TargetItemCode    = item.ItemCode,
                ArticleNumber     = articleNumber,
                ItemName          = item.ItemName,
                CurrentStockTarget = item.Available,
                QtySold30d        = item.QtySold30d,
                QtySold60d        = item.QtySold60d,
                QtySold90d        = item.QtySold90d,
                AvgDailySales30d  = avgDailySales,
                DaysOfStock       = daysOfStock,
                SuggestedQty      = suggestedQty,
                TrendCategory     = trend,
                Priority          = priority
            });
        }

        var ordered = rows
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.SuggestedQty)
            .ToList();

        _logger.LogInformation(
            "Analyzer: {Total} rows ({Actionable} with SuggestedQty > 0)",
            ordered.Count, ordered.Count(r => r.SuggestedQty > 0));

        return ordered;
    }

    // ── Private ──────────────────────────────────────────

    private static (string trend, int priority) Classify(
        decimal available, decimal sold30, decimal sold60, decimal sold90,
        decimal avgDaily, decimal daysOfStock)
    {
        if (available <= 0 && sold90 == 0)
            return (LiquiMolyTrendCategory.Inactive, 6);

        if (sold90 == 0 && available > 0)
            return (LiquiMolyTrendCategory.DeadStock, 5);

        if (daysOfStock < 7)
            return (LiquiMolyTrendCategory.LowStock, 1);

        if (daysOfStock < 14 && avgDaily >= FastMovingThreshold)
            return (LiquiMolyTrendCategory.FastMoving, 2);

        if (avgDaily < SlowMovingThreshold && sold90 > 0)
            return (LiquiMolyTrendCategory.SlowMoving, 4);

        return (LiquiMolyTrendCategory.Normal, 3);
    }
}
