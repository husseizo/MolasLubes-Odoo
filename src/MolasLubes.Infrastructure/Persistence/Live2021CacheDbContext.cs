using Microsoft.EntityFrameworkCore;
using MolasLubes.Domain.Entities.Cache;

namespace MolasLubes.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the MOLAS_Live_2021_Cache SQL Server database (Profile B).
/// Dedicated to AutoHub / Germax entities — never shares tables with MolasCacheDbContext.
/// </summary>
public class Live2021CacheDbContext : DbContext
{
    public Live2021CacheDbContext(DbContextOptions<Live2021CacheDbContext> options)
        : base(options)
    {
    }

    public DbSet<CacheGermaxProduct> GermaxProducts => Set<CacheGermaxProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // =====================================================
        // GERMAX PRODUCTS
        // =====================================================
        modelBuilder.Entity<CacheGermaxProduct>(entity =>
        {
            entity.ToTable("CacheGermaxProducts");

            entity.HasKey(x => x.ItemCode);

            entity.Property(x => x.ItemCode)
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(x => x.ItemName)
                  .HasMaxLength(255)
                  .IsRequired();

            entity.Property(x => x.ItemGroupName).HasMaxLength(100);
            entity.Property(x => x.EngineCode).HasMaxLength(100);
            entity.Property(x => x.GermaxArticleNumber).HasMaxLength(50);
            entity.Property(x => x.OemPartNumber).HasMaxLength(255);
            entity.Property(x => x.PartsCatalog);
            entity.Property(x => x.ProductUrl).HasMaxLength(500);
            entity.Property(x => x.ImageUrl).HasMaxLength(500);
            entity.Property(x => x.MatchMethod).HasMaxLength(50);
            entity.Property(x => x.MatchScore).HasColumnType("decimal(5,2)");
            entity.Property(x => x.ScrapeStatus).HasMaxLength(20);
            entity.Property(x => x.ScrapeError).HasMaxLength(1000);
            entity.Property(x => x.IsActive).HasDefaultValue(true);

            entity.HasIndex(x => x.GermaxArticleNumber)
                  .HasDatabaseName("IX_CacheGermaxProducts_GermaxArticleNumber");

            entity.HasIndex(x => x.ItemGroupName)
                  .HasDatabaseName("IX_CacheGermaxProducts_ItemGroupName");

            entity.HasIndex(x => x.EngineCode)
                  .HasDatabaseName("IX_CacheGermaxProducts_EngineCode");

            entity.HasIndex(x => x.ScrapedAt)
                  .HasDatabaseName("IX_CacheGermaxProducts_ScrapedAt");
        });

        base.OnModelCreating(modelBuilder);
    }
}
