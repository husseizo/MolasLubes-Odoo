using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddInvoiceLineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "NeonInvoiceLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "GrossBuyPr",
                table: "NeonInvoiceLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OdooStatus",
                table: "NeonInvoiceLines",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OdooSyncDir",
                table: "NeonInvoiceLines",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OdooErrorMsg",
                table: "NeonInvoiceLines",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OdooLastSync",
                table: "NeonInvoiceLines",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "NeonInvoiceLines");

            migrationBuilder.DropColumn(
                name: "GrossBuyPr",
                table: "NeonInvoiceLines");

            migrationBuilder.DropColumn(
                name: "OdooStatus",
                table: "NeonInvoiceLines");

            migrationBuilder.DropColumn(
                name: "OdooSyncDir",
                table: "NeonInvoiceLines");

            migrationBuilder.DropColumn(
                name: "OdooErrorMsg",
                table: "NeonInvoiceLines");

            migrationBuilder.DropColumn(
                name: "OdooLastSync",
                table: "NeonInvoiceLines");
        }
    }
}
