using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCacheDeliveryLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheDeliveryLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SapDocEntry = table.Column<int>(type: "int", nullable: false),
                    LineNum = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrossBuyPr = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseEntry = table.Column<int>(type: "int", nullable: false),
                    BaseLine = table.Column<int>(type: "int", nullable: false),
                    OdooMoveId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OdooSalesOrderLineId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OdooStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OdooSyncDir = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    OdooErrorMsg = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    OdooLastSync = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheDeliveryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CacheDeliveryLines_CacheDeliveries_SapDocEntry",
                        column: x => x.SapDocEntry,
                        principalTable: "CacheDeliveries",
                        principalColumn: "SapDocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CacheDeliveryLines_SapDocEntry",
                table: "CacheDeliveryLines",
                column: "SapDocEntry");

            migrationBuilder.CreateIndex(
                name: "IX_CacheDeliveryLines_OdooMoveId",
                table: "CacheDeliveryLines",
                column: "OdooMoveId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CacheDeliveryLines");
        }
    }
}
