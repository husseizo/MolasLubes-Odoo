using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MolasLubes.Infrastructure.Persistence.Migrations.Neon
{
    /// <summary>
    /// Safety-net migration: ensures CardName and VatSum exist on NeonInvoices
    /// even if 20260219100000 was recorded in __EFMigrationsHistory without
    /// the DDL ever running (the Designer.cs was missing, so EF Core skipped it).
    /// IF NOT EXISTS makes this a no-op when the columns are already present.
    /// </summary>
    public partial class EnsureNeonInvoiceCardNameVatSum : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""NeonInvoices""
    ADD COLUMN IF NOT EXISTS ""CardName"" character varying(100);

ALTER TABLE ""NeonInvoices""
    ADD COLUMN IF NOT EXISTS ""VatSum"" numeric(18,2) NOT NULL DEFAULT 0;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down is intentionally empty — the columns are required by the
            // NeonInvoice entity and must not be removed by a rollback of this
            // safety-net migration.
        }
    }
}
