using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Live2021Cache
{
    /// <inheritdoc />
    public partial class InitLive2021GermaxSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheGermaxProducts",
                columns: table => new
                {
                    ItemCode            = table.Column<string>(type: "nvarchar(50)",   maxLength: 50,   nullable: false),
                    ItemName            = table.Column<string>(type: "nvarchar(255)",  maxLength: 255,  nullable: false),
                    ItemGroupName       = table.Column<string>(type: "nvarchar(100)",  maxLength: 100,  nullable: true),
                    EngineCode          = table.Column<string>(type: "nvarchar(100)",  maxLength: 100,  nullable: true),
                    GermaxArticleNumber = table.Column<string>(type: "nvarchar(50)",   maxLength: 50,   nullable: true),
                    OemPartNumber       = table.Column<string>(type: "nvarchar(255)",  maxLength: 255,  nullable: true),
                    FitForAuto          = table.Column<string>(type: "nvarchar(max)",               nullable: true),
                    Description         = table.Column<string>(type: "nvarchar(max)",               nullable: true),
                    ImageUrl            = table.Column<string>(type: "nvarchar(500)",  maxLength: 500,  nullable: true),
                    AllImageUrls        = table.Column<string>(type: "nvarchar(max)",               nullable: true),
                    ProductUrl          = table.Column<string>(type: "nvarchar(500)",  maxLength: 500,  nullable: true),
                    MatchMethod         = table.Column<string>(type: "nvarchar(50)",   maxLength: 50,   nullable: true),
                    MatchScore          = table.Column<decimal>(type: "decimal(5,2)",               nullable: true),
                    ScrapedAt           = table.Column<DateTime>(type: "datetime2",                 nullable: true),
                    LastSapSeedAt       = table.Column<DateTime>(type: "datetime2",                 nullable: false),
                    IsActive            = table.Column<bool>(type: "bit",                           nullable: false, defaultValue: true),
                    ScrapeStatus        = table.Column<string>(type: "nvarchar(20)",   maxLength: 20,   nullable: true),
                    ScrapeError         = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheGermaxProducts", x => x.ItemCode);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CacheGermaxProducts_GermaxArticleNumber",
                table: "CacheGermaxProducts",
                column: "GermaxArticleNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CacheGermaxProducts_ItemGroupName",
                table: "CacheGermaxProducts",
                column: "ItemGroupName");

            migrationBuilder.CreateIndex(
                name: "IX_CacheGermaxProducts_EngineCode",
                table: "CacheGermaxProducts",
                column: "EngineCode");

            migrationBuilder.CreateIndex(
                name: "IX_CacheGermaxProducts_ScrapedAt",
                table: "CacheGermaxProducts",
                column: "ScrapedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CacheGermaxProducts");
        }
    }
}
