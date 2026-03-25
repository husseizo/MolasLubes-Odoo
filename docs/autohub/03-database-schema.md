# AutoHub — Database Schema

## Databases

| Database | Engine | Purpose |
|---|---|---|
| `MOLAS_Live_2021_Cache` | SQL Server | Profile B cache — seed + scraped data |
| `MolasAutoHub` | Neon / PostgreSQL | Profile B Neon — final enriched products |

---

## Cache DB: `MOLAS_Live_2021_Cache`

### Table: `CacheGermaxProducts`

```sql
CREATE TABLE [dbo].[CacheGermaxProducts] (
    [ItemCode]             nvarchar(50)   NOT NULL,
    [ItemName]             nvarchar(255)  NOT NULL,
    [ItemGroupName]        nvarchar(100)  NULL,
    [EngineCode]           nvarchar(100)  NULL,
    [GermaxArticleNumber]  nvarchar(50)   NULL,
    [OemPartNumber]        nvarchar(255)  NULL,
    [FitForAuto]           nvarchar(max)  NULL,
    [Description]          nvarchar(max)  NULL,
    [ImageUrl]             nvarchar(500)  NULL,
    [AllImageUrls]         nvarchar(max)  NULL,
    [ProductUrl]           nvarchar(500)  NULL,
    [MatchMethod]          nvarchar(50)   NULL,
    [MatchScore]           decimal(5,2)   NULL,
    [ScrapedAt]            datetime2      NULL,
    [LastSapSeedAt]        datetime2      NOT NULL,
    [IsActive]             bit            NOT NULL DEFAULT 1,
    [ScrapeStatus]         nvarchar(20)   NULL,
    [ScrapeError]          nvarchar(1000) NULL,

    CONSTRAINT [PK_CacheGermaxProducts] PRIMARY KEY ([ItemCode])
);
```

### Indexes

```sql
CREATE INDEX [IX_CacheGermaxProducts_GermaxArticleNumber]
    ON [dbo].[CacheGermaxProducts] ([GermaxArticleNumber]);

CREATE INDEX [IX_CacheGermaxProducts_ItemGroupName]
    ON [dbo].[CacheGermaxProducts] ([ItemGroupName]);

CREATE INDEX [IX_CacheGermaxProducts_EngineCode]
    ON [dbo].[CacheGermaxProducts] ([EngineCode]);

CREATE INDEX [IX_CacheGermaxProducts_ScrapedAt]
    ON [dbo].[CacheGermaxProducts] ([ScrapedAt]);
```

### Column Reference

| Column | Type | Nullable | Description |
|---|---|---|---|
| `ItemCode` | nvarchar(50) | No (PK) | SAP item code |
| `ItemName` | nvarchar(255) | No | SAP item name |
| `ItemGroupName` | nvarchar(100) | Yes | SAP item group (e.g. Land Rover, Volvo) |
| `EngineCode` | nvarchar(100) | Yes | SAP `U_Engine_Code` UDF |
| `GermaxArticleNumber` | nvarchar(50) | Yes | Article number from Germax product page |
| `OemPartNumber` | nvarchar(255) | Yes | OEM part number from Germax |
| `FitForAuto` | nvarchar(max) | Yes | Compatibility list from Germax |
| `Description` | nvarchar(max) | Yes | Product description from Germax |
| `ImageUrl` | nvarchar(500) | Yes | Primary product image URL |
| `AllImageUrls` | nvarchar(max) | Yes | JSON array of all image URLs |
| `ProductUrl` | nvarchar(500) | Yes | Canonical Germax product page URL |
| `MatchMethod` | nvarchar(50) | Yes | Which search strategy produced the match |
| `MatchScore` | decimal(5,2) | Yes | Confidence score (0–100) |
| `ScrapedAt` | datetime2 | Yes | When the Germax scrape last ran |
| `LastSapSeedAt` | datetime2 | No | When SAP seed last upserted this row |
| `IsActive` | bit | No | `1` if SAP `frozenFor = 'N'` |
| `ScrapeStatus` | nvarchar(20) | Yes | `PENDING`, `SCRAPED`, `NO_MATCH`, `ERROR` |
| `ScrapeError` | nvarchar(1000) | Yes | Error detail when status is `ERROR` |

### ScrapeStatus Values

