namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public static class SapCountryMapper
{
    private static readonly Dictionary<string, string> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "Tanzania", "TZ" },
            { "Kenya", "KE" },
            { "Uganda", "UG" },
            { "Rwanda", "RW" },
            { "Burundi", "BI" },
            { "United States", "US" },
            { "United Kingdom", "GB" }
        };

    public static string? ToSapCode(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
            return null;

        // Already looks like SAP code
        if (country.Length <= 3)
            return country.ToUpperInvariant();

        return Map.TryGetValue(country.Trim(), out var code)
            ? code
            : null;
    }
}