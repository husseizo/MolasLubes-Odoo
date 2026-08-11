using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.AutoHub
{
    /// <inheritdoc />
    public partial class AddTantivyScrapedTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "neon_tantivy_scraped",
                columns: table => new
                {
                    item_code          = table.Column<string>(type: "character varying(50)",  maxLength: 50,   nullable: false),
                    article_no         = table.Column<string>(type: "character varying(100)", maxLength: 100,  nullable: true),
                    brand              = table.Column<string>(type: "character varying(50)",  maxLength: 50,   nullable: true),
                    part_name          = table.Column<string>(type: "text",                                    nullable: true),
                    specifications     = table.Column<string>(type: "text",                                    nullable: true),
                    reference_numbers  = table.Column<string>(type: "text",                                    nullable: true),
                    applications       = table.Column<string>(type: "text",                                    nullable: true),
                    product_url        = table.Column<string>(type: "character varying(500)", maxLength: 500,  nullable: true),
                    image_url          = table.Column<string>(type: "character varying(500)", maxLength: 500,  nullable: true),
                    scrape_status      = table.Column<string>(type: "character varying(20)",  maxLength: 20,   nullable: false, defaultValue: "PENDING"),
                    scrape_error       = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    scraped_at         = table.Column<DateTime>(type: "timestamp with time zone",              nullable: true),
                    last_seed_at       = table.Column<DateTime>(type: "timestamp with time zone",              nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_neon_tantivy_scraped", x => x.item_code);
                });

            migrationBuilder.CreateIndex(
                name: "ix_neon_tantivy_scraped_brand",
                table: "neon_tantivy_scraped",
                column: "brand");

            migrationBuilder.CreateIndex(
                name: "ix_neon_tantivy_scraped_scrape_status",
                table: "neon_tantivy_scraped",
                column: "scrape_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "neon_tantivy_scraped");
        }
    }
}
