using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCacheLiquiMolyProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheLiquiMolyProducts",
                columns: table => new
                {
                    ArticleNumber = table.Column<string>(type: "nvarchar(20)",  maxLength: 20,   nullable: false),
                    Name          = table.Column<string>(type: "nvarchar(max)",               nullable: false),
                    Category      = table.Column<string>(type: "nvarchar(100)", maxLength: 100,  nullable: true),
                    SubCategory   = table.Column<string>(type: "nvarchar(100)", maxLength: 100,  nullable: true),
                    Description   = table.Column<string>(type: "nvarchar(2000)",maxLength: 2000, nullable: true),
                    SpecGrade     = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,   nullable: true),
                    PackagingSize = table.Column<string>(type: "nvarchar(30)",  maxLength: 30,   nullable: true),
                    ImageUrl      = table.Column<string>(type: "nvarchar(500)", maxLength: 500,  nullable: true),
                    ProductUrl    = table.Column<string>(type: "nvarchar(500)", maxLength: 500,  nullable: true),
                    IsActive      = table.Column<bool>(type: "bit", nullable: false),
                    ScrapedAt     = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheLiquiMolyProducts", x => x.ArticleNumber);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CacheLiquiMolyProducts_Category",
                table: "CacheLiquiMolyProducts",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_CacheLiquiMolyProducts_IsActive",
                table: "CacheLiquiMolyProducts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CacheLiquiMolyProducts_ScrapedAt",
                table: "CacheLiquiMolyProducts",
                column: "ScrapedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CacheLiquiMolyProducts");
        }
    }
}
