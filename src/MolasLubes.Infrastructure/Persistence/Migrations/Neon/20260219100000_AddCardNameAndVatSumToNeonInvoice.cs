using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <inheritdoc />
    public partial class AddCardNameAndVatSumToNeonInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use IF NOT EXISTS so this migration is idempotent.
            // The Designer.cs was originally missing, which caused EF Core to skip
            // this migration entirely — the columns were never added.  Even if the
            // migration ID is already in __EFMigrationsHistory, running the app after
            // a manual delete of that row (or a fresh DB) will safely add the columns.
            migrationBuilder.Sql(@"
ALTER TABLE ""NeonInvoices""
    ADD COLUMN IF NOT EXISTS ""CardName"" character varying(100);

ALTER TABLE ""NeonInvoices""
    ADD COLUMN IF NOT EXISTS ""VatSum"" numeric(18,2) NOT NULL DEFAULT 0;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardName",
                table: "NeonInvoices");

            migrationBuilder.DropColumn(
                name: "VatSum",
                table: "NeonInvoices");
        }
    }
}
