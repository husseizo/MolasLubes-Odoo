using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class InitialNeonSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NeonCustomers",
                columns: table => new
                {
                    CardCode = table.Column<string>(type: "text", nullable: false),
                    CardName = table.Column<string>(type: "text", nullable: false),
                    OdooPartnerId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CreditLimit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OutstandingBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AvailableCredit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SalesPersonCode = table.Column<int>(type: "integer", nullable: true),
                    SalesPersonName = table.Column<string>(type: "text", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonCustomers", x => x.CardCode);
                });

            migrationBuilder.CreateTable(
                name: "NeonDeliveries",
                columns: table => new
                {
                    SapDocEntry = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SapDocNum = table.Column<int>(type: "integer", nullable: false),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    OdooDeliveryId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SalesOrderEntry = table.Column<int>(type: "integer", nullable: false),
                    CardCode = table.Column<string>(type: "text", nullable: false),
                    DeliveryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BaseOrderEntry = table.Column<int>(type: "integer", nullable: true),
                    DeliveredQty = table.Column<decimal>(type: "numeric", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonDeliveries", x => x.SapDocEntry);
                });

            migrationBuilder.CreateTable(
                name: "NeonInvoices",
                columns: table => new
                {
                    SapDocEntry = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocNum = table.Column<int>(type: "integer", nullable: false),
                    CustomerCode = table.Column<string>(type: "text", nullable: false),
                    OdooInvoiceId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    DocTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonInvoices", x => x.SapDocEntry);
                });

            migrationBuilder.CreateTable(
                name: "NeonPayments",
                columns: table => new
                {
                    SapDocEntry = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocNum = table.Column<int>(type: "integer", nullable: false),
                    CustomerCode = table.Column<string>(type: "text", nullable: false),
                    OdooPaymentId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonPayments", x => x.SapDocEntry);
                });

            migrationBuilder.CreateTable(
                name: "NeonPriceLists",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemCode = table.Column<string>(type: "text", nullable: false),
                    PriceList = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OdooPricelistId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OdooSyncDir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonPriceLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NeonProducts",
                columns: table => new
                {
                    ItemCode = table.Column<string>(type: "text", nullable: false),
                    ItemName = table.Column<string>(type: "text", nullable: false),
                    ForeignName = table.Column<string>(type: "text", nullable: true),
                    ItemGroupCode = table.Column<int>(type: "integer", nullable: true),
                    ItemGroupName = table.Column<string>(type: "text", nullable: true),
                    Brand = table.Column<string>(type: "text", nullable: true),
                    UoM = table.Column<string>(type: "text", nullable: true),
                    DefaultWarehouse = table.Column<string>(type: "text", nullable: true),
                    IsInventoryItem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OnHandSap = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AvailableCache = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OdooProductId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonProducts", x => x.ItemCode);
                });

            migrationBuilder.CreateTable(
                name: "NeonSalesOrderLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SalesOrderEntry = table.Column<int>(type: "integer", nullable: false),
                    ItemCode = table.Column<string>(type: "text", nullable: false),
                    ItemName = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OdooSalesOrderLineId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonSalesOrderLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NeonSalesOrders",
                columns: table => new
                {
                    SapDocEntry = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocNum = table.Column<int>(type: "integer", nullable: false),
                    CustomerCode = table.Column<string>(type: "text", nullable: false),
                    CustomerName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OdooSalesOrderId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OdooSyncDir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    DocTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DocDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SalesPersonCode = table.Column<int>(type: "integer", nullable: true),
                    SalesPersonName = table.Column<string>(type: "text", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonSalesOrders", x => x.SapDocEntry);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NeonPriceLists_ItemCode_PriceList",
                table: "NeonPriceLists",
                columns: new[] { "ItemCode", "PriceList" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NeonCustomers");

            migrationBuilder.DropTable(
                name: "NeonDeliveries");

            migrationBuilder.DropTable(
                name: "NeonInvoices");

            migrationBuilder.DropTable(
                name: "NeonPayments");

            migrationBuilder.DropTable(
                name: "NeonPriceLists");

            migrationBuilder.DropTable(
                name: "NeonProducts");

            migrationBuilder.DropTable(
                name: "NeonSalesOrderLines");

            migrationBuilder.DropTable(
                name: "NeonSalesOrders");
        }
    }
}
