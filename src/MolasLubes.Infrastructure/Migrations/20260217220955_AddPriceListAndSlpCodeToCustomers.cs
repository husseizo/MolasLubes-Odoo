using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceListAndSlpCodeToCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PriceList",
                table: "CacheCustomers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlpCode",
                table: "CacheCustomers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CacheCustomers_PriceList",
                table: "CacheCustomers",
                column: "PriceList");

            migrationBuilder.CreateIndex(
                name: "IX_CacheCustomers_SlpCode",
                table: "CacheCustomers",
                column: "SlpCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CacheCustomers_PriceList",
                table: "CacheCustomers");

            migrationBuilder.DropIndex(
                name: "IX_CacheCustomers_SlpCode",
                table: "CacheCustomers");

            migrationBuilder.DropColumn(
                name: "PriceList",
                table: "CacheCustomers");

            migrationBuilder.DropColumn(
                name: "SlpCode",
                table: "CacheCustomers");
        }
    }
}
