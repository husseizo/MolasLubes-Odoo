namespace MolasLubes.Infrastructure.Integrations.TantivyScraper;

public class TantivyScraperSettings
{
    public const string SectionName = "TantivyScraper";

    public string VikaBaseUrl            { get; set; } = "https://catalogue.vikadpa.com";
    public string BorsehungBaseUrl       { get; set; } = "https://parts.borsehung.de/Search.cshtml";
    public bool   Headless               { get; set; } = true;
    public float  PageTimeoutMs          { get; set; } = 30_000;
    public float  SearchTimeoutMs        { get; set; } = 15_000;
    public int    DelayBetweenRequestsMs    { get; set; } = 5_000;
    public int    BatchSize                 { get; set; } = 20;
    public int    ConsecutiveErrorThreshold { get; set; } = 3;
    public int    RateLimitBackoffMs        { get; set; } = 120_000;
}
