using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    public partial class AddLiquiMolyProductBarcodeInfo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllBarcodes",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BarcodeResolutionNote",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BarcodeResolutionStatus",
                table: "NeonLiquiMolyProducts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasUnitBarcode",
                table: "NeonLiquiMolyProducts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryBarcode",
                table: "NeonLiquiMolyProducts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PrimaryBarcodeBaseQtyInGroup",
                table: "NeonLiquiMolyProducts",
                type: "numeric(19,6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrimaryBarcodeUomEntry",
                table: "NeonLiquiMolyProducts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryBarcodeUomCode",
                table: "NeonLiquiMolyProducts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryBarcodeUomName",
                table: "NeonLiquiMolyProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SapUomInfo",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllBarcodes", table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "BarcodeResolutionNote", table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "BarcodeResolutionStatus", table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "HasUnitBarcode", table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "PrimaryBarcode", table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "PrimaryBarcodeBaseQtyInGroup", table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "PrimaryBarcodeUomEntry", table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "PrimaryBarcodeUomCode", table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "PrimaryBarcodeUomName", table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "SapUomInfo", table: "NeonLiquiMolyProducts");
        }
    }
}
