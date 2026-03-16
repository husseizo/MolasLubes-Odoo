using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddContactAndAddressToNeonCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Phone1",
                table: "NeonCustomers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone2",
                table: "NeonCustomers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "NeonCustomers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillToStreet",
                table: "NeonCustomers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillToCity",
                table: "NeonCustomers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillToCountry",
                table: "NeonCustomers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToStreet",
                table: "NeonCustomers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCity",
                table: "NeonCustomers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCountry",
                table: "NeonCustomers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Phone1",       table: "NeonCustomers");
            migrationBuilder.DropColumn(name: "Phone2",       table: "NeonCustomers");
            migrationBuilder.DropColumn(name: "Email",        table: "NeonCustomers");
            migrationBuilder.DropColumn(name: "BillToStreet", table: "NeonCustomers");
            migrationBuilder.DropColumn(name: "BillToCity",   table: "NeonCustomers");
            migrationBuilder.DropColumn(name: "BillToCountry",table: "NeonCustomers");
            migrationBuilder.DropColumn(name: "ShipToStreet", table: "NeonCustomers");
            migrationBuilder.DropColumn(name: "ShipToCity",   table: "NeonCustomers");
            migrationBuilder.DropColumn(name: "ShipToCountry",table: "NeonCustomers");
        }
    }
}
