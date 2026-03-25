# AutoHub — Implementation Plan (Class Changes)

This document lists every new file and class required to implement Profile B. All paths are relative to `src/`.

---

## 1. Profile Configuration Classes

### New Files

| File | Namespace | Purpose |
|---|---|---|
| `MolasLubes.Infrastructure/Integrations/SapB1/Profiles/SapCompanyProfile.cs` | `MolasLubes.Infrastructure.Integrations.SapB1.Profiles` | Holds per-company SAP credentials and connection strings |
| `MolasLubes.Infrastructure/Integrations/SapB1/Profiles/IntegrationProfilesOptions.cs` | `MolasLubes.Infrastructure.Integrations.SapB1.Profiles` | Root options bound from `IntegrationProfiles` config section |
| `MolasLubes.Infrastructure/Integrations/Germax/GermaxScraperSettings.cs` | `MolasLubes.Infrastructure.Integrations.Germax` | Scraper tuning: base URL, delays, concurrency, item groups |

### Class Shapes

```csharp
// SapCompanyProfile.cs
public class SapCompanyProfile
{
    public string ProfileName  { get; set; } = string.Empty;
    public SapSettings Sap     { get; set; } = new();
    public ProfileConnectionStrings ConnectionStrings { get; set; } = new();
}

public class ProfileConnectionStrings
{
    public string CacheDb { get; set; } = string.Empty;
    public string NeonDb  { get; set; } = string.Empty;
}

// IntegrationProfilesOptions.cs
public class IntegrationProfilesOptions
{
    public const string SectionName = "IntegrationProfiles";

    public string Default { get; set; } = "MolasLubes";
    public Dictionary<string, SapCompanyProfile> Profiles { get; set; } = new();
}

// GermaxScraperSettings.cs
public class GermaxScraperSettings
{
    public const string SectionName = "GermaxScraper";

    public string BaseUrl                     { get; set; } = "https://germaxparts.com";
    public int    RequestTimeoutSeconds        { get; set; } = 30;
    public int    DelayBetweenRequestsMs       { get; set; } = 1500;
    public int    MaxConcurrency               { get; set; } = 1;
    public List<string> SearchPaths            { get; set; } = new();
    public List<string> AllowedItemGroups      { get; set; } = new();
    public List<string> SearchStrategyOrder    { get; set; } = new();
    public int    MaxCandidatesPerSearch        { get; set; } = 5;
}
```

---

## 2. SAP Connection Factory (Profile-Based)

Refactors the current single-company coupling in:
- `SapSettings.cs` (line 3)
- `Program.cs` (line 109, line 115)

### New Files

| File | Purpose |
|---|---|
| `MolasLubes.Infrastructure/Integrations/SapB1/DiApi/ISapCompanyConnectionFactory.cs` | Interface for resolving a live SAP connection by profile name |
| `MolasLubes.Infrastructure/Integrations/SapB1/DiApi/SapCompanyConnectionFactory.cs` | Concrete factory; holds one connection per named profile |
| `MolasLubes.Infrastructure/Integrations/SapB1/DiApi/SapCompanyConnection.cs` | Wraps a single company connection; takes a `SapCompanyProfile` |

### Class Shapes

```csharp
// ISapCompanyConnectionFactory.cs
public interface ISapCompanyConnectionFactory
{
    SapCompanyConnection GetConnection(string profileName);
}

// SapCompanyConnectionFactory.cs
public class SapCompanyConnectionFactory : ISapCompanyConnectionFactory
{
    private readonly IntegrationProfilesOptions _options;
    private readonly ILogger<SapCompanyConnectionFactory> _logger;
    // lazy-initialized connections per profile
    private readonly ConcurrentDictionary<string, SapCompanyConnection> _connections = new();

    public SapCompanyConnection GetConnection(string profileName) { ... }
}

// SapCompanyConnection.cs
public class SapCompanyConnection : IDisposable
{
    public SapCompanyProfile Profile { get; }
    // Wraps SAPbobsCOM.Company
    public SAPbobsCOM.Company Company { get; private set; }

    public SapCompanyConnection(SapCompanyProfile profile, ILogger logger) { ... }
    public void Connect()    { ... }
    public void Disconnect() { ... }
}
```

