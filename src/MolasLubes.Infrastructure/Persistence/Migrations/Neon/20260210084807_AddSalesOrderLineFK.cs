using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddSalesOrderLineFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ItemCode",
                table: "NeonSalesOrderLines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_NeonSalesOrderLines_OdooSalesOrderLineId",
                table: "NeonSalesOrderLines",
                column: "OdooSalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_NeonSalesOrderLines_SalesOrderEntry",
                table: "NeonSalesOrderLines",
                column: "SalesOrderEntry");

            migrationBuilder.AddForeignKey(
                name: "FK_NeonSalesOrderLines_NeonSalesOrders_SalesOrderEntry",
                table: "NeonSalesOrderLines",
                column: "SalesOrderEntry",
                principalTable: "NeonSalesOrders",
                principalColumn: "SapDocEntry",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NeonSalesOrderLines_NeonSalesOrders_SalesOrderEntry",
                table: "NeonSalesOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_NeonSalesOrderLines_OdooSalesOrderLineId",
                table: "NeonSalesOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_NeonSalesOrderLines_SalesOrderEntry",
                table: "NeonSalesOrderLines");

            migrationBuilder.AlterColumn<string>(
                name: "ItemCode",
                table: "NeonSalesOrderLines",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);
        }
    }
}
