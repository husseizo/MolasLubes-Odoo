using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContactAndAddressToCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Phone1",
                table: "CacheCustomers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone2",
                table: "CacheCustomers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "CacheCustomers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillToStreet",
                table: "CacheCustomers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillToCity",
                table: "CacheCustomers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillToCountry",
                table: "CacheCustomers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToStreet",
                table: "CacheCustomers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCity",
                table: "CacheCustomers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCountry",
                table: "CacheCustomers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Phone1",
                table: "CacheCustomers");

            migrationBuilder.DropColumn(
                name: "Phone2",
                table: "CacheCustomers");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "CacheCustomers");

            migrationBuilder.DropColumn(
                name: "BillToStreet",
                table: "CacheCustomers");

            migrationBuilder.DropColumn(
                name: "BillToCity",
                table: "CacheCustomers");

            migrationBuilder.DropColumn(
                name: "BillToCountry",
                table: "CacheCustomers");

            migrationBuilder.DropColumn(
                name: "ShipToStreet",
                table: "CacheCustomers");

            migrationBuilder.DropColumn(
                name: "ShipToCity",
                table: "CacheCustomers");

            migrationBuilder.DropColumn(
                name: "ShipToCountry",
                table: "CacheCustomers");
        }
    }
}