> **Backward compatibility:** Keep `SapDiApiConnection` untouched until all existing readers/writers are migrated to use the factory. Migrate one reader at a time. Remove `SapDiApiConnection` only after full migration.

---

## 3. Profile-Specific DB Contexts

Do **not** reuse the global `MolasCacheDbContext` or `NeonDbContext` registrations.

### New Files

| File | Context class | Target DB |
|---|---|---|
| `MolasLubes.Infrastructure/Persistence/Live2021CacheDbContext.cs` | `Live2021CacheDbContext` | `MOLAS_Live_2021_Cache` (SQL Server) |
| `MolasLubes.Infrastructure/Persistence/AutoHubDbContext.cs` | `AutoHubDbContext` | `MolasAutoHub` (Neon / PostgreSQL) |

Both contexts are **dedicated** (not inheriting from existing contexts). They contain only the Germax entities for now.

```csharp
// Live2021CacheDbContext.cs
public class Live2021CacheDbContext : DbContext
{
    public DbSet<CacheGermaxProduct> GermaxProducts => Set<CacheGermaxProduct>();
    protected override void OnModelCreating(ModelBuilder b) { ... }
}

// AutoHubDbContext.cs
public class AutoHubDbContext : DbContext
{
    public DbSet<NeonGermaxProduct> GermaxProducts => Set<NeonGermaxProduct>();
    protected override void OnModelCreating(ModelBuilder b) { ... }
}
```

---

## 4. SAP Seed Reader — AutoHub Profile

### New File

`MolasLubes.Infrastructure/Integrations/SapB1/DiApi/SapAutoHubSeedReader.cs`

Responsibilities:
- Connect to `MOLAS_Live_2021` via the profile factory
- Execute the seed query (`OITM JOIN OITB`)
- Filter `frozenFor = 'N'` and allowed item groups
- Return `IReadOnlyList<GermaxSeedDto>`
- Support an optional watermark date for delta reads

```csharp
public class SapAutoHubSeedReader
{
    public Task<IReadOnlyList<GermaxSeedDto>> ReadAllAsync(CancellationToken ct = default);
    public Task<IReadOnlyList<GermaxSeedDto>> ReadSinceAsync(DateTime watermark, CancellationToken ct = default);
}
```

---

## 5. Germax Scraper

### New Files

| File | Purpose |
|---|---|
| `MolasLubes.Infrastructure/Integrations/Germax/Dtos/GermaxSeedDto.cs` | Input from SAP seed reader |
| `MolasLubes.Infrastructure/Integrations/Germax/Dtos/GermaxCandidateDto.cs` | Search result candidate before scoring |
| `MolasLubes.Infrastructure/Integrations/Germax/Dtos/GermaxProductDto.cs` | Fully scraped product details |
| `MolasLubes.Infrastructure/Integrations/Germax/GermaxProductScraperService.cs` | Orchestrates search → score → scrape |

### GermaxSeedDto

```csharp
public class GermaxSeedDto
{
    public string ItemCode      { get; set; } = string.Empty;
    public string ItemName      { get; set; } = string.Empty;
    public string? EngineCode   { get; set; }
    public string ItemGroupCode { get; set; } = string.Empty;
    public string ItemGroupName { get; set; } = string.Empty;
}
```

### GermaxProductDto

```csharp
public class GermaxProductDto
{
    public string  ItemCode             { get; set; } = string.Empty;
    public string? GermaxArticleNumber  { get; set; }
    public string? OemPartNumber        { get; set; }
    public string? FitForAuto           { get; set; }
    public string? Description          { get; set; }
    public string? ProductUrl           { get; set; }
    public string? ImageUrl             { get; set; }
    public string? AllImageUrls         { get; set; }
    public string? MatchMethod          { get; set; }
    public decimal MatchScore           { get; set; }
}
```

### GermaxProductScraperService — Method Surface

