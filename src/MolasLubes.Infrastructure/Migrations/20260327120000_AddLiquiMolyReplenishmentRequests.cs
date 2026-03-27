using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MolasLubes.Infrastructure.Persistence;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    [DbContext(typeof(MolasCacheDbContext))]
    [Migration("20260327120000_AddLiquiMolyReplenishmentRequests")]
    public class AddLiquiMolyReplenishmentRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheLiquiMolyReplenishmentRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestRef = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceProfile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetProfile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceWarehouse = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TargetWarehouse = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedBySapUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ApprovedBySapUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RejectedBySapUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExecutedBySapUser = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Comments = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TransferRef = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    GoodsIssueDocEntry = table.Column<int>(type: "int", nullable: true),
                    GoodsIssueDocNum = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    GoodsReceiptDocEntry = table.Column<int>(type: "int", nullable: true),
                    GoodsReceiptDocNum = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheLiquiMolyReplenishmentRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CacheLiquiMolyReplenishmentRequestLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    SourceItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ArticleNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CurrentStockTarget = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AvailableSupplierStock = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    QtySold30d = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    QtySold60d = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    QtySold90d = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AvgDailySales30d = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DaysOfStock = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SuggestedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TrendCategory = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ApprovedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ExecutionStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExecutionMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheLiquiMolyReplenishmentRequestLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CacheLiquiMolyReplenishmentRequestLines_CacheLiquiMolyReplenishmentRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "CacheLiquiMolyReplenishmentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CacheLMReplenishmentRequests_RequestRef",
                table: "CacheLiquiMolyReplenishmentRequests",
                column: "RequestRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CacheLMReplenishmentRequests_Status",
                table: "CacheLiquiMolyReplenishmentRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CacheLMReplenishmentRequestLines_RequestId",
                table: "CacheLiquiMolyReplenishmentRequestLines",
                column: "RequestId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CacheLiquiMolyReplenishmentRequestLines");

            migrationBuilder.DropTable(
                name: "CacheLiquiMolyReplenishmentRequests");
        }
    }
}
