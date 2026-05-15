using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    public partial class AddLiquiMolyProductContentSections : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Application",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiquiMolyRecommendations",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecificationItems",
                table: "NeonLiquiMolyProducts",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Application",
                table: "NeonLiquiMolyProducts");

            migrationBuilder.DropColumn(
                name: "LiquiMolyRecommendations",
                table: "NeonLiquiMolyProducts");

            migrationBuilder.DropColumn(
                name: "SpecificationItems",
                table: "NeonLiquiMolyProducts");
        }
    }
}
