using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddAutoHubDocumentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AutoHub data has been moved to the dedicated AutoHub PostgreSQL database.
            // Drop the table that migration 000002 created in this (MolasLubes) database.
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""NeonAutoHubProducts"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NeonAutoHubProducts",
                columns: table => new
                {
                    ItemCode      = table.Column<string>(type: "text", nullable: false),
                    ItemName      = table.Column<string>(type: "text", nullable: false),
                    OnHandSap     = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AvailableCache = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive      = table.Column<bool>(type: "boolean", nullable: false),
                    SyncedAt      = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NeonAutoHubProducts", x => x.ItemCode);
                });
        }
    }
}
