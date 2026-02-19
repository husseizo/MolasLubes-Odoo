using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddNeonDeliveryLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NeonDeliveryLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeliveryEntry = table.Column<int>(type: "integer", nullable: false),
                    LineNum = table.Column<int>(type: "integer", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossBuyPr = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseEntry = table.Column<int>(type: "integer", nullable: false),
                    BaseLine = table.Column<int>(type: "integer", nullable: false),
                    OdooMoveId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OdooSalesOrderLineId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OdooSyncDir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonDeliveryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NeonDeliveryLines_NeonDeliveries_DeliveryEntry",
                        column: x => x.DeliveryEntry,
                        principalTable: "NeonDeliveries",
                        principalColumn: "SapDocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NeonDeliveryLines_DeliveryEntry",
                table: "NeonDeliveryLines",
                column: "DeliveryEntry");

            migrationBuilder.CreateIndex(
                name: "IX_NeonDeliveryLines_OdooMoveId",
                table: "NeonDeliveryLines",
                column: "OdooMoveId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NeonDeliveryLines");
        }
    }
}
