using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <summary>
    /// Adds the following columns to NeonLiquiMolyProducts:
    ///   - AllPackagingSizes      (text) — JSON array of size strings
    ///   - AllImageUrls           (text) — JSON array of image URL strings
    ///   - Approvals              (text) — JSON array of approval strings
    ///   - Specifications         (text) — JSON object (key/value specs)
    ///   - ProductInfoPdfUrl      (varchar 500) — English Product Information PDF
    ///   - SafetyDataSheetPdfUrl  (varchar 500) — English Safety Data Sheet PDF
    ///
    /// Also drops the 2000-char limit on Description (becomes text).
    /// </summary>
    public partial class AddLiquiMolyProductDetails : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Widen Description to unlimited text ────────────────────
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            // ── New multi-value / JSON columns ─────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "AllPackagingSizes",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllImageUrls",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Approvals",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specifications",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true);

            // ── PDF download URLs ──────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "ProductInfoPdfUrl",
                table: "NeonLiquiMolyProducts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyDataSheetPdfUrl",
                table: "NeonLiquiMolyProducts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AllPackagingSizes",   table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "AllImageUrls",        table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "Approvals",           table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "Specifications",      table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "ProductInfoPdfUrl",   table: "NeonLiquiMolyProducts");
            migrationBuilder.DropColumn(name: "SafetyDataSheetPdfUrl", table: "NeonLiquiMolyProducts");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "NeonLiquiMolyProducts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
