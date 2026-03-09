using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <summary>
    /// Adds Liter (numeric 8,3, nullable) to NeonLiquiMolyProducts.
    /// Stores the volume in litres parsed from PackagingSize (e.g. 5.000, 0.500 for 500 ml).
    /// </summary>
    public partial class AddLiterToLiquiMolyProducts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Liter",
                table: "NeonLiquiMolyProducts",
                type: "numeric(8,3)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Liter", table: "NeonLiquiMolyProducts");
        }
    }
}
