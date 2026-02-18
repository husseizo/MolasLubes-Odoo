using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddNeonInvoiceLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NeonInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceEntry = table.Column<int>(type: "integer", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseEntry = table.Column<int>(type: "integer", nullable: false),
                    BaseLine = table.Column<int>(type: "integer", nullable: false),
                    OdooInvoiceLineId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NeonInvoiceLines_NeonInvoices_InvoiceEntry",
                        column: x => x.InvoiceEntry,
                        principalTable: "NeonInvoices",
                        principalColumn: "SapDocEntry",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NeonInvoiceLines_InvoiceEntry",
                table: "NeonInvoiceLines",
                column: "InvoiceEntry");

            migrationBuilder.CreateIndex(
                name: "IX_NeonInvoiceLines_OdooInvoiceLineId",
                table: "NeonInvoiceLines",
                column: "OdooInvoiceLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NeonInvoiceLines");
        }
    }
}
