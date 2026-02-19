using Microsoft.EntityFrameworkCore;
using MolasLubes.Domain.Entities.Cache;

namespace MolasLubes.Infrastructure.Persistence;

public class MolasCacheDbContext : DbContext
{
    public MolasCacheDbContext(DbContextOptions<MolasCacheDbContext> options)
        : base(options)
    {
    }

    // =============================
    // CACHE TABLES
    // =============================
    public DbSet<CacheProduct> CacheProducts => Set<CacheProduct>();
    public DbSet<CacheCustomer> CacheCustomers => Set<CacheCustomer>();
    public DbSet<CacheDelivery> CacheDeliveries => Set<CacheDelivery>();
    public DbSet<CacheSalesOrder> CacheSalesOrders => Set<CacheSalesOrder>();
    public DbSet<CacheSalesOrderLine> CacheSalesOrderLines => Set<CacheSalesOrderLine>();
    public DbSet<CacheInvoice> CacheInvoices => Set<CacheInvoice>();
    public DbSet<CacheInvoiceLine> CacheInvoiceLines => Set<CacheInvoiceLine>();
    public DbSet<CachePayment> CachePayment => Set<CachePayment>(); // 🔴 singular by design
    public DbSet<CacheStockReservation> CacheStockReservations => Set<CacheStockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // =====================================================
        // PRODUCTS
        // =====================================================
        modelBuilder.Entity<CacheProduct>(entity =>
        {
            entity.ToTable("CacheProducts");

            // ✅ COMPOSITE KEY
            entity.HasKey(x => new { x.ItemCode, x.WarehouseCode });

            entity.Property(x => x.ItemCode)
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(x => x.WarehouseCode)
                  .HasMaxLength(20)
                  .IsRequired();

            entity.Property(x => x.RowVersion)
                  .IsRowVersion();

            entity.Property(x => x.Barcode)
                  .HasMaxLength(50);

            entity.Property(x => x.OnHandSap).HasPrecision(18, 4);
            entity.Property(x => x.AvailableCache).HasPrecision(18, 4);

            entity.HasIndex(x => x.Barcode);
            entity.Property(x => x.PriceList_1).HasPrecision(18, 2);
            entity.Property(x => x.PriceList_2).HasPrecision(18, 2);
            entity.Property(x => x.PriceList_3).HasPrecision(18, 2);

            entity.Property(x => x.OdooProductId).HasMaxLength(20);
            entity.Property(x => x.OdooStatus).HasMaxLength(10);
            entity.Property(x => x.OdooSyncDir).HasMaxLength(10);

            entity.HasIndex(x => x.ItemCode);
            entity.HasIndex(x => x.WarehouseCode);
            entity.HasIndex(x => x.OdooProductId);
            entity.HasIndex(x => x.OdooStatus);
        });


        // =====================================================
        // CUSTOMERS
        // =====================================================
        modelBuilder.Entity<CacheCustomer>(entity =>
        {
            entity.ToTable("CacheCustomers");

            entity.HasKey(x => x.CardCode);

            entity.Property(x => x.CreditLimit).HasPrecision(18, 2);
            entity.Property(x => x.OutstandingBalance).HasPrecision(18, 2);
            entity.Property(x => x.AvailableCredit).HasPrecision(18, 2);

            entity.Property(x => x.PriceList);
            entity.Property(x => x.SlpCode);

            entity.HasIndex(x => x.PriceList);
            entity.HasIndex(x => x.SlpCode);

            entity.Property(x => x.OdooPartnerId).HasMaxLength(20);
            entity.Property(x => x.OdooStatus).HasMaxLength(10);
            entity.Property(x => x.OdooSyncDir).HasMaxLength(10);

            entity.HasIndex(x => x.OdooPartnerId);
            entity.HasIndex(x => x.OdooStatus);
        });

        // =====================================================
        // DELIVERIES
        // =====================================================
        modelBuilder.Entity<CacheDelivery>(entity =>
        {
            entity.ToTable("CacheDeliveries");

            entity.HasKey(x => x.SapDocEntry);

            // 🔥🔥🔥 THIS LINE FIXES EVERYTHING
            entity.Property(x => x.SapDocEntry)
                  .ValueGeneratedNever();

            entity.Property(x => x.CardCode)
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(x => x.DeliveredQty)
                  .HasPrecision(18, 4);

            entity.Property(x => x.OdooDeliveryId).HasMaxLength(20);
            entity.Property(x => x.OdooStatus).HasMaxLength(10);
            entity.Property(x => x.OdooSyncDir).HasMaxLength(10);

            entity.HasIndex(x => x.DeliveryDate);
            entity.HasIndex(x => x.LastSapSyncAt);
            entity.HasIndex(x => x.OdooDeliveryId);
        });

        // =====================================================
        // SALES ORDERS
        // =====================================================
        modelBuilder.Entity<CacheSalesOrder>(entity =>
        {
            entity.ToTable("CacheSalesOrders");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                  .ValueGeneratedOnAdd();

            entity.Property(e => e.SapDocEntry)
                  .ValueGeneratedNever();

            entity.HasIndex(e => e.SapDocEntry)
                  .IsUnique();

            entity.Property(e => e.OdooSalesOrderId).HasMaxLength(20);
            entity.Property(e => e.OdooStatus).HasMaxLength(10);
            entity.Property(e => e.OdooSyncDir).HasMaxLength(10);

            entity.HasIndex(e => e.OdooSalesOrderId);
            entity.HasIndex(e => e.OdooStatus);
        });