| Value | Meaning |
|---|---|
| `null` | Row is new from SAP seed, not yet attempted |
| `PENDING` | Queued for scraping |
| `SCRAPED` | Successfully matched and scraped |
| `NO_MATCH` | Searched but no candidate met the score threshold |
| `ERROR` | HTTP or parse failure; see `ScrapeError` |

---

## Entity Class: `CacheGermaxProduct`

**File:** `src/MolasLubes.Domain/Entities/Cache/CacheGermaxProduct.cs`

```csharp
public class CacheGermaxProduct
{
    public string   ItemCode            { get; set; } = string.Empty;
    public string   ItemName            { get; set; } = string.Empty;
    public string?  ItemGroupName       { get; set; }
    public string?  EngineCode          { get; set; }
    public string?  GermaxArticleNumber { get; set; }
    public string?  OemPartNumber       { get; set; }
    public string?  FitForAuto          { get; set; }
    public string?  Description         { get; set; }
    public string?  ImageUrl            { get; set; }
    public string?  AllImageUrls        { get; set; }
    public string?  ProductUrl          { get; set; }
    public string?  MatchMethod         { get; set; }
    public decimal? MatchScore          { get; set; }
    public DateTime? ScrapedAt          { get; set; }
    public DateTime LastSapSeedAt       { get; set; }
    public bool     IsActive            { get; set; } = true;
    public string?  ScrapeStatus        { get; set; }
    public string?  ScrapeError         { get; set; }
}
```

**EF Core configuration** (inside `Live2021CacheDbContext.OnModelCreating`):

```csharp
builder.Entity<CacheGermaxProduct>(e =>
{
    e.ToTable("CacheGermaxProducts");
    e.HasKey(x => x.ItemCode);
    e.Property(x => x.ItemCode).HasMaxLength(50);
    e.Property(x => x.ItemName).HasMaxLength(255).IsRequired();
    e.Property(x => x.ItemGroupName).HasMaxLength(100);
    e.Property(x => x.EngineCode).HasMaxLength(100);
    e.Property(x => x.GermaxArticleNumber).HasMaxLength(50);
    e.Property(x => x.OemPartNumber).HasMaxLength(255);
    e.Property(x => x.ProductUrl).HasMaxLength(500);
    e.Property(x => x.ImageUrl).HasMaxLength(500);
    e.Property(x => x.MatchMethod).HasMaxLength(50);
    e.Property(x => x.MatchScore).HasColumnType("decimal(5,2)");
    e.Property(x => x.ScrapeStatus).HasMaxLength(20);
    e.Property(x => x.ScrapeError).HasMaxLength(1000);
    e.Property(x => x.IsActive).HasDefaultValue(true);

    e.HasIndex(x => x.GermaxArticleNumber).HasDatabaseName("IX_CacheGermaxProducts_GermaxArticleNumber");
    e.HasIndex(x => x.ItemGroupName).HasDatabaseName("IX_CacheGermaxProducts_ItemGroupName");
    e.HasIndex(x => x.EngineCode).HasDatabaseName("IX_CacheGermaxProducts_EngineCode");
    e.HasIndex(x => x.ScrapedAt).HasDatabaseName("IX_CacheGermaxProducts_ScrapedAt");
});
```

---

## Neon DB: `MolasAutoHub`

### Table: `neon_germax_products`

```sql
CREATE TABLE neon_germax_products (
    item_code             varchar(50)     NOT NULL,
    item_name             text            NOT NULL,
    item_group_name       varchar(100),
    engine_code           varchar(100),
    germax_article_number varchar(50),
    oem_part_number       text,
    fit_for_auto          text,
    description           text,
    image_url             varchar(500),
    all_image_urls        text,
    product_url           varchar(500),
    match_method          varchar(50),
    match_score           numeric(5,2),
    scraped_at            timestamptz,
    last_sap_seed_at      timestamptz     NOT NULL,
    is_active             boolean         NOT NULL DEFAULT TRUE,
    scrape_status         varchar(20),
    scrape_error          varchar(1000),

    CONSTRAINT pk_neon_germax_products PRIMARY KEY (item_code)
);
```

### Indexes

