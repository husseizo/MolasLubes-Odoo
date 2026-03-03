using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddLiquiMolyProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NeonLiquiMolyProducts",
                columns: table => new
                {
                    ArticleNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name          = table.Column<string>(type: "text", nullable: false),
                    Category      = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SubCategory   = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description   = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SpecGrade     = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PackagingSize = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ImageUrl      = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProductUrl    = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive      = table.Column<bool>(type: "boolean", nullable: false),
                    ScrapedAt     = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonLiquiMolyProducts", x => x.ArticleNumber);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NeonLiquiMolyProducts_Category",
                table: "NeonLiquiMolyProducts",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_NeonLiquiMolyProducts_IsActive",
                table: "NeonLiquiMolyProducts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_NeonLiquiMolyProducts_ScrapedAt",
                table: "NeonLiquiMolyProducts",
                column: "ScrapedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NeonLiquiMolyProducts");
        }
    }
}
