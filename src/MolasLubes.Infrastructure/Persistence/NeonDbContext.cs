using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MolasLubes.Domain.Entities.Neon;

namespace MolasLubes.Infrastructure.Persistence;

public class NeonDbContext : DbContext
{
    public NeonDbContext(DbContextOptions<NeonDbContext> options)
        : base(options)
    {
    }

    // =============================
    // DB SETS
    // =============================
    public DbSet<NeonProduct> Products => Set<NeonProduct>();
    public DbSet<NeonCustomer> Customers => Set<NeonCustomer>();
    public DbSet<NeonPriceList> PriceLists => Set<NeonPriceList>();
    public DbSet<NeonSalesOrder> SalesOrders => Set<NeonSalesOrder>();
    public DbSet<NeonSalesOrderLine> SalesOrderLines => Set<NeonSalesOrderLine>();
    public DbSet<NeonDelivery> Deliveries => Set<NeonDelivery>();
    public DbSet<NeonDeliveryLine> DeliveryLines => Set<NeonDeliveryLine>();
    public DbSet<NeonInvoice> Invoices => Set<NeonInvoice>();
    public DbSet<NeonInvoiceLine> InvoiceLines => Set<NeonInvoiceLine>();
    public DbSet<NeonPayment> Payments => Set<NeonPayment>();
    public DbSet<NeonLiquiMolyProduct> LiquiMolyProducts => Set<NeonLiquiMolyProduct>();
    public DbSet<NeonApiCache> ApiCaches => Set<NeonApiCache>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // =====================================================
        // 🌍 GLOBAL UTC ENFORCEMENT (Postgres-safe)
        // =====================================================
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc
                ? v
                : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
        );

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                if (prop.ClrType == typeof(DateTime))
                {
                    prop.SetValueConverter(utcConverter);
                }
            }
        }

        // =====================================================
        // PRODUCTS
        // =====================================================
        modelBuilder.Entity<NeonProduct>(e =>
        {
            e.ToTable("NeonProducts");

            e.HasKey(x => x.ItemCode);
            e.HasIndex(x => x.Barcode);
            e.HasIndex(x => x.Brand);

            e.Property(x => x.Barcode) .HasMaxLength(50);
            e.Property(x => x.ItemName).IsRequired();
            e.Property(x => x.OnHandSap).HasPrecision(18, 2);
            e.Property(x => x.AvailableCache).HasPrecision(18, 2);

            e.Property(x => x.OdooProductId).HasMaxLength(20);
            e.Property(x => x.OdooStatus).HasMaxLength(10);
            e.Property(x => x.OdooErrorMsg).HasMaxLength(255);
            e.Property(x => x.OdooSyncDir).HasMaxLength(10);

            e.Property(x => x.SyncedAt).IsRequired();
        });

        // =====================================================
        // CUSTOMERS
        // =====================================================
        modelBuilder.Entity<NeonCustomer>(e =>
        {
            e.ToTable("NeonCustomers");

            e.HasKey(x => x.CardCode);

            e.Property(x => x.CardName).IsRequired();
            e.Property(x => x.CreditLimit).HasPrecision(18, 2);
            e.Property(x => x.OutstandingBalance).HasPrecision(18, 2);
            e.Property(x => x.AvailableCredit).HasPrecision(18, 2);

            e.Property(x => x.Phone1).HasMaxLength(50);
            e.Property(x => x.Phone2).HasMaxLength(50);
            e.Property(x => x.Email).HasMaxLength(100);

            e.Property(x => x.BillToStreet).HasMaxLength(200);
            e.Property(x => x.BillToCity).HasMaxLength(100);
            e.Property(x => x.BillToCountry).HasMaxLength(10);

            e.Property(x => x.ShipToStreet).HasMaxLength(200);
            e.Property(x => x.ShipToCity).HasMaxLength(100);
            e.Property(x => x.ShipToCountry).HasMaxLength(10);

            e.Property(x => x.OdooPartnerId).HasMaxLength(20);
            e.Property(x => x.OdooStatus).HasMaxLength(10);
            e.Property(x => x.OdooErrorMsg).HasMaxLength(255);
            e.Property(x => x.OdooSyncDir).HasMaxLength(10);

            e.Property(x => x.SyncedAt).IsRequired();
        });

        // =====================================================
        // PRICE LISTS
        // =====================================================
        modelBuilder.Entity<NeonPriceList>(e =>
        {
            e.ToTable("NeonPriceLists");

            e.HasKey(x => x.Id);

            e.HasIndex(x => new { x.ItemCode, x.PriceList })
             .IsUnique();

            e.Property(x => x.Price).HasPrecision(18, 2);

            e.Property(x => x.OdooPricelistId).HasMaxLength(20);
            e.Property(x => x.OdooStatus).HasMaxLength(10);
            e.Property(x => x.OdooErrorMsg).HasMaxLength(255);
            e.Property(x => x.OdooSyncDir).HasMaxLength(10);

            e.Property(x => x.SyncedAt).IsRequired();
        });

        // =====================================================
        // SALES ORDERS
        // =====================================================
        modelBuilder.Entity<NeonSalesOrder>(e =>
        {
            e.ToTable("NeonSalesOrders");

            e.HasKey(x => x.SapDocEntry);

            e.Property(x => x.DocTotal).HasPrecision(18, 2);
            e.Property(x => x.DocDate).IsRequired();

            e.Property(x => x.OdooSalesOrderId).HasMaxLength(20);
            e.Property(x => x.OdooStatus).HasMaxLength(10);
            e.Property(x => x.OdooErrorMsg).HasMaxLength(255);
            e.Property(x => x.OdooSyncDir).HasMaxLength(10);

            e.Property(x => x.SyncedAt).IsRequired();
        });

        modelBuilder.Entity<NeonSalesOrderLine>(e =>
        {
            e.ToTable("NeonSalesOrderLines");

            e.HasKey(x => x.Id);

            e.Property(x => x.ItemCode)
             .IsRequired()
             .HasMaxLength(50);

            e.Property(x => x.ItemName)
             .IsRequired();

            e.Property(x => x.Quantity)
             .HasPrecision(18, 2);

            e.Property(x => x.Price)
             .HasPrecision(18, 2);

            e.Property(x => x.LineTotal)
             .HasPrecision(18, 2);

            // 🔗 FK relationship
            e.HasOne(x => x.SalesOrder)
             .WithMany(x => x.Lines)
             .HasForeignKey(x => x.SalesOrderEntry)
             .OnDelete(DeleteBehavior.Cascade);

            // 🚀 Query speed
            e.HasIndex(x => x.SalesOrderEntry);
            e.HasIndex(x => x.OdooSalesOrderLineId);
        });

        // =====================================================
        // DELIVERIES
        // =====================================================
        modelBuilder.Entity<NeonDelivery>(e =>
        {
            e.ToTable("NeonDeliveries");

            e.HasKey(x => x.SapDocEntry);

            e.Property(x => x.DeliveryDate).IsRequired();

            e.Property(x => x.OdooDeliveryId).HasMaxLength(20);
            e.Property(x => x.OdooStatus).HasMaxLength(10);
            e.Property(x => x.OdooErrorMsg).HasMaxLength(255);
            e.Property(x => x.OdooSyncDir).HasMaxLength(10);

            e.Property(x => x.SyncedAt).IsRequired();
        });

        modelBuilder.Entity<NeonDeliveryLine>(e =>
        {
            e.ToTable("NeonDeliveryLines");

            e.HasKey(x => x.Id);

            e.Property(x => x.ItemCode)
             .IsRequired()
             .HasMaxLength(50);

            e.Property(x => x.Description)
             .IsRequired()
             .HasMaxLength(200);

            e.Property(x => x.Quantity)
             .HasPrecision(18, 4);

            e.Property(x => x.LineTotal)
             .HasPrecision(18, 2);

            e.Property(x => x.GrossBuyPr)
             .HasPrecision(18, 2);

            e.Property(x => x.OdooMoveId)
             .HasMaxLength(20);

            e.Property(x => x.OdooSalesOrderLineId)
             .HasMaxLength(20);

            e.Property(x => x.OdooStatus)
             .HasMaxLength(10);

            e.Property(x => x.OdooSyncDir)
             .HasMaxLength(10);

            e.Property(x => x.OdooErrorMsg)
             .HasMaxLength(255);

            // 🔗 FK relationship
            e.HasOne(x => x.Delivery)
             .WithMany(x => x.Lines)
             .HasForeignKey(x => x.DeliveryEntry)
             .OnDelete(DeleteBehavior.Cascade);

            // 🚀 Query speed
            e.HasIndex(x => x.DeliveryEntry);
            e.HasIndex(x => x.OdooMoveId);
        });

        // =====================================================
        // INVOICES
        // =====================================================
        modelBuilder.Entity<NeonInvoice>(e =>
        {
            e.ToTable("NeonInvoices");

            e.HasKey(x => x.SapDocEntry);

            e.Property(x => x.CardName).HasMaxLength(100);

            e.Property(x => x.DocTotal).HasPrecision(18, 2);
            e.Property(x => x.VatSum).HasPrecision(18, 2);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.Property(x => x.InvoiceDate).IsRequired();

            e.Property(x => x.OdooInvoiceId).HasMaxLength(20);
            e.Property(x => x.OdooStatus).HasMaxLength(10);
            e.Property(x => x.OdooErrorMsg).HasMaxLength(255);
            e.Property(x => x.OdooSyncDir).HasMaxLength(10);

            e.Property(x => x.SyncedAt).IsRequired();

            // 🔍 Delta sync speed
            e.HasIndex(x => x.SyncedAt);
        });

        modelBuilder.Entity<NeonInvoiceLine>(e =>
        {
            e.ToTable("NeonInvoiceLines");

            e.HasKey(x => x.Id);

            e.Property(x => x.ItemCode)
             .IsRequired()
             .HasMaxLength(50);

            e.Property(x => x.Description)
             .IsRequired()
             .HasMaxLength(200);

            e.Property(x => x.Quantity)
             .HasPrecision(18, 4);

            e.Property(x => x.LineTotal)
             .HasPrecision(18, 2);

            e.Property(x => x.GrossBuyPr)
             .HasPrecision(18, 2);

            e.Property(x => x.OdooInvoiceLineId)
             .HasMaxLength(20);

            e.Property(x => x.OdooStatus)
             .HasMaxLength(10);

            e.Property(x => x.OdooSyncDir)
             .HasMaxLength(10);

            e.Property(x => x.OdooErrorMsg)
             .HasMaxLength(255);

            // 🔗 FK relationship
            e.HasOne(x => x.Invoice)
             .WithMany(x => x.Lines)
             .HasForeignKey(x => x.InvoiceEntry)
             .OnDelete(DeleteBehavior.Cascade);

            // 🚀 Query speed
            e.HasIndex(x => x.InvoiceEntry);
            e.HasIndex(x => x.OdooInvoiceLineId);
        });

        // =====================================================
        // PAYMENTS
        // =====================================================
        modelBuilder.Entity<NeonPayment>(e =>
        {
            e.ToTable("NeonPayments");

            e.HasKey(x => x.SapDocEntry);

            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.PaymentDate).IsRequired();

            e.Property(x => x.OdooPaymentId).HasMaxLength(20);
            e.Property(x => x.OdooStatus).HasMaxLength(10);
            e.Property(x => x.OdooErrorMsg).HasMaxLength(255);
            e.Property(x => x.OdooSyncDir).HasMaxLength(10);

            e.Property(x => x.SyncedAt).IsRequired();

            // =========================
            // 🔗 FK → INVOICE
            // =========================
            e.HasOne(x => x.Invoice)
             .WithMany(x => x.Payments)
             .HasForeignKey(x => x.InvoiceEntry)
             .OnDelete(DeleteBehavior.Cascade);

            // 🔍 Fast joins
            e.HasIndex(x => x.InvoiceEntry);
        });

        // =====================================================
        // LIQUI-MOLY SCRAPED PRODUCTS
        // =====================================================
        modelBuilder.Entity<NeonLiquiMolyProduct>(e =>
        {
            e.ToTable("NeonLiquiMolyProducts");

            e.HasKey(x => x.ArticleNumber);

            e.Property(x => x.ArticleNumber).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.SubCategory).HasMaxLength(100);
            e.Property(x => x.Description);                     // text (no length cap)
            e.Property(x => x.SpecGrade).HasMaxLength(50);
            e.Property(x => x.PackagingSize).HasMaxLength(30);
            e.Property(x => x.Liter).HasColumnType("numeric(8,3)");

            // JSON columns for multi-value fields
            e.Property(x => x.AllPackagingSizes);               // JSON array of strings
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.AllImageUrls);                    // JSON array of strings
            e.Property(x => x.PrimaryBarcode).HasMaxLength(50);
            e.Property(x => x.PrimaryBarcodeUomCode).HasMaxLength(20);
            e.Property(x => x.PrimaryBarcodeUomName).HasMaxLength(100);
            e.Property(x => x.PrimaryBarcodeBaseQtyInGroup).HasColumnType("numeric(19,6)");
            e.Property(x => x.BarcodeResolutionStatus).HasMaxLength(50);
            e.Property(x => x.BarcodeResolutionNote);
            e.Property(x => x.EanCode).HasMaxLength(20);
            e.Property(x => x.AllBarcodes);                     // JSON array of barcode rows
            e.Property(x => x.SapUomInfo);                      // JSON object snapshot
            e.Property(x => x.Approvals);                       // JSON array of strings
            e.Property(x => x.Specifications);                  // JSON object (key-value)
            e.Property(x => x.SpecificationItems);              // JSON array of strings
            e.Property(x => x.OverviewProperties);              // JSON array of strings
            e.Property(x => x.Application);                     // plain text
            e.Property(x => x.LiquiMolyRecommendations);        // JSON array of strings

            // PDF downloads
            e.Property(x => x.ProductInfoPdfUrl).HasMaxLength(500);
            e.Property(x => x.SafetyDataSheetPdfUrl).HasMaxLength(500);

            e.Property(x => x.ProductUrl).HasMaxLength(500);
            e.Property(x => x.ScrapedAt).IsRequired();

            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.IsActive);
            e.HasIndex(x => x.ScrapedAt);
        });

        // =====================================================
        // API RESPONSE CACHE
        // =====================================================
        modelBuilder.Entity<NeonApiCache>(e =>
        {
            e.ToTable("NeonApiCache");
            e.HasKey(x => x.CacheKey);
            e.Property(x => x.CacheKey).HasMaxLength(512).IsRequired();
            e.Property(x => x.Endpoint).HasMaxLength(200).IsRequired();
            e.Property(x => x.DataJson).IsRequired();
            e.HasIndex(x => x.ExpiresAt);
        });

        base.OnModelCreating(modelBuilder);
    }
}
