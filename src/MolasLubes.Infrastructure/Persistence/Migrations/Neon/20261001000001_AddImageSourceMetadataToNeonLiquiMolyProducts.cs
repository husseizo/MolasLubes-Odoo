using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    public partial class AddImageSourceMetadataToNeonLiquiMolyProducts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageSourceArticleNumber",
                table: "NeonLiquiMolyProducts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImageFallbackUsed",
                table: "NeonLiquiMolyProducts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ImageFallbackReason",
                table: "NeonLiquiMolyProducts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageSourceArticleNumber",
                table: "NeonLiquiMolyProducts");

            migrationBuilder.DropColumn(
                name: "ImageFallbackUsed",
                table: "NeonLiquiMolyProducts");

            migrationBuilder.DropColumn(
                name: "ImageFallbackReason",
                table: "NeonLiquiMolyProducts");
        }
    }
}