```sql
CREATE INDEX ix_neon_germax_products_germax_article_number
    ON neon_germax_products (germax_article_number);

CREATE INDEX ix_neon_germax_products_item_group_name
    ON neon_germax_products (item_group_name);

CREATE INDEX ix_neon_germax_products_engine_code
    ON neon_germax_products (engine_code);

CREATE INDEX ix_neon_germax_products_scraped_at
    ON neon_germax_products (scraped_at);
```

---

## Entity Class: `NeonGermaxProduct`

**File:** `src/MolasLubes.Domain/Entities/Neon/NeonGermaxProduct.cs`

```csharp
public class NeonGermaxProduct
{
    public string   ItemCode            { get; set; } = string.Empty;
    public string   ItemName            { get; set; } = string.Empty;
    public string?  ItemGroupName       { get; set; }
    public string?  EngineCode          { get; set; }
    public string?  GermaxArticleNumber { get; set; }
    public string?  OemPartNumber       { get; set; }
    public string?  FitForAuto          { get; set; }
    public string?  Description         { get; set; }
    public string?  ImageUrl            { get; set; }
    public string?  AllImageUrls        { get; set; }
    public string?  ProductUrl          { get; set; }
    public string?  MatchMethod         { get; set; }
    public decimal? MatchScore          { get; set; }
    public DateTime? ScrapedAt          { get; set; }
    public DateTime LastSapSeedAt       { get; set; }
    public bool     IsActive            { get; set; } = true;
    public string?  ScrapeStatus        { get; set; }
    public string?  ScrapeError         { get; set; }
}
```

**EF Core configuration** (inside `AutoHubDbContext.OnModelCreating`):

```csharp
builder.Entity<NeonGermaxProduct>(e =>
{
    e.ToTable("neon_germax_products");
    e.HasKey(x => x.ItemCode);
    e.Property(x => x.ItemCode).HasColumnName("item_code").HasMaxLength(50);
    e.Property(x => x.ItemName).HasColumnName("item_name").IsRequired();
    e.Property(x => x.ItemGroupName).HasColumnName("item_group_name").HasMaxLength(100);
    e.Property(x => x.EngineCode).HasColumnName("engine_code").HasMaxLength(100);
    e.Property(x => x.GermaxArticleNumber).HasColumnName("germax_article_number").HasMaxLength(50);
    e.Property(x => x.OemPartNumber).HasColumnName("oem_part_number");
    e.Property(x => x.FitForAuto).HasColumnName("fit_for_auto");
    e.Property(x => x.Description).HasColumnName("description");
    e.Property(x => x.ImageUrl).HasColumnName("image_url").HasMaxLength(500);
    e.Property(x => x.AllImageUrls).HasColumnName("all_image_urls");
    e.Property(x => x.ProductUrl).HasColumnName("product_url").HasMaxLength(500);
    e.Property(x => x.MatchMethod).HasColumnName("match_method").HasMaxLength(50);
    e.Property(x => x.MatchScore).HasColumnName("match_score").HasColumnType("numeric(5,2)");
    e.Property(x => x.ScrapedAt).HasColumnName("scraped_at");
    e.Property(x => x.LastSapSeedAt).HasColumnName("last_sap_seed_at");
    e.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
    e.Property(x => x.ScrapeStatus).HasColumnName("scrape_status").HasMaxLength(20);
    e.Property(x => x.ScrapeError).HasColumnName("scrape_error").HasMaxLength(1000);

    e.HasIndex(x => x.GermaxArticleNumber).HasDatabaseName("ix_neon_germax_products_germax_article_number");
    e.HasIndex(x => x.ItemGroupName).HasDatabaseName("ix_neon_germax_products_item_group_name");
    e.HasIndex(x => x.EngineCode).HasDatabaseName("ix_neon_germax_products_engine_code");
    e.HasIndex(x => x.ScrapedAt).HasDatabaseName("ix_neon_germax_products_scraped_at");
});
```

---

## EF Core Migrations

Each context needs its own migration project or migration folder:

```bash
# Cache DB migrations
dotnet ef migrations add InitGermaxCache \
  --context Live2021CacheDbContext \
  --output-dir Persistence/Migrations/Live2021Cache \
  --project src/MolasLubes.Infrastructure

# Neon DB migrations
dotnet ef migrations add InitGermaxAutoHub \
  --context AutoHubDbContext \
  --output-dir Persistence/Migrations/AutoHub \
  --project src/MolasLubes.Infrastructure
```
