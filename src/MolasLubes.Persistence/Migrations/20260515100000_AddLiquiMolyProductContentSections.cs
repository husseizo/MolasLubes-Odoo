using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    public partial class AddLiquiMolyProductContentSections : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Application",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiquiMolyRecommendations",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecificationItems",
                table: "CacheLiquiMolyProducts",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Application",
                table: "CacheLiquiMolyProducts");

            migrationBuilder.DropColumn(
                name: "LiquiMolyRecommendations",
                table: "CacheLiquiMolyProducts");

            migrationBuilder.DropColumn(
                name: "SpecificationItems",
                table: "CacheLiquiMolyProducts");
        }
    }
}
