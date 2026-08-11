using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.AutoHub
{
    /// <inheritdoc />
    public partial class AddTantivyPartsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tantivy_parts",
                columns: table => new
                {
                    ItemCode   = table.Column<string>(type: "character varying(50)",  maxLength: 50,  nullable: false),
                    ItemName   = table.Column<string>(type: "text",                                   nullable: false),
                    U_MdlTEST  = table.Column<string>(type: "character varying(50)",  maxLength: 50,  nullable: true),
                    U_Article_No  = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    U_Engine_Code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SyncedAt   = table.Column<DateTime>(type: "timestamp with time zone",              nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tantivy_parts", x => x.ItemCode);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tantivy_parts_mdltest",
                table: "Tantivy_parts",
                column: "U_MdlTEST");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Tantivy_parts");
        }
    }
}
