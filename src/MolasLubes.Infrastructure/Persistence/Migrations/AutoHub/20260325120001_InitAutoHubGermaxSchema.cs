using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.AutoHub
{
    /// <inheritdoc />
    public partial class InitAutoHubGermaxSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "neon_germax_products",
                columns: table => new
                {
                    item_code             = table.Column<string>(type: "character varying(50)",   maxLength: 50,   nullable: false),
                    item_name             = table.Column<string>(type: "text",                                    nullable: false),
                    item_group_name       = table.Column<string>(type: "character varying(100)",  maxLength: 100,  nullable: true),
                    engine_code           = table.Column<string>(type: "character varying(100)",  maxLength: 100,  nullable: true),
                    germax_article_number = table.Column<string>(type: "character varying(50)",   maxLength: 50,   nullable: true),
                    oem_part_number       = table.Column<string>(type: "text",                                    nullable: true),
                    fit_for_auto          = table.Column<string>(type: "text",                                    nullable: true),
                    description           = table.Column<string>(type: "text",                                    nullable: true),
                    image_url             = table.Column<string>(type: "character varying(500)",  maxLength: 500,  nullable: true),
                    all_image_urls        = table.Column<string>(type: "text",                                    nullable: true),
                    product_url           = table.Column<string>(type: "character varying(500)",  maxLength: 500,  nullable: true),
                    match_method          = table.Column<string>(type: "character varying(50)",   maxLength: 50,   nullable: true),
                    match_score           = table.Column<decimal>(type: "numeric(5,2)",                           nullable: true),
                    scraped_at            = table.Column<DateTime>(type: "timestamp with time zone",              nullable: true),
                    last_sap_seed_at      = table.Column<DateTime>(type: "timestamp with time zone",              nullable: false),
                    is_active             = table.Column<bool>(type: "boolean",                                   nullable: false, defaultValue: true),
                    scrape_status         = table.Column<string>(type: "character varying(20)",   maxLength: 20,   nullable: true),
                    scrape_error          = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_neon_germax_products", x => x.item_code);
                });

            migrationBuilder.CreateIndex(
                name: "ix_neon_germax_products_germax_article_number",
                table: "neon_germax_products",
                column: "germax_article_number");

            migrationBuilder.CreateIndex(
                name: "ix_neon_germax_products_item_group_name",
                table: "neon_germax_products",
                column: "item_group_name");

            migrationBuilder.CreateIndex(
                name: "ix_neon_germax_products_engine_code",
                table: "neon_germax_products",
                column: "engine_code");

            migrationBuilder.CreateIndex(
                name: "ix_neon_germax_products_scraped_at",
                table: "neon_germax_products",
                column: "scraped_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "neon_germax_products");
        }
    }
}
