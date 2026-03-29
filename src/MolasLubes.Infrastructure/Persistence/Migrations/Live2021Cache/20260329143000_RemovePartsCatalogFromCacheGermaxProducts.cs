using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Live2021Cache
{
    public partial class RemovePartsCatalogFromCacheGermaxProducts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartsCatalog",
                table: "CacheGermaxProducts");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PartsCatalog",
                table: "CacheGermaxProducts",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
