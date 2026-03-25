namespace MolasLubes.Infrastructure.Integrations.Germax;

public class GermaxScraperSettings
{
    public const string SectionName = "GermaxScraper";

    public string BaseUrl                  { get; set; } = "https://germaxparts.com";
    public int    RequestTimeoutSeconds    { get; set; } = 30;
    public int    DelayBetweenRequestsMs   { get; set; } = 1500;
    public int    MaxConcurrency           { get; set; } = 1;
    public List<string> SearchPaths        { get; set; } = new();
    public List<string> AllowedItemGroups  { get; set; } = new();
    public List<string> SearchStrategyOrder { get; set; } = new();
    public int    MaxCandidatesPerSearch   { get; set; } = 5;
}
