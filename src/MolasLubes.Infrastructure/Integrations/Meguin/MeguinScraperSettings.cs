using MolasLubes.Infrastructure.Integrations.LiquiMoly;

namespace MolasLubes.Infrastructure.Integrations.Meguin;

/// <summary>
/// Settings for the Meguin product scraper.
/// Inherits the same shape as <see cref="LiquiMolyScraperSettings"/> because
/// both sites are Magento 2 stores with identical HTML structure.
/// </summary>
public class MeguinScraperSettings : LiquiMolyScraperSettings
{
    public MeguinScraperSettings()
    {
        BaseUrl = "https://www.meguin.com";
        CategoryPaths = new()
        {
            { "/en/oils.html",      "Oils"      },
            { "/en/greases.html",   "Greases"   },
            { "/en/gear-oils.html", "Gear Oils" },
        };
    }
}
