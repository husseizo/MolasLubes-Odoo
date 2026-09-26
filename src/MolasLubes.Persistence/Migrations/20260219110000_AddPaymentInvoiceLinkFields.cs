using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentInvoiceLinkFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The primary invoice this payment was applied to (from RCT2).
            // Allows NeonPaymentSyncService to set the correct InvoiceEntry FK.
            migrationBuilder.AddColumn<int>(
                name: "InvoiceDocEntry",
                table: "CachePayment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Amount applied to the linked invoice (SumApplied from RCT2).
            migrationBuilder.AddColumn<decimal>(
                name: "SumApplied",
                table: "CachePayment",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_CachePayment_InvoiceDocEntry",
                table: "CachePayment",
                column: "InvoiceDocEntry");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CachePayment_InvoiceDocEntry",
                table: "CachePayment");

            migrationBuilder.DropColumn(
                name: "InvoiceDocEntry",
                table: "CachePayment");

            migrationBuilder.DropColumn(
                name: "SumApplied",
                table: "CachePayment");
        }
    }
}
