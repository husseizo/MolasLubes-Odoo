using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    public partial class AddLiquiMolyProductBarcodeInfo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllBarcodes",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BarcodeResolutionNote",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BarcodeResolutionStatus",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasUnitBarcode",
                table: "CacheLiquiMolyProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryBarcode",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PrimaryBarcodeBaseQtyInGroup",
                table: "CacheLiquiMolyProducts",
                type: "decimal(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrimaryBarcodeUomEntry",
                table: "CacheLiquiMolyProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryBarcodeUomCode",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryBarcodeUomName",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SapUomInfo",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllBarcodes", table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "BarcodeResolutionNote", table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "BarcodeResolutionStatus", table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "HasUnitBarcode", table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "PrimaryBarcode", table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "PrimaryBarcodeBaseQtyInGroup", table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "PrimaryBarcodeUomEntry", table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "PrimaryBarcodeUomCode", table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "PrimaryBarcodeUomName", table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "SapUomInfo", table: "CacheLiquiMolyProducts");
        }
    }
}
