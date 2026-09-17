using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

namespace MolasLubes.Infrastructure.Integrations.SapB1.Profiles;

public class SapCompanyProfile
{
    public SapSettings Sap { get; set; } = new();
    public ProfileConnectionStrings ConnectionStrings { get; set; } = new();

    /// <summary>
    /// Controls how Liqui Moly items are identified when <c>U_MdlTEST</c> UDF is absent.
    /// "NumericCode" (default) → matches items whose ItemCode is purely numeric (AutoHub pattern).
    /// "AllActive"             → matches all non-frozen items (use when every item in the company is Liqui Moly).
    /// </summary>
    public string LiquiMolyFallbackStrategy { get; set; } = "NumericCode";
}

public class ProfileConnectionStrings
{
    public string CacheDb { get; set; } = string.Empty;
    public string NeonDb  { get; set; } = string.Empty;
}
