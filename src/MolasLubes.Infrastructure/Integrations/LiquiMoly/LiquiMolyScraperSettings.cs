namespace MolasLubes.Infrastructure.Integrations.LiquiMoly;

public class LiquiMolyScraperSettings
{
    /// <summary>Base URL for Liqui-Moly website (no trailing slash).</summary>
    public string BaseUrl { get; set; } = "https://www.liqui-moly.com";

    /// <summary>Delay (ms) between page requests — be polite to the server.</summary>
    public int DelayBetweenRequestsMs { get; set; } = 1500;

    /// <summary>Delay (ms) between category sweeps.</summary>
    public int DelayBetweenCategoriesMs { get; set; } = 3000;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Optional hard-coded OWW API prefix (e.g. "/api/v2/oww/101/TZA/ENG/1").
    /// When empty the prefix is auto-detected from the fragment of the first
    /// oil-guide redirect (e.g. "#oww:/api/v2/oww/101/TZA/ENG/1/...").
    /// Only needed if auto-detection fails or the server is slow to redirect.
    /// </summary>
    public string OwwApiPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Relative paths (from BaseUrl) of product-catalog category pages to scrape.
    /// Key = path, Value = human-readable category name.
    ///
    /// The Liqui-Moly website uses Magento 2 with .html suffix routes.
    /// Each listing page includes JSON-LD structured data where the "sku" field
    /// contains packaging-variant article numbers (e.g. "1035") that match
    /// the ItemCode values in CacheProducts.
    /// </summary>
    public Dictionary<string, string> CategoryPaths { get; set; } = new()
    {
        { "/en/engine-oils.html",                  "Engine Oils"        },
        { "/en/gear-oils.html",                    "Gear Oils"          },
        { "/en/automatic-transmission-oils.html",  "Automatic Trans."   },
        { "/en/coolant.html",                      "Coolants"           },
        { "/en/brake-fluids.html",                 "Brake Fluids"       },
        { "/en/power-steering-fluids.html",        "Power Steering"     },
        { "/en/additives.html",                    "Additives"          },
        { "/en/motorbike-oils.html",               "Motorcycle"         },
        { "/en/greases.html",                      "Greases"            },
        { "/en/lubricants.html",                   "Special Lubricants" },
    };
}
