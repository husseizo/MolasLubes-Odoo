using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeviceTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheNotificationDeviceTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Platform = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeviceToken = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SapUserCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BundleId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastPushedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheNotificationDeviceTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CacheNotificationTokens_Device",
                table: "CacheNotificationDeviceTokens",
                columns: new[] { "Platform", "DeviceToken", "BundleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CacheNotificationTokens_UserActive",
                table: "CacheNotificationDeviceTokens",
                columns: new[] { "SapUserCode", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CacheNotificationDeviceTokens");
        }
    }
}
