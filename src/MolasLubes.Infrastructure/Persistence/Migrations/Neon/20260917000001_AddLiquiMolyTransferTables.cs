using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddLiquiMolyTransferTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NeonLiquiMolyTransfers",
                columns: table => new
                {
                    DocEntry      = table.Column<int>(type: "integer", nullable: false),
                    DocType       = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    DocNum        = table.Column<int>(type: "integer", nullable: false),
                    SourceProfile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DocDate       = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TaxDate       = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DocDueDate    = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FromWhsCode   = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ToWhsCode     = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Comments      = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    DocStatus     = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    UserSign      = table.Column<int>(type: "integer", nullable: true),
                    DocTotal      = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReplenishmentRef = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SyncedAt      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonLiquiMolyTransfers", x => new { x.DocEntry, x.DocType });
                });

            migrationBuilder.CreateTable(
                name: "NeonLiquiMolyTransferLines",
                columns: table => new
                {
                    Id          = table.Column<long>(type: "bigint", nullable: false)
                                      .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocEntry    = table.Column<int>(type: "integer", nullable: false),
                    DocType     = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    LineNum     = table.Column<int>(type: "integer", nullable: false),
                    ItemCode    = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Quantity    = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    OpenQty     = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UomCode     = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FromWhsCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ToWhsCode   = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Price       = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    LineTotal   = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    BaseType    = table.Column<int>(type: "integer", nullable: true),
                    BaseEntry   = table.Column<int>(type: "integer", nullable: true),
                    BaseLine    = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonLiquiMolyTransferLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NeonLiquiMolyTransferLines_NeonLiquiMolyTransfers_DocEntry_DocType",
                        columns: x => new { x.DocEntry, x.DocType },
                        principalTable: "NeonLiquiMolyTransfers",
                        principalColumns: new[] { "DocEntry", "DocType" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NeonLiquiMolyTransfers_DocDate",
                table: "NeonLiquiMolyTransfers",
                column: "DocDate");

            migrationBuilder.CreateIndex(
                name: "IX_NeonLiquiMolyTransfers_ReplenishmentRef",
                table: "NeonLiquiMolyTransfers",
                column: "ReplenishmentRef");

            migrationBuilder.CreateIndex(
                name: "IX_NeonLiquiMolyTransfers_SourceProfile_DocType",
                table: "NeonLiquiMolyTransfers",
                columns: new[] { "SourceProfile", "DocType" });

            migrationBuilder.CreateIndex(
                name: "IX_NeonLiquiMolyTransferLines_DocEntry_DocType",
                table: "NeonLiquiMolyTransferLines",
                columns: new[] { "DocEntry", "DocType" });

            migrationBuilder.CreateIndex(
                name: "IX_NeonLiquiMolyTransferLines_ItemCode",
                table: "NeonLiquiMolyTransferLines",
                column: "ItemCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NeonLiquiMolyTransferLines");
            migrationBuilder.DropTable(name: "NeonLiquiMolyTransfers");
        }
    }
}
