using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseToCacheProductCompositeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_CacheProducts",
                table: "CacheProducts");

            migrationBuilder.AlterColumn<string>(
                name: "WarehouseCode",
                table: "CacheStockReservations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<decimal>(
                name: "OnHandSap",
                table: "CacheProducts",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "AvailableCache",
                table: "CacheProducts",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "ItemCode",
                table: "CacheProducts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "WarehouseCode",
                table: "CacheProducts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CacheProducts",
                table: "CacheProducts",
                columns: new[] { "ItemCode", "WarehouseCode" });

            migrationBuilder.CreateIndex(
                name: "IX_CacheStockReservations_ItemCode_WarehouseCode",
                table: "CacheStockReservations",
                columns: new[] { "ItemCode", "WarehouseCode" });

            migrationBuilder.CreateIndex(
                name: "IX_CacheProducts_ItemCode",
                table: "CacheProducts",
                column: "ItemCode");

            migrationBuilder.CreateIndex(
                name: "IX_CacheProducts_WarehouseCode",
                table: "CacheProducts",
                column: "WarehouseCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CacheStockReservations_ItemCode_WarehouseCode",
                table: "CacheStockReservations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CacheProducts",
                table: "CacheProducts");

            migrationBuilder.DropIndex(
                name: "IX_CacheProducts_ItemCode",
                table: "CacheProducts");

            migrationBuilder.DropIndex(
                name: "IX_CacheProducts_WarehouseCode",
                table: "CacheProducts");

            migrationBuilder.DropColumn(
                name: "WarehouseCode",
                table: "CacheProducts");

            migrationBuilder.AlterColumn<string>(
                name: "WarehouseCode",
                table: "CacheStockReservations",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<decimal>(
                name: "OnHandSap",
                table: "CacheProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "AvailableCache",
                table: "CacheProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<string>(
                name: "ItemCode",
                table: "CacheProducts",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddPrimaryKey(
                name: "PK_CacheProducts",
                table: "CacheProducts",
                column: "ItemCode");
        }
    }
}
