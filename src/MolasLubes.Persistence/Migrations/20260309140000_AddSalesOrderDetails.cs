using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <summary>
    /// Adds missing columns to CacheSalesOrders and CacheSalesOrderLines:
    ///
    /// CacheSalesOrders:
    ///   - CustomerName  (nvarchar(200), nullable)
    ///   - DocDate       (datetime2, nullable)
    ///   - DocTotal      (decimal(18,2), not null, default 0)
    ///   - UpdateDate    (datetime2, nullable)
    ///
    /// CacheSalesOrderLines:
    ///   - LineNum       (int, not null, default 0)
    ///   - ItemName      (nvarchar(200), nullable)
    ///   - Price         (decimal(18,2), not null, default 0)
    ///   - LineTotal     (decimal(18,2), not null, default 0)
    /// </summary>
    public partial class AddSalesOrderDetails : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── CacheSalesOrders ──────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "CacheSalesOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DocDate",
                table: "CacheSalesOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DocTotal",
                table: "CacheSalesOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateDate",
                table: "CacheSalesOrders",
                type: "datetime2",
                nullable: true);

            // ── CacheSalesOrderLines ──────────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "LineNum",
                table: "CacheSalesOrderLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "CacheSalesOrderLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "CacheSalesOrderLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LineTotal",
                table: "CacheSalesOrderLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CustomerName", table: "CacheSalesOrders");
            migrationBuilder.DropColumn(name: "DocDate",      table: "CacheSalesOrders");
            migrationBuilder.DropColumn(name: "DocTotal",     table: "CacheSalesOrders");
            migrationBuilder.DropColumn(name: "UpdateDate",   table: "CacheSalesOrders");

            migrationBuilder.DropColumn(name: "LineNum",   table: "CacheSalesOrderLines");
            migrationBuilder.DropColumn(name: "ItemName",  table: "CacheSalesOrderLines");
            migrationBuilder.DropColumn(name: "Price",     table: "CacheSalesOrderLines");
            migrationBuilder.DropColumn(name: "LineTotal", table: "CacheSalesOrderLines");
        }
    }
}
