using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImageSourceMetadataToLiquiMolyProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageSourceArticleNumber",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImageFallbackUsed",
                table: "CacheLiquiMolyProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ImageFallbackReason",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageSourceArticleNumber",
                table: "CacheLiquiMolyProducts");

            migrationBuilder.DropColumn(
                name: "ImageFallbackUsed",
                table: "CacheLiquiMolyProducts");

            migrationBuilder.DropColumn(
                name: "ImageFallbackReason",
                table: "CacheLiquiMolyProducts");
        }
    }
}
