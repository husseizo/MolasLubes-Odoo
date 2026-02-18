using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddInvoicePaymentLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InvoiceEntry",
                table: "NeonPayments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_NeonPayments_InvoiceEntry",
                table: "NeonPayments",
                column: "InvoiceEntry");

            migrationBuilder.CreateIndex(
                name: "IX_NeonInvoices_SyncedAt",
                table: "NeonInvoices",
                column: "SyncedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_NeonPayments_NeonInvoices_InvoiceEntry",
                table: "NeonPayments",
                column: "InvoiceEntry",
                principalTable: "NeonInvoices",
                principalColumn: "SapDocEntry",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NeonPayments_NeonInvoices_InvoiceEntry",
                table: "NeonPayments");

            migrationBuilder.DropIndex(
                name: "IX_NeonPayments_InvoiceEntry",
                table: "NeonPayments");

            migrationBuilder.DropIndex(
                name: "IX_NeonInvoices_SyncedAt",
                table: "NeonInvoices");

            migrationBuilder.DropColumn(
                name: "InvoiceEntry",
                table: "NeonPayments");
        }
    }
}
