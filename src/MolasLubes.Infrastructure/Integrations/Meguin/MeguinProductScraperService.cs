using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;

namespace MolasLubes.Infrastructure.Integrations.Meguin;

/// <summary>
/// Scraper for the Meguin product catalogue (www.meguin.com).
///
/// Meguin runs the same Magento 2 theme as Liqui-Moly, so all HTML parsing
/// logic is inherited from <see cref="LiquiMolyProductScraperService"/>.
/// The only differences are the base URL, category paths, and log prefix —
/// all handled via the overrides below.
/// </summary>
public class MeguinProductScraperService : LiquiMolyProductScraperService
{
    protected override string BrandKey  => "Meguin";
    protected override string LogPrefix => "[Meguin] ";

    public MeguinProductScraperService(
        HttpClient httpClient,
        IOptions<MeguinScraperSettings> settings,
        ILogger<MeguinProductScraperService> logger)
        : base(httpClient, settings.Value, logger) { }
}
