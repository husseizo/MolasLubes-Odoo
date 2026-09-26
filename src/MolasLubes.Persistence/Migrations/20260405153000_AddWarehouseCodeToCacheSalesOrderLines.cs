using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MolasLubes.Infrastructure.Persistence;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    [DbContext(typeof(MolasCacheDbContext))]
    [Migration("20260405153000_AddWarehouseCodeToCacheSalesOrderLines")]
    public partial class AddWarehouseCodeToCacheSalesOrderLines : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WarehouseCode",
                table: "CacheSalesOrderLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WarehouseCode",
                table: "CacheSalesOrderLines");
        }
    }
}
