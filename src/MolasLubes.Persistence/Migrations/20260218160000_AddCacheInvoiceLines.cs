using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCacheInvoiceLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SapDocEntry = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseEntry = table.Column<int>(type: "int", nullable: false),
                    BaseLine = table.Column<int>(type: "int", nullable: false),
                    OdooInvoiceLineId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CacheInvoiceLines_CacheInvoices_SapDocEntry",
                        column: x => x.SapDocEntry,
                        principalTable: "CacheInvoices",
                        principalColumn: "SapDocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CacheInvoiceLines_OdooInvoiceLineId",
                table: "CacheInvoiceLines",
                column: "OdooInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CacheInvoiceLines_SapDocEntry",
                table: "CacheInvoiceLines",
                column: "SapDocEntry");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CacheInvoiceLines");
        }
    }
}
