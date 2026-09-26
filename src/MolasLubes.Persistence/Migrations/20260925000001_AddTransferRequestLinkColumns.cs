using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    public partial class AddTransferRequestLinkColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientReference",
                table: "CacheLiquiMolyTransfers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActorSapUserCode",
                table: "CacheLiquiMolyTransfers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaseRequestDocEntry",
                table: "CacheLiquiMolyTransfers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseRequestDocNum",
                table: "CacheLiquiMolyTransfers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InventoryTransferDocEntry",
                table: "CacheLiquiMolyTransfers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InventoryTransferDocNum",
                table: "CacheLiquiMolyTransfers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CacheLiquiMolyTransfers_ClientReference",
                table: "CacheLiquiMolyTransfers",
                column: "ClientReference");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CacheLiquiMolyTransfers_ClientReference",
                table: "CacheLiquiMolyTransfers");

            migrationBuilder.DropColumn(
                name: "ClientReference",
                table: "CacheLiquiMolyTransfers");

            migrationBuilder.DropColumn(
                name: "ActorSapUserCode",
                table: "CacheLiquiMolyTransfers");

            migrationBuilder.DropColumn(
                name: "BaseRequestDocEntry",
                table: "CacheLiquiMolyTransfers");

            migrationBuilder.DropColumn(
                name: "BaseRequestDocNum",
                table: "CacheLiquiMolyTransfers");

            migrationBuilder.DropColumn(
                name: "InventoryTransferDocEntry",
                table: "CacheLiquiMolyTransfers");

            migrationBuilder.DropColumn(
                name: "InventoryTransferDocNum",
                table: "CacheLiquiMolyTransfers");
        }
    }
}
