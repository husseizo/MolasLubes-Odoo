using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

namespace MolasLubes.Infrastructure.Integrations.SapB1.Profiles;

public class SapCompanyProfile
{
    public SapSettings Sap { get; set; } = new();
    public ProfileConnectionStrings ConnectionStrings { get; set; } = new();
}

public class ProfileConnectionStrings
{
    public string CacheDb { get; set; } = string.Empty;
    public string NeonDb  { get; set; } = string.Empty;
}
