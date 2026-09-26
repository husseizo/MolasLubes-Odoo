using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the following columns to CacheLiquiMolyProducts:
    ///   - AllPackagingSizes  (nvarchar(max)) — JSON array of size strings
    ///   - AllImageUrls       (nvarchar(max)) — JSON array of image URL strings
    ///   - Approvals          (nvarchar(max)) — JSON array of approval strings
    ///   - Specifications     (nvarchar(max)) — JSON object (key/value specs)
    ///   - ProductInfoPdfUrl  (nvarchar(500)) — English Product Information PDF
    ///   - SafetyDataSheetPdfUrl (nvarchar(500)) — English Safety Data Sheet PDF
    ///
    /// Also widens Description from nvarchar(2000) → nvarchar(4000).
    /// </summary>
    public partial class AddLiquiMolyProductDetails : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Widen Description ──────────────────────────────────────
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            // ── New multi-value / JSON columns ─────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "AllPackagingSizes",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllImageUrls",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Approvals",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specifications",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(max)",
                nullable: true);

            // ── PDF download URLs ──────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "ProductInfoPdfUrl",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyDataSheetPdfUrl",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllPackagingSizes",   table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "AllImageUrls",        table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "Approvals",           table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "Specifications",      table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "ProductInfoPdfUrl",   table: "CacheLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "SafetyDataSheetPdfUrl", table: "CacheLiquiMolyProducts");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
