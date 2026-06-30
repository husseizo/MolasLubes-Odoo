using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    public partial class AddNeonApiCache : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NeonApiCache",
                columns: table => new
                {
                    CacheKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: false),
                    CachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonApiCache", x => x.CacheKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NeonApiCache_ExpiresAt",
                table: "NeonApiCache",
                column: "ExpiresAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NeonApiCache");
        }
    }
}
