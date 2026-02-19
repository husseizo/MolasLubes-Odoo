using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddCardNameAndVatSumToNeonInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Customer name carried from SAP CardName (OINV.CardName)
            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "NeonInvoices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // VAT total – was already in CacheInvoice but missing from NeonInvoices
            migrationBuilder.AddColumn<decimal>(
                name: "VatSum",
                table: "NeonInvoices",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardName",
                table: "NeonInvoices");

            migrationBuilder.DropColumn(
                name: "VatSum",
                table: "NeonInvoices");
        }
    }
}
