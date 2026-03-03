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
    /// Relative paths (from BaseUrl) of product-catalog category pages to scrape.
    /// Key = path, Value = human-readable category name.
    /// </summary>
    public Dictionary<string, string> CategoryPaths { get; set; } = new()
    {
        { "/en/products/engine-oils/",             "Engine Oils"        },
        { "/en/products/gear-oils/",               "Gear Oils"          },
        { "/en/products/automatic-transmission/",  "Automatic Trans."   },
        { "/en/products/coolant-antifreeze/",      "Coolants"           },
        { "/en/products/brake-fluids/",            "Brake Fluids"       },
        { "/en/products/power-steering/",          "Power Steering"     },
        { "/en/products/additives/",               "Additives"          },
        { "/en/products/motor-cycle/",             "Motorcycle"         },
        { "/en/products/greases/",                 "Greases"            },
        { "/en/products/special-lubricants/",      "Special Lubricants" },
    };
}
