using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCacheSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheCustomers",
                columns: table => new
                {
                    CardCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CardName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    OdooCustomerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OdooPartnerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LastSapDeltaAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreditLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OutstandingBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AvailableCredit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreditUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheCustomers", x => x.CardCode);
                });

            migrationBuilder.CreateTable(
                name: "CacheDeliveries",
                columns: table => new
                {
                    SapDocEntry = table.Column<int>(type: "int", nullable: false),
                    SapDocNum = table.Column<int>(type: "int", nullable: false),
                    CardCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BaseOrderEntry = table.Column<int>(type: "int", nullable: true),
                    DeliveredQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SapUpdateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCancelled = table.Column<bool>(type: "bit", nullable: false),
                    LastSapSyncAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OdooDeliveryId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheDeliveries", x => x.SapDocEntry);
                });

            migrationBuilder.CreateTable(
                name: "CacheInvoices",
                columns: table => new
                {
                    SapDocEntry = table.Column<int>(type: "int", nullable: false),
                    BaseOrderEntry = table.Column<int>(type: "int", nullable: false),
                    SapDocNum = table.Column<int>(type: "int", nullable: false),
                    CardCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DocDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DocTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatSum = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OdooInvoiceId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CachedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheInvoices", x => x.SapDocEntry);
                });

            migrationBuilder.CreateTable(
                name: "CachePayment",
                columns: table => new
                {
                    SapDocEntry = table.Column<int>(type: "int", nullable: false),
                    SapDocNum = table.Column<int>(type: "int", nullable: false),
                    CardCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DocDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalPaid = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CachedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OdooPaymentId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OdooSyncDir = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachePayment", x => x.SapDocEntry);
                });

            migrationBuilder.CreateTable(
                name: "CacheProducts",
                columns: table => new
                {
                    ItemCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OnHandSap = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AvailableCache = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PriceList_1 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PriceList_2 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PriceList_3 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OdooProductId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OdooPricelistId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OdooStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LastSapSyncAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheProducts", x => x.ItemCode);
                });

            migrationBuilder.CreateTable(
                name: "CacheSalesOrders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SapDocEntry = table.Column<int>(type: "int", nullable: false),
                    SapDocNum = table.Column<int>(type: "int", nullable: false),
                    CustomerCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DocStatus = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    OdooSalesOrderId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheSalesOrders", x => x.Id);
                    table.UniqueConstraint("AK_CacheSalesOrders_SapDocEntry", x => x.SapDocEntry);
                });

            migrationBuilder.CreateTable(
                name: "CacheStockReservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    WarehouseCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsCommitted = table.Column<bool>(type: "bit", nullable: false),
                    SapDocEntry = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheStockReservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CacheSalesOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SapDocEntry = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OdooSalesOrderLineId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheSalesOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CacheSalesOrderLines_CacheSalesOrders_SapDocEntry",
                        column: x => x.SapDocEntry,
                        principalTable: "CacheSalesOrders",
                        principalColumn: "SapDocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CacheCustomers_OdooPartnerId",
                table: "CacheCustomers",
                column: "OdooPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CacheCustomers_OdooStatus",
                table: "CacheCustomers",
                column: "OdooStatus");

            migrationBuilder.CreateIndex(
                name: "IX_CacheDeliveries_DeliveryDate",
                table: "CacheDeliveries",
                column: "DeliveryDate");

            migrationBuilder.CreateIndex(
                name: "IX_CacheDeliveries_LastSapSyncAt",
                table: "CacheDeliveries",
                column: "LastSapSyncAt");

            migrationBuilder.CreateIndex(
                name: "IX_CacheDeliveries_OdooDeliveryId",
                table: "CacheDeliveries",
                column: "OdooDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_CacheInvoices_OdooInvoiceId",
                table: "CacheInvoices",
                column: "OdooInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CacheInvoices_OdooStatus",
                table: "CacheInvoices",
                column: "OdooStatus");

            migrationBuilder.CreateIndex(
                name: "IX_CachePayment_OdooPaymentId",
                table: "CachePayment",
                column: "OdooPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CachePayment_OdooStatus",
                table: "CachePayment",
                column: "OdooStatus");

            migrationBuilder.CreateIndex(
                name: "IX_CacheProducts_OdooProductId",
                table: "CacheProducts",
                column: "OdooProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CacheProducts_OdooStatus",
                table: "CacheProducts",
                column: "OdooStatus");

            migrationBuilder.CreateIndex(
                name: "IX_CacheSalesOrderLines_OdooSalesOrderLineId",
                table: "CacheSalesOrderLines",
                column: "OdooSalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CacheSalesOrderLines_SapDocEntry",
                table: "CacheSalesOrderLines",
                column: "SapDocEntry");

            migrationBuilder.CreateIndex(
                name: "IX_CacheSalesOrders_OdooSalesOrderId",
                table: "CacheSalesOrders",
                column: "OdooSalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CacheSalesOrders_OdooStatus",
                table: "CacheSalesOrders",
                column: "OdooStatus");

            migrationBuilder.CreateIndex(
                name: "IX_CacheSalesOrders_SapDocEntry",
                table: "CacheSalesOrders",
                column: "SapDocEntry",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CacheCustomers");

            migrationBuilder.DropTable(
                name: "CacheDeliveries");

            migrationBuilder.DropTable(
                name: "CacheInvoices");

            migrationBuilder.DropTable(
                name: "CachePayment");

            migrationBuilder.DropTable(
                name: "CacheProducts");

            migrationBuilder.DropTable(
                name: "CacheSalesOrderLines");

            migrationBuilder.DropTable(
                name: "CacheStockReservations");

            migrationBuilder.DropTable(
                name: "CacheSalesOrders");
        }
    }
}
