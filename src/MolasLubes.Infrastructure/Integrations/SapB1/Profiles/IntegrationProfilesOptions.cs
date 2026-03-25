namespace MolasLubes.Infrastructure.Integrations.SapB1.Profiles;

public class IntegrationProfilesOptions
{
    public const string SectionName = "IntegrationProfiles";

    public string Default { get; set; } = "MolasLubes";
    public Dictionary<string, SapCompanyProfile> Profiles { get; set; } = new();
}
