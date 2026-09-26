using Microsoft.Data.SqlClient;

namespace MolasLubes.Tests.Infrastructure;

/// <summary>
/// Integration tests that guard against code/schema drift on CacheLiquiMolyTransfers.
///
/// These tests connect to the live SQL Server database identified by the
/// MOLASLUBES_CACHE_DB_CONN environment variable. They are skipped automatically
/// when that variable is not set so that CI without a database does not fail.
///
/// To run locally:
///   $env:MOLASLUBES_CACHE_DB_CONN = "Server=.;Database=MolasCacheDb;User Id=sa;Password=...;TrustServerCertificate=True"
///   dotnet test --filter "FullyQualifiedName~CacheLiquiMolyTransferSchema"
/// </summary>
public class CacheLiquiMolyTransferSchemaTests
{
    // All columns the EF model expects to be present on CacheLiquiMolyTransfers.
    // When new columns are added to CacheLiquiMolyTransfer.cs (and a matching
    // migration is created), add them here too so the drift test stays accurate.
    private static readonly (string Column, string DataType)[] ExpectedColumns =
    [
        ("Id",                        "int"),
        ("TransferRef",               "nvarchar"),
        ("SourceProfile",             "nvarchar"),
        ("TargetProfile",             "nvarchar"),
        ("SourceWarehouse",           "nvarchar"),
        ("TargetWarehouse",           "nvarchar"),
        ("Comments",                  "nvarchar"),
        ("GoodsIssueDocEntry",        "int"),
        ("GoodsIssueDocNum",          "nvarchar"),
        ("GoodsReceiptDocEntry",      "int"),
        ("GoodsReceiptDocNum",        "nvarchar"),
        ("Status",                    "nvarchar"),
        ("ErrorMessage",              "nvarchar"),
        ("CreatedAt",                 "datetime2"),
        ("CompletedAt",               "datetime2"),
        ("ClientReference",           "nvarchar"),
        ("ActorSapUserCode",          "nvarchar"),
        ("BaseRequestDocEntry",       "int"),
        ("BaseRequestDocNum",         "nvarchar"),
        ("InventoryTransferDocEntry", "int"),
        ("InventoryTransferDocNum",   "nvarchar"),
    ];

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("MOLASLUBES_CACHE_DB_CONN");

    [Fact]
    public void AllExpectedColumns_ExistOnLiveDatabase()
    {
        if (ConnectionString is null)
        {
            // Not a failure — skip gracefully when running without a database.
            return;
        }

        var actual = GetActualColumns();

        var missing = ExpectedColumns
            .Where(e => !actual.ContainsKey(e.Column))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"CacheLiquiMolyTransfers is missing {missing.Count} column(s) on the live database — " +
            $"migration not applied? Missing: {string.Join(", ", missing.Select(m => m.Column))}");
    }

    [Fact]
    public void AllExpectedColumns_HaveCorrectDataType()
    {
        if (ConnectionString is null) return;

        var actual = GetActualColumns();
        var mismatched = new List<string>();

        foreach (var (col, expectedType) in ExpectedColumns)
        {
            if (!actual.TryGetValue(col, out var actualType)) continue; // caught by AllExpectedColumns test
            if (!actualType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
                mismatched.Add($"{col} (expected {expectedType}, got {actualType})");
        }

        Assert.True(
            mismatched.Count == 0,
            $"CacheLiquiMolyTransfers has type mismatches: {string.Join(", ", mismatched)}");
    }

    [Fact]
    public void NoPendingMigrations_OnLiveDatabase()
    {
        if (ConnectionString is null) return;

        // Read __EFMigrationsHistory and compare against the migration files
        // embedded in the assembly at compile time.
        // This test fails if a migration exists in code but is absent from the DB.
        var appliedIds = GetAppliedMigrationIds();

        // Known migrations for MolasCacheDb — update when adding new migrations.
        var expectedMigrationIds = new[]
        {
            "20260518090000_AddLiquiMolyProductBarcodeInfo",
            "20260515100000_AddLiquiMolyProductContentSections",
            "20260525085113_AddNotificationDeviceTokens",
            "20260623100000_AddEanCodeToLiquiMolyProducts",
            "20260625100000_AddInternalUserManagement",
            "20260925000001_AddTransferRequestLinkColumns",
        };

        var missing = expectedMigrationIds
            .Where(id => !appliedIds.Contains(id))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Live database is missing {missing.Count} migration(s) from __EFMigrationsHistory: " +
            string.Join(", ", missing));
    }

    private static Dictionary<string, string> GetActualColumns()
    {
        const string sql = """
            SELECT COLUMN_NAME, DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'CacheLiquiMolyTransfers'
            """;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            result[rdr.GetString(0)] = rdr.GetString(1);

        return result;
    }

    private static HashSet<string> GetAppliedMigrationIds()
    {
        const string sql = "SELECT MigrationId FROM __EFMigrationsHistory";
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            result.Add(rdr.GetString(0));

        return result;
    }
}
