using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SAPbobsCOM;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

namespace MolasLubes.Infrastructure.Services.LiquiMolyReplenishment;

/// <summary>
/// Calculates inter-company sales prices using Price List 5 (PL05) from MolasLubes company.
/// Formula: IF(ITM1.Price > 0, ITM1.Price, OITM.AvgPrice * 1.5)
/// Used for creating Sales Orders in MolasLubes when executing Liqui Moly replenishment via SO→PO→GR flow.
/// </summary>
public class PL05PricingCalculator
{
    private readonly SapDiApiConnection _molasLubesConnection;
    private readonly ILogger<PL05PricingCalculator> _logger;

    private const int PRICE_LIST_5 = 5;
    private const decimal MARKUP_MULTIPLIER = 1.5m;

    public PL05PricingCalculator(
        SapDiApiConnection molasLubesConnection,
        ILogger<PL05PricingCalculator> logger)
    {
        _molasLubesConnection = molasLubesConnection;
        _logger = logger;
    }

    /// <summary>
    /// Calculates prices for multiple items using PL05 pricing logic.
    /// Returns a dictionary of ItemCode → Price.
    /// Items not found or with errors will be excluded from the result.
    /// </summary>
    public Dictionary<string, decimal> CalculatePrices(IEnumerable<string> itemCodes)
    {
        var items = itemCodes.ToList();
        if (items.Count == 0)
        {
            _logger.LogWarning("PL05PricingCalculator: No item codes provided");
            return new Dictionary<string, decimal>();
        }

        _logger.LogInformation(
            "PL05PricingCalculator: Calculating prices for {Count} item(s) using Price List {PriceList}",
            items.Count, PRICE_LIST_5);

        var result = new Dictionary<string, decimal>();

        foreach (var itemCode in items)
        {
            try
            {
                var price = CalculatePrice(itemCode);
                if (price.HasValue)
                {
                    result[itemCode] = price.Value;
                    _logger.LogDebug(
                        "PL05PricingCalculator: {ItemCode} → {Price:F2}",
                        itemCode, price.Value);
                }
                else
                {
                    _logger.LogWarning(
                        "PL05PricingCalculator: Could not determine price for {ItemCode}",
                        itemCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "PL05PricingCalculator: Error calculating price for {ItemCode}",
                    itemCode);
            }
        }

        _logger.LogInformation(
            "PL05PricingCalculator: Calculated {SuccessCount}/{TotalCount} prices",
            result.Count, items.Count);

        return result;
    }

    /// <summary>
    /// Calculates price for a single item using PL05 pricing logic.
    /// Returns null if item not found or price cannot be determined.
    /// </summary>
    public decimal? CalculatePrice(string itemCode)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            _logger.LogWarning("PL05PricingCalculator: Empty itemCode provided");
            return null;
        }

        var company = _molasLubesConnection.GetConnectedCompany();
        Recordset? rs = null;

        try
        {
            rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

            // Query ITM1 (price list prices) and OITM (item master for AvgPrice fallback)
            // Formula: IF(ITM1.Price > 0, ITM1.Price, OITM.AvgPrice * 1.5)
            var sql = $@"
SELECT 
    OITM.ItemCode,
    OITM.ItemName,
    OITM.AvgPrice,
    ITM1.Price AS PL05Price
FROM OITM
LEFT JOIN ITM1 
    ON OITM.ItemCode = ITM1.ItemCode 
    AND ITM1.PriceList = {PRICE_LIST_5}
WHERE OITM.ItemCode = '{EscapeSql(itemCode)}'
";

            rs.DoQuery(sql);

            if (rs.EoF)
            {
                _logger.LogWarning(
                    "PL05PricingCalculator: Item {ItemCode} not found in OITM",
                    itemCode);
                return null;
            }

            var pl05PriceObj = rs.Fields.Item("PL05Price").Value;
            var avgPriceObj = rs.Fields.Item("AvgPrice").Value;

            // Convert PL05 price
            decimal? pl05Price = null;
            if (pl05PriceObj != null && pl05PriceObj != DBNull.Value)
            {
                pl05Price = Convert.ToDecimal(pl05PriceObj);
            }

            // Convert average price
            decimal? avgPrice = null;
            if (avgPriceObj != null && avgPriceObj != DBNull.Value)
            {
                avgPrice = Convert.ToDecimal(avgPriceObj);
            }

            // Apply pricing formula
            decimal finalPrice;

            if (pl05Price.HasValue && pl05Price.Value > 0)
            {
                // Use PL05 price if available and positive
                finalPrice = pl05Price.Value;
                _logger.LogDebug(
                    "PL05PricingCalculator: {ItemCode} using PL05 price: {Price:F2}",
                    itemCode, finalPrice);
            }
            else if (avgPrice.HasValue && avgPrice.Value > 0)
            {
                // Fallback to AvgPrice * 1.5
                finalPrice = avgPrice.Value * MARKUP_MULTIPLIER;
                _logger.LogDebug(
                    "PL05PricingCalculator: {ItemCode} using fallback (AvgPrice * {Multiplier}): {AvgPrice:F2} → {Price:F2}",
                    itemCode, MARKUP_MULTIPLIER, avgPrice.Value, finalPrice);
            }
            else
            {
                // No valid price found
                _logger.LogWarning(
                    "PL05PricingCalculator: {ItemCode} has no valid price (PL05={PL05}, AvgPrice={Avg})",
                    itemCode, pl05Price, avgPrice);
                return null;
            }

            return finalPrice;
        }
        catch (COMException comEx)
        {
            _logger.LogError(comEx,
                "PL05PricingCalculator: COM error calculating price for {ItemCode} | HRESULT: {HResult}",
                itemCode, comEx.HResult);
            throw;
        }
        finally
        {
            if (rs != null) Marshal.ReleaseComObject(rs);
        }
    }

    private static string EscapeSql(string value)
    {
        return value.Replace("'", "''");
    }
}
