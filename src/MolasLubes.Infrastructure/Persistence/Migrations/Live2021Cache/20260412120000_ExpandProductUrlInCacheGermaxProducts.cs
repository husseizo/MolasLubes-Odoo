using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Live2021Cache
{
    /// <inheritdoc />
    public partial class ExpandProductUrlInCacheGermaxProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ProductUrl",
                table: "CacheGermaxProducts",
                type: "nvarchar(max)",
                nullable: true,
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ProductUrl",
                table: "CacheGermaxProducts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
