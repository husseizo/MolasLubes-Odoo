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
    public DbSet<NeonInvoice> Invoices => Set<NeonInvoice>();
    public DbSet<NeonInvoiceLine> InvoiceLines => Set<NeonInvoiceLine>();
    public DbSet<NeonPayment> Payments => Set<NeonPayment>();

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

        // =====================================================
        // INVOICES
        // =====================================================
        modelBuilder.Entity<NeonInvoice>(e =>
        {
            e.ToTable("NeonInvoices");

            e.HasKey(x => x.SapDocEntry);

            e.Property(x => x.DocTotal).HasPrecision(18, 2);
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

            e.Property(x => x.Quantity)
             .HasPrecision(18, 4);

            e.Property(x => x.LineTotal)
             .HasPrecision(18, 2);

            e.Property(x => x.OdooInvoiceLineId)
             .HasMaxLength(20);

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

        base.OnModelCreating(modelBuilder);
    }
}