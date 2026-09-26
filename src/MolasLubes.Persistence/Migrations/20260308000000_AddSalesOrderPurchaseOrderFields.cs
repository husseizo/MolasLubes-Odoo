using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations;

/// <summary>
/// Adds Sales Order and Purchase Order tracking fields to LiquiMoly replenishment
/// requests for inter-company transaction traceability.
/// </summary>
public partial class AddSalesOrderPurchaseOrderFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add Sales Order fields (MolasLubes side)
        migrationBuilder.AddColumn<int>(
            name: "SalesOrderDocEntry",
            table: "CacheLiquiMolyReplenishmentRequests",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "SalesOrderDocNum",
            table: "CacheLiquiMolyReplenishmentRequests",
            type: "int",
            nullable: true);

        // Add Purchase Order fields (AutoHub side)
        migrationBuilder.AddColumn<int>(
            name: "PurchaseOrderDocEntry",
            table: "CacheLiquiMolyReplenishmentRequests",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PurchaseOrderDocNum",
            table: "CacheLiquiMolyReplenishmentRequests",
            type: "int",
            nullable: true);

        // Add document type indicator
        migrationBuilder.AddColumn<string>(
            name: "ExecutionMode",
            table: "CacheLiquiMolyReplenishmentRequests",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: true,
            defaultValue: "TRANSFER");  // "TRANSFER" (old) or "SALES_PURCHASE" (new)
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SalesOrderDocEntry",
            table: "CacheLiquiMolyReplenishmentRequests");

        migrationBuilder.DropColumn(
            name: "SalesOrderDocNum",
            table: "CacheLiquiMolyReplenishmentRequests");

        migrationBuilder.DropColumn(
            name: "PurchaseOrderDocEntry",
            table: "CacheLiquiMolyReplenishmentRequests");

        migrationBuilder.DropColumn(
            name: "PurchaseOrderDocNum",
            table: "CacheLiquiMolyReplenishmentRequests");

        migrationBuilder.DropColumn(
            name: "ExecutionMode",
            table: "CacheLiquiMolyReplenishmentRequests");
    }
}
