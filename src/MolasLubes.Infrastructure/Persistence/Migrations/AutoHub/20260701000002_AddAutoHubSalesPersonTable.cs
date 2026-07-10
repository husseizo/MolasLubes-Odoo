using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.AutoHub
{
    /// <inheritdoc />
    public partial class AddAutoHubSalesPersonTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NeonAutoHubSalesPersons",
                columns: table => new
                {
                    SalesPersonCode = table.Column<int>(type: "integer", nullable: false),
                    SalesPersonName = table.Column<string>(type: "text", nullable: false),
                    IsActive        = table.Column<bool>(type: "boolean", nullable: false),
                    Email           = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SyncedAt        = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubSalesPersons", x => x.SalesPersonCode);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NeonAutoHubSalesPersons");
        }
    }
}
