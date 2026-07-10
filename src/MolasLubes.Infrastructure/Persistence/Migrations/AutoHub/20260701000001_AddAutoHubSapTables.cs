using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.AutoHub
{
    /// <inheritdoc />
    public partial class AddAutoHubSapTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── AutoHub SAP B1 products ──────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NeonAutoHubProducts",
                columns: table => new
                {
                    ItemCode             = table.Column<string>(type: "text", nullable: false),
                    ItemName             = table.Column<string>(type: "text", nullable: false),
                    OnHandSap            = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AvailableCache       = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive             = table.Column<bool>(type: "boolean", nullable: false),
                    SyncedAt             = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    U_MdlTEST            = table.Column<string>(type: "text", nullable: true),
                    U_Item_Name          = table.Column<string>(type: "text", nullable: true),
                    U_Article_No         = table.Column<string>(type: "text", nullable: true),
                    U_ReferenceNum       = table.Column<string>(type: "text", nullable: true),
                    U_OriginalNumber     = table.Column<string>(type: "text", nullable: true),
                    U_PT_No_Inproduction = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubProducts", x => x.ItemCode);
                });

            // ── Delta sync watermark ─────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NeonAutoHubSyncState",
                columns: table => new
                {
                    DocType     = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubSyncState", x => x.DocType);
                });

            // ── Deliveries ───────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NeonAutoHubDeliveries",
                columns: table => new
                {
                    DocEntry     = table.Column<int>(type: "integer", nullable: false),
                    DocNum       = table.Column<int>(type: "integer", nullable: false),
                    CardCode     = table.Column<string>(type: "text", nullable: false),
                    CardName     = table.Column<string>(type: "text", nullable: true),
                    DocDate      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocStatus    = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    IsCancelled  = table.Column<bool>(type: "boolean", nullable: false),
                    Comments     = table.Column<string>(type: "text", nullable: true),
                    UpdatedInSap = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubDeliveries", x => x.DocEntry);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubDeliveries_CardCode", table: "NeonAutoHubDeliveries", column: "CardCode");
            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubDeliveries_DocDate",  table: "NeonAutoHubDeliveries", column: "DocDate");

            migrationBuilder.CreateTable(
                name: "NeonAutoHubDeliveryLines",
                columns: table => new
                {
                    Id          = table.Column<long>(type: "bigint", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocEntry    = table.Column<int>(type: "integer", nullable: false),
                    LineNum     = table.Column<int>(type: "integer", nullable: false),
                    ItemCode    = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Quantity    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Price       = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal   = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WhsCode     = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubDeliveryLines", x => x.Id);
                    table.ForeignKey(name: "FK_NeonAutoHubDeliveryLines_NeonAutoHubDeliveries_DocEntry",
                        column: x => x.DocEntry,
                        principalTable: "NeonAutoHubDeliveries",
                        principalColumn: "DocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubDeliveryLines_DocEntry",  table: "NeonAutoHubDeliveryLines", column: "DocEntry");
            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubDeliveryLines_ItemCode",  table: "NeonAutoHubDeliveryLines", column: "ItemCode");

            // ── Sales Orders ─────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NeonAutoHubSalesOrders",
                columns: table => new
                {
                    DocEntry     = table.Column<int>(type: "integer", nullable: false),
                    DocNum       = table.Column<int>(type: "integer", nullable: false),
                    CardCode     = table.Column<string>(type: "text", nullable: false),
                    CardName     = table.Column<string>(type: "text", nullable: true),
                    DocDate      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocDueDate   = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DocStatus    = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    DocTotal     = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Comments     = table.Column<string>(type: "text", nullable: true),
                    UpdatedInSap = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubSalesOrders", x => x.DocEntry);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubSalesOrders_DocDate",   table: "NeonAutoHubSalesOrders", column: "DocDate");
            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubSalesOrders_DocStatus", table: "NeonAutoHubSalesOrders", column: "DocStatus");

            migrationBuilder.CreateTable(
                name: "NeonAutoHubSalesOrderLines",
                columns: table => new
                {
                    Id          = table.Column<long>(type: "bigint", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocEntry    = table.Column<int>(type: "integer", nullable: false),
                    LineNum     = table.Column<int>(type: "integer", nullable: false),
                    ItemCode    = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Quantity    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    OpenQty     = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Price       = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal   = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WhsCode     = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubSalesOrderLines", x => x.Id);
                    table.ForeignKey(name: "FK_NeonAutoHubSalesOrderLines_NeonAutoHubSalesOrders_DocEntry",
                        column: x => x.DocEntry,
                        principalTable: "NeonAutoHubSalesOrders",
                        principalColumn: "DocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubSalesOrderLines_DocEntry",  table: "NeonAutoHubSalesOrderLines", column: "DocEntry");
            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubSalesOrderLines_ItemCode",  table: "NeonAutoHubSalesOrderLines", column: "ItemCode");

            // ── Invoices ─────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NeonAutoHubInvoices",
                columns: table => new
                {
                    DocEntry     = table.Column<int>(type: "integer", nullable: false),
                    DocNum       = table.Column<int>(type: "integer", nullable: false),
                    CardCode     = table.Column<string>(type: "text", nullable: false),
                    CardName     = table.Column<string>(type: "text", nullable: true),
                    DocDate      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocStatus    = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    DocTotal     = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatSum       = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidToDate   = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Comments     = table.Column<string>(type: "text", nullable: true),
                    UpdatedInSap = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubInvoices", x => x.DocEntry);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubInvoices_DocDate", table: "NeonAutoHubInvoices", column: "DocDate");

            migrationBuilder.CreateTable(
                name: "NeonAutoHubInvoiceLines",
                columns: table => new
                {
                    Id          = table.Column<long>(type: "bigint", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocEntry    = table.Column<int>(type: "integer", nullable: false),
                    LineNum     = table.Column<int>(type: "integer", nullable: false),
                    ItemCode    = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Quantity    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Price       = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal   = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WhsCode     = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubInvoiceLines", x => x.Id);
                    table.ForeignKey(name: "FK_NeonAutoHubInvoiceLines_NeonAutoHubInvoices_DocEntry",
                        column: x => x.DocEntry,
                        principalTable: "NeonAutoHubInvoices",
                        principalColumn: "DocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubInvoiceLines_DocEntry",  table: "NeonAutoHubInvoiceLines", column: "DocEntry");
            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubInvoiceLines_ItemCode",  table: "NeonAutoHubInvoiceLines", column: "ItemCode");

            // ── Goods Receipts ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NeonAutoHubGoodsReceipts",
                columns: table => new
                {
                    DocEntry     = table.Column<int>(type: "integer", nullable: false),
                    DocNum       = table.Column<int>(type: "integer", nullable: false),
                    DocDate      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Comments     = table.Column<string>(type: "text", nullable: true),
                    UpdatedInSap = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubGoodsReceipts", x => x.DocEntry);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubGoodsReceipts_DocDate", table: "NeonAutoHubGoodsReceipts", column: "DocDate");

            migrationBuilder.CreateTable(
                name: "NeonAutoHubGoodsReceiptLines",
                columns: table => new
                {
                    Id          = table.Column<long>(type: "bigint", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocEntry    = table.Column<int>(type: "integer", nullable: false),
                    LineNum     = table.Column<int>(type: "integer", nullable: false),
                    ItemCode    = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Quantity    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    WhsCode     = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubGoodsReceiptLines", x => x.Id);
                    table.ForeignKey(name: "FK_NeonAutoHubGoodsReceiptLines_NeonAutoHubGoodsReceipts_DocEntry",
                        column: x => x.DocEntry,
                        principalTable: "NeonAutoHubGoodsReceipts",
                        principalColumn: "DocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubGoodsReceiptLines_DocEntry",  table: "NeonAutoHubGoodsReceiptLines", column: "DocEntry");
            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubGoodsReceiptLines_ItemCode",  table: "NeonAutoHubGoodsReceiptLines", column: "ItemCode");

            // ── Stock Transfers ──────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NeonAutoHubStockTransfers",
                columns: table => new
                {
                    DocEntry     = table.Column<int>(type: "integer", nullable: false),
                    DocNum       = table.Column<int>(type: "integer", nullable: false),
                    DocDate      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Comments     = table.Column<string>(type: "text", nullable: true),
                    UpdatedInSap = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubStockTransfers", x => x.DocEntry);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubStockTransfers_DocDate", table: "NeonAutoHubStockTransfers", column: "DocDate");

            migrationBuilder.CreateTable(
                name: "NeonAutoHubStockTransferLines",
                columns: table => new
                {
                    Id           = table.Column<long>(type: "bigint", nullable: false)
                                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocEntry     = table.Column<int>(type: "integer", nullable: false),
                    LineNum      = table.Column<int>(type: "integer", nullable: false),
                    ItemCode     = table.Column<string>(type: "text", nullable: false),
                    Description  = table.Column<string>(type: "text", nullable: true),
                    Quantity     = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FromWhsCode  = table.Column<string>(type: "text", nullable: true),
                    ToWhsCode    = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubStockTransferLines", x => x.Id);
                    table.ForeignKey(name: "FK_NeonAutoHubStockTransferLines_NeonAutoHubStockTransfers_DocEntry",
                        column: x => x.DocEntry,
                        principalTable: "NeonAutoHubStockTransfers",
                        principalColumn: "DocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubStockTransferLines_DocEntry",  table: "NeonAutoHubStockTransferLines", column: "DocEntry");
            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubStockTransferLines_ItemCode",  table: "NeonAutoHubStockTransferLines", column: "ItemCode");

            // ── Inventory Countings ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NeonAutoHubInventoryCountings",
                columns: table => new
                {
                    DocEntry     = table.Column<int>(type: "integer", nullable: false),
                    DocNum       = table.Column<int>(type: "integer", nullable: false),
                    CountDate    = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Remarks      = table.Column<string>(type: "text", nullable: true),
                    UpdatedInSap = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubInventoryCountings", x => x.DocEntry);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubInventoryCountings_CountDate", table: "NeonAutoHubInventoryCountings", column: "CountDate");

            migrationBuilder.CreateTable(
                name: "NeonAutoHubInventoryCountingLines",
                columns: table => new
                {
                    Id         = table.Column<long>(type: "bigint", nullable: false)
                                      .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocEntry   = table.Column<int>(type: "integer", nullable: false),
                    LineNum    = table.Column<int>(type: "integer", nullable: false),
                    ItemCode   = table.Column<string>(type: "text", nullable: false),
                    WhsCode    = table.Column<string>(type: "text", nullable: true),
                    CountedQty = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubInventoryCountingLines", x => x.Id);
                    table.ForeignKey(name: "FK_NeonAutoHubInventoryCountingLines_NeonAutoHubInventoryCountings_DocEntry",
                        column: x => x.DocEntry,
                        principalTable: "NeonAutoHubInventoryCountings",
                        principalColumn: "DocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubInventoryCountingLines_DocEntry",  table: "NeonAutoHubInventoryCountingLines", column: "DocEntry");
            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubInventoryCountingLines_ItemCode",  table: "NeonAutoHubInventoryCountingLines", column: "ItemCode");

            // ── Purchase Orders ──────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NeonAutoHubPurchaseOrders",
                columns: table => new
                {
                    DocEntry     = table.Column<int>(type: "integer", nullable: false),
                    DocNum       = table.Column<int>(type: "integer", nullable: false),
                    CardCode     = table.Column<string>(type: "text", nullable: false),
                    CardName     = table.Column<string>(type: "text", nullable: true),
                    DocDate      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocDueDate   = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DocStatus    = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    DocTotal     = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Comments     = table.Column<string>(type: "text", nullable: true),
                    UpdatedInSap = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubPurchaseOrders", x => x.DocEntry);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubPurchaseOrders_DocDate",   table: "NeonAutoHubPurchaseOrders", column: "DocDate");
            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubPurchaseOrders_DocStatus", table: "NeonAutoHubPurchaseOrders", column: "DocStatus");

            migrationBuilder.CreateTable(
                name: "NeonAutoHubPurchaseOrderLines",
                columns: table => new
                {
                    Id          = table.Column<long>(type: "bigint", nullable: false)
                                       .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocEntry    = table.Column<int>(type: "integer", nullable: false),
                    LineNum     = table.Column<int>(type: "integer", nullable: false),
                    ItemCode    = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Quantity    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    OpenQty     = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Price       = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal   = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WhsCode     = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubPurchaseOrderLines", x => x.Id);
                    table.ForeignKey(name: "FK_NeonAutoHubPurchaseOrderLines_NeonAutoHubPurchaseOrders_DocEntry",
                        column: x => x.DocEntry,
                        principalTable: "NeonAutoHubPurchaseOrders",
                        principalColumn: "DocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubPurchaseOrderLines_DocEntry",  table: "NeonAutoHubPurchaseOrderLines", column: "DocEntry");
            migrationBuilder.CreateIndex(name: "IX_NeonAutoHubPurchaseOrderLines_ItemCode",  table: "NeonAutoHubPurchaseOrderLines", column: "ItemCode");

            // ── Units of Measure ─────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "NeonAutoHubUoMs",
                columns: table => new
                {
                    UomEntry   = table.Column<int>(type: "integer", nullable: false),
                    UomCode    = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UomName    = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GroupEntry = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubUoMs", x => x.UomEntry);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NeonAutoHubDeliveryLines");
            migrationBuilder.DropTable(name: "NeonAutoHubDeliveries");
            migrationBuilder.DropTable(name: "NeonAutoHubSalesOrderLines");
            migrationBuilder.DropTable(name: "NeonAutoHubSalesOrders");
            migrationBuilder.DropTable(name: "NeonAutoHubInvoiceLines");
            migrationBuilder.DropTable(name: "NeonAutoHubInvoices");
            migrationBuilder.DropTable(name: "NeonAutoHubGoodsReceiptLines");
            migrationBuilder.DropTable(name: "NeonAutoHubGoodsReceipts");
            migrationBuilder.DropTable(name: "NeonAutoHubStockTransferLines");
            migrationBuilder.DropTable(name: "NeonAutoHubStockTransfers");
            migrationBuilder.DropTable(name: "NeonAutoHubInventoryCountingLines");
            migrationBuilder.DropTable(name: "NeonAutoHubInventoryCountings");
            migrationBuilder.DropTable(name: "NeonAutoHubPurchaseOrderLines");
            migrationBuilder.DropTable(name: "NeonAutoHubPurchaseOrders");
            migrationBuilder.DropTable(name: "NeonAutoHubUoMs");
            migrationBuilder.DropTable(name: "NeonAutoHubSyncState");
            migrationBuilder.DropTable(name: "NeonAutoHubProducts");
        }
    }
}
