using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.AutoHub
{
    public partial class RemovePartsCatalogFromNeonGermaxProducts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "parts_catalog",
                table: "neon_germax_products");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "parts_catalog",
                table: "neon_germax_products",
                type: "text",
                nullable: true);
        }
    }
}
