using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;

namespace MolasLubes.Infrastructure.Services.LiquiMolyReplenishment;

/// <summary>
/// Calculates inter-company sales prices from MolasLubes company using direct SQL.
/// Formula: IF(PL05 > 0, PL05, PL03 + ((PL01-PL03)/2))
/// Uses direct SqlConnection against the SAP SQL Server — no DI API, avoiding
/// COM global-state corruption when multiple companies are used in the same process.
/// </summary>
public class PL05PricingCalculator
{
    private readonly string _connectionString;
    private readonly ILogger<PL05PricingCalculator> _logger;

    private const decimal MARKUP_MULTIPLIER = 1.5m;

    public PL05PricingCalculator(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<PL05PricingCalculator> logger)
    {
        _logger = logger;

        var profiles = profileOptions.Value;
        if (!profiles.Profiles.TryGetValue(profiles.Default, out var molasLubesProfile))
            throw new InvalidOperationException(
                $"Default SAP profile '{profiles.Default}' not found in IntegrationProfiles.");

        var sap = molasLubesProfile.Sap;
        var sqlUser = sap.SqlUserName ?? sap.UserName;
        var sqlPass = sap.SqlPassword ?? sap.Password;
        _connectionString =
            $"Server={sap.Server};Database={sap.CompanyDB};User Id={sqlUser};Password={sqlPass};TrustServerCertificate=True;Connection Timeout=30;";
    }

    /// <summary>
    /// Calculates prices for multiple items. Returns ItemCode → Price for successfully priced items.
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
            "PL05PricingCalculator: Calculating prices for {Count} item(s)",
            items.Count);

        var result = new Dictionary<string, decimal>();

        foreach (var itemCode in items)
        {
            try
            {
                var price = CalculatePrice(itemCode);
                if (price.HasValue)
                {
                    result[itemCode] = price.Value;
                    _logger.LogDebug("PL05PricingCalculator: {ItemCode} → {Price:F2}", itemCode, price.Value);
                }
                else
                {
                    _logger.LogWarning("PL05PricingCalculator: Could not determine price for {ItemCode}", itemCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PL05PricingCalculator: Error calculating price for {ItemCode}", itemCode);
            }
        }

        _logger.LogInformation(
            "PL05PricingCalculator: Calculated {Success}/{Total} prices",
            result.Count, items.Count);

        return result;
    }

    /// <summary>
    /// Calculates price for a single item via direct SQL against Molas_Lubes_LTD.
    /// Returns null if item not found or no valid price exists.
    /// </summary>
    public decimal? CalculatePrice(string itemCode)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            _logger.LogWarning("PL05PricingCalculator: Empty itemCode provided");
            return null;
        }

        using var conn = new SqlConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT
    OITM.AvgPrice,
    PL01.Price AS PL01Price,
    PL03.Price AS PL03Price,
    PL05.Price AS PL05Price
FROM OITM
LEFT JOIN ITM1 PL01 ON OITM.ItemCode = PL01.ItemCode AND PL01.PriceList = 1
LEFT JOIN ITM1 PL03 ON OITM.ItemCode = PL03.ItemCode AND PL03.PriceList = 3
LEFT JOIN ITM1 PL05 ON OITM.ItemCode = PL05.ItemCode AND PL05.PriceList = 5
WHERE OITM.ItemCode = @ItemCode";
        cmd.Parameters.AddWithValue("@ItemCode", itemCode);

        using var reader = cmd.ExecuteReader();

        if (!reader.Read())
        {
            _logger.LogWarning("PL05PricingCalculator: Item {ItemCode} not found in OITM", itemCode);
            return null;
        }

        decimal? pl01 = reader.IsDBNull(reader.GetOrdinal("PL01Price")) ? null : reader.GetDecimal(reader.GetOrdinal("PL01Price"));
        decimal? pl03 = reader.IsDBNull(reader.GetOrdinal("PL03Price")) ? null : reader.GetDecimal(reader.GetOrdinal("PL03Price"));
        decimal? pl05 = reader.IsDBNull(reader.GetOrdinal("PL05Price")) ? null : reader.GetDecimal(reader.GetOrdinal("PL05Price"));

        if (pl05.HasValue && pl05.Value > 0)
        {
            _logger.LogDebug("PL05PricingCalculator: {ItemCode} using PL05={Price:F2}", itemCode, pl05.Value);
            return pl05.Value;
        }

        if (pl03.HasValue && pl03.Value > 0 && pl01.HasValue && pl01.Value > 0)
        {
            var price = pl03.Value + ((pl01.Value - pl03.Value) / 2);
            _logger.LogDebug(
                "PL05PricingCalculator: {ItemCode} using PL03+((PL01-PL03)/2) = {Price:F2}",
                itemCode, price);
            return price;
        }

        _logger.LogWarning(
            "PL05PricingCalculator: {ItemCode} has no valid price (PL05={PL05}, PL03={PL03}, PL01={PL01})",
            itemCode, pl05, pl03, pl01);
        return null;
    }

    private static string EscapeSql(string value) => value.Replace("'", "''");
}
