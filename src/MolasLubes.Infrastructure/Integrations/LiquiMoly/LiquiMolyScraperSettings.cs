public class LiquiMolyScraperSettings
{
    public string BaseUrl { get; set; } = "https://www.liqui-moly.com";

    public int DelayBetweenCategoriesMs { get; set; } = 1200;

    public int MaxParallelRequests { get; set; } = 6;

    public int DelayBetweenRequestsMs { get; set; } = 250;

    // Optional:
    public int HttpTimeoutSeconds { get; set; } = 30;

    public Dictionary<string, string> CategoryPaths { get; set; } = new()
    {
        { "/en/engine-oils.html","Engine Oils"},
        { "/en/gear-oils.html","Gear Oils"},
        { "/en/additives.html","Additives"},
        { "/en/brake-fluids.html","Brake Fluids"},
        { "/en/coolant.html","Coolant"}
    };
}