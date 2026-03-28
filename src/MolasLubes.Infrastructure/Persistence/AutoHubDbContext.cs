using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MolasLubes.Domain.Entities.Neon;

namespace MolasLubes.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the Parts_Catalog PostgreSQL database (Profile B).
/// Dedicated to AutoHub / Germax entities — never shares tables with NeonDbContext.
/// </summary>
public class AutoHubDbContext : DbContext
{
    public AutoHubDbContext(DbContextOptions<AutoHubDbContext> options)
        : base(options)
    {
    }

    public DbSet<NeonGermaxProduct> GermaxProducts => Set<NeonGermaxProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // =====================================================
        // GLOBAL UTC ENFORCEMENT (Postgres-safe)
        // =====================================================
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc
                ? v
                : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
        );

        var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
            v => v == null
                ? v
                : v.Value.Kind == DateTimeKind.Utc
                    ? v
                    : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
            v => v == null
                ? v
                : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
        );

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                if (prop.ClrType == typeof(DateTime))
                    prop.SetValueConverter(utcConverter);

                if (prop.ClrType == typeof(DateTime?))
                    prop.SetValueConverter(nullableUtcConverter);
            }
        }

        // =====================================================
        // GERMAX PRODUCTS
        // =====================================================
        modelBuilder.Entity<NeonGermaxProduct>(e =>
        {
            e.ToTable("neon_germax_products");

            e.HasKey(x => x.ItemCode);

            e.Property(x => x.ItemCode)
             .HasColumnName("item_code")
             .HasMaxLength(50)
             .IsRequired();

            e.Property(x => x.ItemName)
             .HasColumnName("item_name")
             .IsRequired();

            e.Property(x => x.ItemGroupName)
             .HasColumnName("item_group_name")
             .HasMaxLength(100);

            e.Property(x => x.EngineCode)
             .HasColumnName("engine_code")
             .HasMaxLength(100);

            e.Property(x => x.GermaxArticleNumber)
             .HasColumnName("germax_article_number")
             .HasMaxLength(50);

            e.Property(x => x.OemPartNumber)
             .HasColumnName("oem_part_number");

            e.Property(x => x.PartsCatalog)
             .HasColumnName("parts_catalog");

            e.Property(x => x.FitForAuto)
             .HasColumnName("fit_for_auto");

            e.Property(x => x.Description)
             .HasColumnName("description");

            e.Property(x => x.ImageUrl)
             .HasColumnName("image_url")
             .HasMaxLength(500);

            e.Property(x => x.AllImageUrls)
             .HasColumnName("all_image_urls");

            e.Property(x => x.ProductUrl)
             .HasColumnName("product_url")
             .HasMaxLength(500);

            e.Property(x => x.MatchMethod)
             .HasColumnName("match_method")
             .HasMaxLength(50);

            e.Property(x => x.MatchScore)
             .HasColumnName("match_score")
             .HasColumnType("numeric(5,2)");

            e.Property(x => x.ScrapedAt)
             .HasColumnName("scraped_at");

            e.Property(x => x.LastSapSeedAt)
             .HasColumnName("last_sap_seed_at")
             .IsRequired();

            e.Property(x => x.IsActive)
             .HasColumnName("is_active")
             .HasDefaultValue(true);

            e.Property(x => x.ScrapeStatus)
             .HasColumnName("scrape_status")
             .HasMaxLength(20);

            e.Property(x => x.ScrapeError)
             .HasColumnName("scrape_error")
             .HasMaxLength(1000);

            e.HasIndex(x => x.GermaxArticleNumber)
             .HasDatabaseName("ix_neon_germax_products_germax_article_number");

            e.HasIndex(x => x.ItemGroupName)
             .HasDatabaseName("ix_neon_germax_products_item_group_name");

            e.HasIndex(x => x.EngineCode)
             .HasDatabaseName("ix_neon_germax_products_engine_code");

            e.HasIndex(x => x.ScrapedAt)
             .HasDatabaseName("ix_neon_germax_products_scraped_at");
        });

        base.OnModelCreating(modelBuilder);
    }
}
