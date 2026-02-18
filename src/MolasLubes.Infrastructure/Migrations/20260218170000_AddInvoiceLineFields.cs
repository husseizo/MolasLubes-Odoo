using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "CacheInvoiceLines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "GrossBuyPr",
                table: "CacheInvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OdooStatus",
                table: "CacheInvoiceLines",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OdooSyncDir",
                table: "CacheInvoiceLines",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OdooErrorMsg",
                table: "CacheInvoiceLines",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OdooLastSync",
                table: "CacheInvoiceLines",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "CacheInvoiceLines");

            migrationBuilder.DropColumn(
                name: "GrossBuyPr",
                table: "CacheInvoiceLines");

            migrationBuilder.DropColumn(
                name: "OdooStatus",
                table: "CacheInvoiceLines");

            migrationBuilder.DropColumn(
                name: "OdooSyncDir",
                table: "CacheInvoiceLines");

            migrationBuilder.DropColumn(
                name: "OdooErrorMsg",
                table: "CacheInvoiceLines");

            migrationBuilder.DropColumn(
                name: "OdooLastSync",
                table: "CacheInvoiceLines");
        }
    }
}
