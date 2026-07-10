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

    public DbSet<NeonGermaxProduct>               GermaxProducts             => Set<NeonGermaxProduct>();
    public DbSet<NeonAutoHubProduct>              AutoHubProducts            => Set<NeonAutoHubProduct>();
    public DbSet<NeonAutoHubSyncState>            AutoHubSyncStates          => Set<NeonAutoHubSyncState>();
    public DbSet<NeonAutoHubDelivery>             AutoHubDeliveries          => Set<NeonAutoHubDelivery>();
    public DbSet<NeonAutoHubDeliveryLine>         AutoHubDeliveryLines       => Set<NeonAutoHubDeliveryLine>();
    public DbSet<NeonAutoHubSalesOrder>           AutoHubSalesOrders         => Set<NeonAutoHubSalesOrder>();
    public DbSet<NeonAutoHubSalesOrderLine>       AutoHubSalesOrderLines     => Set<NeonAutoHubSalesOrderLine>();
    public DbSet<NeonAutoHubInvoice>              AutoHubInvoices            => Set<NeonAutoHubInvoice>();
    public DbSet<NeonAutoHubInvoiceLine>          AutoHubInvoiceLines        => Set<NeonAutoHubInvoiceLine>();
    public DbSet<NeonAutoHubGoodsReceipt>         AutoHubGoodsReceipts       => Set<NeonAutoHubGoodsReceipt>();
    public DbSet<NeonAutoHubGoodsReceiptLine>     AutoHubGoodsReceiptLines   => Set<NeonAutoHubGoodsReceiptLine>();
    public DbSet<NeonAutoHubStockTransfer>        AutoHubStockTransfers      => Set<NeonAutoHubStockTransfer>();
    public DbSet<NeonAutoHubStockTransferLine>    AutoHubStockTransferLines  => Set<NeonAutoHubStockTransferLine>();
    public DbSet<NeonAutoHubInventoryCounting>    AutoHubInventoryCountings  => Set<NeonAutoHubInventoryCounting>();
    public DbSet<NeonAutoHubInventoryCountingLine> AutoHubInventoryCountingLines => Set<NeonAutoHubInventoryCountingLine>();
    public DbSet<NeonAutoHubPurchaseOrder>        AutoHubPurchaseOrders      => Set<NeonAutoHubPurchaseOrder>();
    public DbSet<NeonAutoHubPurchaseOrderLine>    AutoHubPurchaseOrderLines  => Set<NeonAutoHubPurchaseOrderLine>();
    public DbSet<NeonAutoHubUoM>                  AutoHubUoMs                => Set<NeonAutoHubUoM>();
    public DbSet<NeonAutoHubSalesPerson>          AutoHubSalesPersons        => Set<NeonAutoHubSalesPerson>();

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

        // =====================================================
        // AUTOHUB PRODUCTS (SAP B1 master items)
        // =====================================================
        modelBuilder.Entity<NeonAutoHubProduct>(e =>
        {
            e.ToTable("NeonAutoHubProducts");
            e.HasKey(x => x.ItemCode);
            e.Property(x => x.ItemName).IsRequired();
            e.Property(x => x.OnHandSap).HasPrecision(18, 2);
            e.Property(x => x.AvailableCache).HasPrecision(18, 2);
            e.Property(x => x.SyncedAt).IsRequired();
        });

        // =====================================================
        // AUTOHUB DELTA SYNC STATE
        // =====================================================
        modelBuilder.Entity<NeonAutoHubSyncState>(e =>
        {
            e.ToTable("NeonAutoHubSyncState");
            e.HasKey(x => x.DocType);
            e.Property(x => x.DocType).HasMaxLength(50).IsRequired();
            e.Property(x => x.LastSyncedAt).IsRequired();
        });

        // =====================================================
        // AUTOHUB DELIVERIES
        // =====================================================
        modelBuilder.Entity<NeonAutoHubDelivery>(e =>
        {
            e.ToTable("NeonAutoHubDeliveries");
            e.HasKey(x => x.DocEntry);
            e.Property(x => x.DocEntry).ValueGeneratedNever();
            e.Property(x => x.CardCode).IsRequired();
            e.Property(x => x.DocStatus).HasMaxLength(1);
            e.Property(x => x.SyncedAt).IsRequired();
            e.HasIndex(x => x.DocDate);
            e.HasIndex(x => x.CardCode);
        });

        modelBuilder.Entity<NeonAutoHubDeliveryLine>(e =>
        {
            e.ToTable("NeonAutoHubDeliveryLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.ItemCode).IsRequired();
            e.Property(x => x.Description).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne(x => x.Delivery).WithMany(x => x.Lines)
             .HasForeignKey(x => x.DocEntry).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DocEntry);
            e.HasIndex(x => x.ItemCode);
        });

        // =====================================================
        // AUTOHUB SALES ORDERS
        // =====================================================
        modelBuilder.Entity<NeonAutoHubSalesOrder>(e =>
        {
            e.ToTable("NeonAutoHubSalesOrders");
            e.HasKey(x => x.DocEntry);
            e.Property(x => x.DocEntry).ValueGeneratedNever();
            e.Property(x => x.CardCode).IsRequired();
            e.Property(x => x.DocStatus).HasMaxLength(1);
            e.Property(x => x.DocTotal).HasPrecision(18, 2);
            e.Property(x => x.SyncedAt).IsRequired();
            e.HasIndex(x => x.DocDate);
            e.HasIndex(x => x.DocStatus);
        });

        modelBuilder.Entity<NeonAutoHubSalesOrderLine>(e =>
        {
            e.ToTable("NeonAutoHubSalesOrderLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.ItemCode).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.OpenQty).HasPrecision(18, 4);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne(x => x.SalesOrder).WithMany(x => x.Lines)
             .HasForeignKey(x => x.DocEntry).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DocEntry);
            e.HasIndex(x => x.ItemCode);
        });

        // =====================================================
        // AUTOHUB INVOICES
        // =====================================================
        modelBuilder.Entity<NeonAutoHubInvoice>(e =>
        {
            e.ToTable("NeonAutoHubInvoices");
            e.HasKey(x => x.DocEntry);
            e.Property(x => x.DocEntry).ValueGeneratedNever();
            e.Property(x => x.CardCode).IsRequired();
            e.Property(x => x.DocStatus).HasMaxLength(1);
            e.Property(x => x.DocTotal).HasPrecision(18, 2);
            e.Property(x => x.VatSum).HasPrecision(18, 2);
            e.Property(x => x.PaidToDate).HasPrecision(18, 2);
            e.Property(x => x.SyncedAt).IsRequired();
            e.HasIndex(x => x.DocDate);
        });

        modelBuilder.Entity<NeonAutoHubInvoiceLine>(e =>
        {
            e.ToTable("NeonAutoHubInvoiceLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.ItemCode).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne(x => x.Invoice).WithMany(x => x.Lines)
             .HasForeignKey(x => x.DocEntry).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DocEntry);
            e.HasIndex(x => x.ItemCode);
        });

        // =====================================================
        // AUTOHUB GOODS RECEIPTS
        // =====================================================
        modelBuilder.Entity<NeonAutoHubGoodsReceipt>(e =>
        {
            e.ToTable("NeonAutoHubGoodsReceipts");
            e.HasKey(x => x.DocEntry);
            e.Property(x => x.DocEntry).ValueGeneratedNever();
            e.Property(x => x.SyncedAt).IsRequired();
            e.HasIndex(x => x.DocDate);
        });

        modelBuilder.Entity<NeonAutoHubGoodsReceiptLine>(e =>
        {
            e.ToTable("NeonAutoHubGoodsReceiptLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.ItemCode).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.HasOne(x => x.GoodsReceipt).WithMany(x => x.Lines)
             .HasForeignKey(x => x.DocEntry).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DocEntry);
            e.HasIndex(x => x.ItemCode);
        });

        // =====================================================
        // AUTOHUB STOCK TRANSFERS
        // =====================================================
        modelBuilder.Entity<NeonAutoHubStockTransfer>(e =>
        {
            e.ToTable("NeonAutoHubStockTransfers");
            e.HasKey(x => x.DocEntry);
            e.Property(x => x.DocEntry).ValueGeneratedNever();
            e.Property(x => x.SyncedAt).IsRequired();
            e.HasIndex(x => x.DocDate);
        });

        modelBuilder.Entity<NeonAutoHubStockTransferLine>(e =>
        {
            e.ToTable("NeonAutoHubStockTransferLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.ItemCode).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.HasOne(x => x.StockTransfer).WithMany(x => x.Lines)
             .HasForeignKey(x => x.DocEntry).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DocEntry);
            e.HasIndex(x => x.ItemCode);
        });

        // =====================================================
        // AUTOHUB INVENTORY COUNTINGS
        // =====================================================
        modelBuilder.Entity<NeonAutoHubInventoryCounting>(e =>
        {
            e.ToTable("NeonAutoHubInventoryCountings");
            e.HasKey(x => x.DocEntry);
            e.Property(x => x.DocEntry).ValueGeneratedNever();
            e.Property(x => x.SyncedAt).IsRequired();
            e.HasIndex(x => x.CountDate);
        });

        modelBuilder.Entity<NeonAutoHubInventoryCountingLine>(e =>
        {
            e.ToTable("NeonAutoHubInventoryCountingLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.ItemCode).IsRequired();
            e.Property(x => x.CountedQty).HasPrecision(18, 4);
            e.HasOne(x => x.InventoryCounting).WithMany(x => x.Lines)
             .HasForeignKey(x => x.DocEntry).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DocEntry);
            e.HasIndex(x => x.ItemCode);
        });

        // =====================================================
        // AUTOHUB PURCHASE ORDERS
        // =====================================================
        modelBuilder.Entity<NeonAutoHubPurchaseOrder>(e =>
        {
            e.ToTable("NeonAutoHubPurchaseOrders");
            e.HasKey(x => x.DocEntry);
            e.Property(x => x.DocEntry).ValueGeneratedNever();
            e.Property(x => x.CardCode).IsRequired();
            e.Property(x => x.DocStatus).HasMaxLength(1);
            e.Property(x => x.DocTotal).HasPrecision(18, 2);
            e.Property(x => x.SyncedAt).IsRequired();
            e.HasIndex(x => x.DocDate);
            e.HasIndex(x => x.DocStatus);
        });

        modelBuilder.Entity<NeonAutoHubPurchaseOrderLine>(e =>
        {
            e.ToTable("NeonAutoHubPurchaseOrderLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.ItemCode).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.OpenQty).HasPrecision(18, 4);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.LineTotal).HasPrecision(18, 2);
            e.HasOne(x => x.PurchaseOrder).WithMany(x => x.Lines)
             .HasForeignKey(x => x.DocEntry).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DocEntry);
            e.HasIndex(x => x.ItemCode);
        });

        // =====================================================
        // AUTOHUB UNITS OF MEASURE
        // =====================================================
        modelBuilder.Entity<NeonAutoHubUoM>(e =>
        {
            e.ToTable("NeonAutoHubUoMs");
            e.HasKey(x => x.UomEntry);
            e.Property(x => x.UomEntry).ValueGeneratedNever();
            e.Property(x => x.UomCode).IsRequired().HasMaxLength(20);
            e.Property(x => x.UomName).HasMaxLength(100);
        });

        // =====================================================
        // AUTOHUB SALES PERSONS
        // =====================================================
        modelBuilder.Entity<NeonAutoHubSalesPerson>(e =>
        {
            e.ToTable("NeonAutoHubSalesPersons");
            e.HasKey(x => x.SalesPersonCode);
            e.Property(x => x.SalesPersonCode).ValueGeneratedNever();
            e.Property(x => x.SalesPersonName).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.SyncedAt).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