        // =====================================================
        // SALES ORDER LINES (FK → ORDERS)
        // =====================================================
        modelBuilder.Entity<CacheSalesOrderLine>(entity =>
        {
            entity.ToTable("CacheSalesOrderLines");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Quantity)
                  .HasPrecision(18, 4);

            entity.Property(x => x.ItemCode)
                  .HasMaxLength(50);

            entity.Property(x => x.OdooSalesOrderLineId)
                  .HasMaxLength(20);

            entity.HasIndex(x => x.OdooSalesOrderLineId);

            // 🔗 FK → CacheSalesOrders.SapDocEntry
            entity.HasOne(x => x.SalesOrder)
                  .WithMany(x => x.Lines)
                  .HasForeignKey(x => x.SapDocEntry)
                  .HasPrincipalKey(x => x.SapDocEntry)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // =====================================================
        // INVOICES
        // =====================================================
        modelBuilder.Entity<CacheInvoice>(entity =>
        {
            entity.ToTable("CacheInvoices");

            entity.HasKey(x => x.SapDocEntry);

            // 🔥🔥🔥 CRITICAL FIX
            entity.Property(x => x.SapDocEntry)
                  .ValueGeneratedNever();

            entity.Property(x => x.DocTotal)
                  .HasPrecision(18, 2);

            entity.Property(x => x.VatSum)
                  .HasPrecision(18, 2);

            entity.Property(x => x.CardName).HasMaxLength(100);

            entity.Property(x => x.OdooInvoiceId).HasMaxLength(20);
            entity.Property(x => x.OdooStatus).HasMaxLength(10);
            entity.Property(x => x.OdooSyncDir).HasMaxLength(10);

            entity.HasIndex(x => x.OdooInvoiceId);
            entity.HasIndex(x => x.OdooStatus);
        });

        // =====================================================
        // INVOICE LINES (FK → INVOICES)
        // =====================================================
        modelBuilder.Entity<CacheInvoiceLine>(entity =>
        {
            entity.ToTable("CacheInvoiceLines");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Quantity)
                  .HasPrecision(18, 4);

            entity.Property(x => x.LineTotal)
                  .HasPrecision(18, 2);

            entity.Property(x => x.GrossBuyPr)
                  .HasPrecision(18, 2);

            entity.Property(x => x.ItemCode)
                  .HasMaxLength(50);

            entity.Property(x => x.Description)
                  .HasMaxLength(200);

            entity.Property(x => x.OdooInvoiceLineId)
                  .HasMaxLength(20);

            entity.Property(x => x.OdooStatus)
                  .HasMaxLength(10);

            entity.Property(x => x.OdooSyncDir)
                  .HasMaxLength(10);

            entity.Property(x => x.OdooErrorMsg)
                  .HasMaxLength(255);

            entity.HasIndex(x => x.OdooInvoiceLineId);

            // 🔗 FK → CacheInvoices.SapDocEntry
            entity.HasOne(x => x.Invoice)
                  .WithMany(x => x.Lines)
                  .HasForeignKey(x => x.SapDocEntry)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // =====================================================
        // PAYMENTS  (⚠ SINGULAR TABLE NAME)
        // =====================================================
        modelBuilder.Entity<CachePayment>(entity =>
        {
            entity.ToTable("CachePayment");

            entity.HasKey(x => x.SapDocEntry);

            entity.Property(x => x.SapDocEntry)
                  .ValueGeneratedNever(); // 🔥 REQUIRED

            entity.Property(x => x.TotalPaid)
                  .HasPrecision(18, 2);

            entity.Property(x => x.SumApplied)
                  .HasPrecision(18, 2);

            entity.Property(x => x.CardCode)
                  .HasMaxLength(20);

            entity.HasIndex(x => x.InvoiceDocEntry);

            entity.Property(x => x.OdooPaymentId).HasMaxLength(20);
            entity.Property(x => x.OdooStatus).HasMaxLength(10);
            entity.Property(x => x.OdooSyncDir).HasMaxLength(10);

            entity.HasIndex(x => x.OdooPaymentId);
            entity.HasIndex(x => x.OdooStatus);
        });

        // =====================================================
        // STOCK RESERVATIONS (INTERNAL)
        // =====================================================
        modelBuilder.Entity<CacheStockReservation>(entity =>
        {
            entity.ToTable("CacheStockReservations");

            // ✅ SINGLE PRIMARY KEY
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Quantity)
                  .HasPrecision(18, 4);

            entity.Property(x => x.ItemCode)
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(x => x.WarehouseCode)
                  .HasMaxLength(20)
                  .IsRequired();

            // ✅ INDEX for fast lookup (NOT PRIMARY KEY)
            entity.HasIndex(x => new { x.ItemCode, x.WarehouseCode });
        });

        base.OnModelCreating(modelBuilder);
    }
}