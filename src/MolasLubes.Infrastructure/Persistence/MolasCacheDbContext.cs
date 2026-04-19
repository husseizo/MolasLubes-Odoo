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
    public DbSet<CacheDeliveryLine> CacheDeliveryLines => Set<CacheDeliveryLine>();
    public DbSet<CacheSalesOrder> CacheSalesOrders => Set<CacheSalesOrder>();
    public DbSet<CacheSalesOrderLine> CacheSalesOrderLines => Set<CacheSalesOrderLine>();
    public DbSet<CacheInvoice> CacheInvoices => Set<CacheInvoice>();
    public DbSet<CacheInvoiceLine> CacheInvoiceLines => Set<CacheInvoiceLine>();
    public DbSet<CachePayment> CachePayment => Set<CachePayment>(); // 🔴 singular by design
    public DbSet<CacheStockReservation> CacheStockReservations => Set<CacheStockReservation>();
    public DbSet<CacheLiquiMolyProduct> CacheLiquiMolyProducts => Set<CacheLiquiMolyProduct>();
    public DbSet<CacheLiquiMolyTransfer> CacheLiquiMolyTransfers => Set<CacheLiquiMolyTransfer>();
    public DbSet<CacheLiquiMolyTransferLine> CacheLiquiMolyTransferLines => Set<CacheLiquiMolyTransferLine>();
    public DbSet<CacheLiquiMolyReplenishmentRequest> CacheLiquiMolyReplenishmentRequests => Set<CacheLiquiMolyReplenishmentRequest>();
    public DbSet<CacheLiquiMolyReplenishmentRequestLine> CacheLiquiMolyReplenishmentRequestLines => Set<CacheLiquiMolyReplenishmentRequestLine>();

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

            entity.Property(x => x.Phone1).HasMaxLength(50);
            entity.Property(x => x.Phone2).HasMaxLength(50);
            entity.Property(x => x.Email).HasMaxLength(100);

            entity.Property(x => x.BillToStreet).HasMaxLength(200);
            entity.Property(x => x.BillToCity).HasMaxLength(100);
            entity.Property(x => x.BillToCountry).HasMaxLength(10);

            entity.Property(x => x.ShipToStreet).HasMaxLength(200);
            entity.Property(x => x.ShipToCity).HasMaxLength(100);
            entity.Property(x => x.ShipToCountry).HasMaxLength(10);

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
        // DELIVERY LINES (FK → DELIVERIES)
        // =====================================================
        modelBuilder.Entity<CacheDeliveryLine>(entity =>
        {
            entity.ToTable("CacheDeliveryLines");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.ItemCode)
                  .HasMaxLength(50)
                  .IsRequired();

            entity.Property(x => x.Description)
                  .HasMaxLength(200)
                  .IsRequired();

            entity.Property(x => x.Quantity)
                  .HasPrecision(18, 4);

            entity.Property(x => x.LineTotal)
                  .HasPrecision(18, 2);

            entity.Property(x => x.GrossBuyPr)
                  .HasPrecision(18, 2);

            entity.Property(x => x.OdooMoveId)
                  .HasMaxLength(20);

            entity.Property(x => x.OdooSalesOrderLineId)
                  .HasMaxLength(20);

            entity.Property(x => x.OdooStatus)
                  .HasMaxLength(10);

            entity.Property(x => x.OdooSyncDir)
                  .HasMaxLength(10);

            entity.Property(x => x.OdooErrorMsg)
                  .HasMaxLength(255);

            entity.HasIndex(x => x.OdooMoveId);

            // 🔗 FK → CacheDeliveries.SapDocEntry
            entity.HasOne(x => x.Delivery)
                  .WithMany(x => x.Lines)
                  .HasForeignKey(x => x.SapDocEntry)
                  .OnDelete(DeleteBehavior.Cascade);

            // 🚀 Query speed
            entity.HasIndex(x => x.SapDocEntry);
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

            entity.Property(e => e.CustomerName).HasMaxLength(200);
            entity.Property(e => e.DocTotal).HasPrecision(18, 2);

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

            entity.Property(x => x.Price)
                  .HasPrecision(18, 2);

            entity.Property(x => x.LineTotal)
                  .HasPrecision(18, 2);

            entity.Property(x => x.ItemCode)
                  .HasMaxLength(50);

            entity.Property(x => x.ItemName)
                  .HasMaxLength(200);

            entity.Property(x => x.WarehouseCode)
                  .HasMaxLength(20);

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
        // LIQUI-MOLY SCRAPED PRODUCTS
        // =====================================================
        modelBuilder.Entity<CacheLiquiMolyProduct>(entity =>
        {
            entity.ToTable("CacheLiquiMolyProducts");

            entity.HasKey(x => x.ArticleNumber);

            entity.Property(x => x.ArticleNumber).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(100);
            entity.Property(x => x.SubCategory).HasMaxLength(100);
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.SpecGrade).HasMaxLength(50);
            entity.Property(x => x.PackagingSize).HasMaxLength(30);
            entity.Property(x => x.Liter).HasColumnType("decimal(8,3)");

            // JSON columns for multi-value fields
            entity.Property(x => x.AllPackagingSizes);          // JSON array of strings
            entity.Property(x => x.ImageUrl).HasMaxLength(500);
            entity.Property(x => x.AllImageUrls);               // JSON array of strings
            entity.Property(x => x.Approvals);                  // JSON array of strings
            entity.Property(x => x.Specifications);             // JSON object (key-value)
            entity.Property(x => x.OverviewProperties);         // JSON array of strings

            // PDF downloads
            entity.Property(x => x.ProductInfoPdfUrl).HasMaxLength(500);
            entity.Property(x => x.SafetyDataSheetPdfUrl).HasMaxLength(500);

            entity.Property(x => x.ProductUrl).HasMaxLength(500);
            entity.Property(x => x.ScrapedAt).IsRequired();

            entity.HasIndex(x => x.Category);
            entity.HasIndex(x => x.IsActive);
            entity.HasIndex(x => x.ScrapedAt);
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

        // =====================================================
        // LIQUI-MOLY TRANSFER AUDIT
        // =====================================================
        modelBuilder.Entity<CacheLiquiMolyTransfer>(entity =>
        {
            entity.ToTable("CacheLiquiMolyTransfers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();

            entity.Property(x => x.TransferRef).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.TransferRef)
                  .IsUnique()
                  .HasDatabaseName("IX_CacheLiquiMolyTransfers_TransferRef");

            entity.Property(x => x.SourceProfile).HasMaxLength(50).IsRequired();
            entity.Property(x => x.TargetProfile).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SourceWarehouse).HasMaxLength(20).IsRequired();
            entity.Property(x => x.TargetWarehouse).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Comments).HasMaxLength(500);
            entity.Property(x => x.GoodsIssueDocNum).HasMaxLength(20);
            entity.Property(x => x.GoodsReceiptDocNum).HasMaxLength(20);
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ErrorMessage).HasMaxLength(1000);

            entity.HasIndex(x => x.Status)
                  .HasDatabaseName("IX_CacheLiquiMolyTransfers_Status");
        });

        modelBuilder.Entity<CacheLiquiMolyTransferLine>(entity =>
        {
            entity.ToTable("CacheLiquiMolyTransferLines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();

            entity.Property(x => x.SourceItemCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.TargetItemCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ArticleNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SourceItemName).HasMaxLength(200);
            entity.Property(x => x.TargetItemName).HasMaxLength(200);
            entity.Property(x => x.Quantity).HasPrecision(18, 4);
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ErrorMessage).HasMaxLength(500);

            entity.HasOne(x => x.Transfer)
                  .WithMany(x => x.Lines)
                  .HasForeignKey(x => x.TransferId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.TransferId)
                  .HasDatabaseName("IX_CacheLiquiMolyTransferLines_TransferId");
        });

        // =====================================================
        // LIQUI-MOLY REPLENISHMENT REQUESTS
        // =====================================================
        modelBuilder.Entity<CacheLiquiMolyReplenishmentRequest>(entity =>
        {
            entity.ToTable("CacheLiquiMolyReplenishmentRequests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();

            entity.Property(x => x.RequestRef).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.RequestRef)
                  .IsUnique()
                  .HasDatabaseName("IX_CacheLMReplenishmentRequests_RequestRef");

            entity.Property(x => x.SourceProfile).HasMaxLength(50).IsRequired();
            entity.Property(x => x.TargetProfile).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SourceWarehouse).HasMaxLength(20).IsRequired();
            entity.Property(x => x.TargetWarehouse).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();

            entity.Property(x => x.RequestedBySapUser).HasMaxLength(50);
            entity.Property(x => x.ApprovedBySapUser).HasMaxLength(50);
            entity.Property(x => x.RejectedBySapUser).HasMaxLength(50);
            entity.Property(x => x.ExecutedBySapUser).HasMaxLength(50);

            entity.Property(x => x.Comments).HasMaxLength(500);
            entity.Property(x => x.RejectionReason).HasMaxLength(500);
            entity.Property(x => x.TransferRef).HasMaxLength(30);
            entity.Property(x => x.GoodsIssueDocNum).HasMaxLength(20);
            entity.Property(x => x.GoodsReceiptDocNum).HasMaxLength(20);
            entity.Property(x => x.ErrorMessage).HasMaxLength(1000);

            entity.HasIndex(x => x.Status)
                  .HasDatabaseName("IX_CacheLMReplenishmentRequests_Status");
        });

        modelBuilder.Entity<CacheLiquiMolyReplenishmentRequestLine>(entity =>
        {
            entity.ToTable("CacheLiquiMolyReplenishmentRequestLines");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();

            entity.Property(x => x.SourceItemCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.TargetItemCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ArticleNumber).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ItemName).HasMaxLength(200);
            entity.Property(x => x.TrendCategory).HasMaxLength(20);
            entity.Property(x => x.ExecutionStatus).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ExecutionMessage).HasMaxLength(500);

            entity.Property(x => x.CurrentStockTarget).HasPrecision(18, 4);
            entity.Property(x => x.AvailableSupplierStock).HasPrecision(18, 4);
            entity.Property(x => x.QtySold30d).HasPrecision(18, 4);
            entity.Property(x => x.QtySold60d).HasPrecision(18, 4);
            entity.Property(x => x.QtySold90d).HasPrecision(18, 4);
            entity.Property(x => x.AvgDailySales30d).HasPrecision(18, 4);
            entity.Property(x => x.DaysOfStock).HasPrecision(18, 2);
            entity.Property(x => x.SuggestedQty).HasPrecision(18, 4);
            entity.Property(x => x.ApprovedQty).HasPrecision(18, 4);

            entity.HasOne(x => x.Request)
                  .WithMany(x => x.Lines)
                  .HasForeignKey(x => x.RequestId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => x.RequestId)
                  .HasDatabaseName("IX_CacheLMReplenishmentRequestLines_RequestId");
        });

        base.OnModelCreating(modelBuilder);
    }
}