```csharp
public class GermaxProductScraperService
{
    Task<IReadOnlyList<GermaxCandidateDto>> SearchCandidatesAsync(GermaxSeedDto seed, CancellationToken ct);
    Task<GermaxCandidateDto?>               ResolveBestCandidateAsync(GermaxSeedDto seed, IReadOnlyList<GermaxCandidateDto> candidates);
    Task<GermaxProductDto?>                 ScrapeProductPageAsync(string url, CancellationToken ct);
    decimal                                 ComputeMatchScore(GermaxSeedDto seed, GermaxCandidateDto candidate);
    string                                  NormalizeText(string text);
}
```

---

## 6. Sync Services

### New Files

| File | Class | Responsibility |
|---|---|---|
| `MolasLubes.Infrastructure/Services/Sync/GermaxCacheSyncService.cs` | `GermaxCacheSyncService` | Upsert scraped data into `MOLAS_Live_2021_Cache` |
| `MolasLubes.Infrastructure/Services/Sync/GermaxAutoHubSyncService.cs` | `GermaxAutoHubSyncService` | Replicate enriched rows from cache to `MolasAutoHub` |

---

## 7. Orchestration Job

### New File

`MolasLubes.Infrastructure/Scheduling/Jobs/GermaxProductEnrichmentJob.cs`

Execution order within one job run:
1. Load cache rows with `ScrapeStatus` = null, `'PENDING'`, or `'ERROR'` (recent only)
2. Call `GermaxProductScraperService` for each seed
3. Call `GermaxCacheSyncService` to persist to cache DB
4. Call `GermaxAutoHubSyncService` to replicate to Neon

---

## 8. Optional Read API

Split into two controllers to keep read access and admin triggers under separate authentication boundaries.

### Read Controller

**File:** `MolasLubes.Api/Controllers/AutoHub/AutoHubGermaxProductsController.cs`

No `ApiKeyAttribute` on the class — apply whatever auth level the rest of the read API uses (e.g. none, or a lighter read key).

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/autohub/germax/products` | Paginated list of enriched products |
| `GET` | `/api/autohub/germax/products/{itemCode}` | Single product detail |
| `GET` | `/api/autohub/germax/products/pending` | Items awaiting scraping |

### Admin Controller

**File:** `MolasLubes.Api/Controllers/AutoHub/AutoHubAdminController.cs`

Decorated with `[ApiKey]` at the class level — same `ApiKeyAttribute` used by the existing `AdminSyncController`.

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/admin/autohub/seed-sync` | Trigger SAP seed sync manually |
| `POST` | `/api/admin/autohub/germax/scrape` | Trigger Germax enrichment manually |
| `POST` | `/api/admin/autohub/germax/retry-failed` | Retry ERROR / NO_MATCH rows |

---

## 9. Entity Files

See [03-database-schema.md](03-database-schema.md) for full column definitions.

| File | Class | DB |
|---|---|---|
| `MolasLubes.Domain/Entities/Cache/CacheGermaxProduct.cs` | `CacheGermaxProduct` | `MOLAS_Live_2021_Cache` |
| `MolasLubes.Domain/Entities/Neon/NeonGermaxProduct.cs` | `NeonGermaxProduct` | `MolasAutoHub` |

---

## 10. DI Registration Summary

Register in `Program.cs` (or a dedicated extension method `AddAutoHubProfile`):

```csharp
// Options
services.Configure<IntegrationProfilesOptions>(
    configuration.GetSection(IntegrationProfilesOptions.SectionName));
services.Configure<GermaxScraperSettings>(
    configuration.GetSection(GermaxScraperSettings.SectionName));

// SAP factory
services.AddSingleton<ISapCompanyConnectionFactory, SapCompanyConnectionFactory>();

// DB contexts — scoped, named connection strings from profile
services.AddDbContext<Live2021CacheDbContext>(options =>
    options.UseSqlServer(GetProfileConnectionString("AutoHub", "CacheDb")));
services.AddDbContext<AutoHubDbContext>(options =>
    options.UseNpgsql(GetProfileConnectionString("AutoHub", "NeonDb")));

// Services
services.AddScoped<SapAutoHubSeedReader>();
services.AddScoped<GermaxProductScraperService>();
services.AddScoped<GermaxCacheSyncService>();
services.AddScoped<GermaxAutoHubSyncService>();
```
