using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Migrations
{
    public partial class AddInternalUserManagement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── InternalUsers ──────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "InternalUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SapUserCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalUsers", x => x.Id);
                });

            // ── InternalUserTokens ─────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "InternalUserTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeviceHint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalUserTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InternalUserTokens_InternalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "InternalUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── BrandRoleMappings ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "BrandRoleMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Brand = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SapUserCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandRoleMappings", x => x.Id);
                });

            // ── AuthAuditEvents ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "AuthAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    ActorId = table.Column<int>(type: "int", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IpHint = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthAuditEvents", x => x.Id);
                });

            // ── CacheNotificationDeviceTokens: add InternalUserId ──────────
            migrationBuilder.AddColumn<int>(
                name: "InternalUserId",
                table: "CacheNotificationDeviceTokens",
                type: "int",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CacheNotificationDeviceTokens_InternalUsers_InternalUserId",
                table: "CacheNotificationDeviceTokens",
                column: "InternalUserId",
                principalTable: "InternalUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Indexes ────────────────────────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_InternalUsers_Username",
                table: "InternalUsers",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InternalUsers_SapUserCode",
                table: "InternalUsers",
                column: "SapUserCode");

            migrationBuilder.CreateIndex(
                name: "IX_InternalUserTokens_TokenHash",
                table: "InternalUserTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InternalUserTokens_UserId",
                table: "InternalUserTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BrandRoleMappings_Brand_Role",
                table: "BrandRoleMappings",
                columns: new[] { "Brand", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditEvents_OccurredAt",
                table: "AuthAuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_CacheNotificationTokens_InternalUserId",
                table: "CacheNotificationDeviceTokens",
                column: "InternalUserId");

            // ── Seed BrandRoleMappings ─────────────────────────────────────
            migrationBuilder.InsertData(
                table: "BrandRoleMappings",
                columns: new[] { "Brand", "Role", "SapUserCode" },
                values: new object[,]
                {
                    { "MolasLubes", "Inventory",  "manager"  },
                    { "MolasLubes", "Admin",       "hussein"  },
                    { "MolasLubes", "Supervisor",  "hussein"  },
                    { "MolasLubes", "Planner",     "hussein"  },
                    { "MolasLubes", "Executor",    "hussein"  },
                    { "MolasLubes", "Viewer",      "ajabuely" },
                    { "AutoHub",    "Inventory",   "manager"  },
                    { "AutoHub",    "Admin",        "hussein"  },
                    { "AutoHub",    "Supervisor",   "hussein"  },
                    { "AutoHub",    "Planner",      "hussein"  },
                    { "AutoHub",    "Executor",     "hussein"  },
                    { "AutoHub",    "Viewer",       "suleiman" }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CacheNotificationDeviceTokens_InternalUsers_InternalUserId",
                table: "CacheNotificationDeviceTokens");

            migrationBuilder.DropIndex(
                name: "IX_CacheNotificationTokens_InternalUserId",
                table: "CacheNotificationDeviceTokens");

            migrationBuilder.DropColumn(
                name: "InternalUserId",
                table: "CacheNotificationDeviceTokens");

            migrationBuilder.DropTable(name: "AuthAuditEvents");
            migrationBuilder.DropTable(name: "InternalUserTokens");
            migrationBuilder.DropTable(name: "BrandRoleMappings");
            migrationBuilder.DropTable(name: "InternalUsers");
        }
    }
}
