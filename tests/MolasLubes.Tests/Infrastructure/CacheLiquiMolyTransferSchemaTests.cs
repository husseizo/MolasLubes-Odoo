using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Tests.Infrastructure;

/// <summary>
/// Integration tests that guard against code/schema drift on CacheLiquiMolyTransfers.
///
/// Environment variables:
///   MOLASLUBES_CACHE_DB_CONN          — SQL Server connection string for MolasCacheDb.
///   MOLASLUBES_REQUIRE_CACHE_DB_TESTS — Set to "true" to fail (not skip) when the
///                                       connection string is absent. Use this in CI
///                                       release-verification pipelines.
///
/// Skip/fail semantics:
///   Connection string present                          → run integration tests normally
///   Connection string absent + REQUIRE flag false/unset → genuinely SKIP (xUnit Skipped)
///   Connection string absent + REQUIRE flag true        → FAIL with actionable message
///
/// To run locally:
///   $env:MOLASLUBES_CACHE_DB_CONN = "Server=.;Database=MolasCacheDb;User Id=sa;Password=...;TrustServerCertificate=True"
///   dotnet test --filter "FullyQualifiedName~CacheLiquiMolyTransferSchema"
/// </summary>
public class CacheLiquiMolyTransferSchemaTests
{
    // All columns the EF model expects on CacheLiquiMolyTransfers.
    // When new columns are added to CacheLiquiMolyTransfer.cs + migration, add them here.
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

    // ----------------------------------------------------------------
    // Environment / connection helpers — shared by all three tests
    // ----------------------------------------------------------------

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("MOLASLUBES_CACHE_DB_CONN");

    private static bool RequireDb =>
        string.Equals(
            Environment.GetEnvironmentVariable("MOLASLUBES_REQUIRE_CACHE_DB_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the connection string when available, otherwise:
    ///   • Calls Skip.If(true, …) when REQUIRE flag is false/unset → xUnit reports Skipped
    ///   • Calls Assert.Fail(…)    when REQUIRE flag is true        → xUnit reports Failed
    ///
    /// Each test must carry [SkippableFact]; [Fact] would treat SkipException as a failure.
    /// Both branches always throw; the trailing throw satisfies the compiler.
    /// </summary>
    private static string RequireConnectionString()
    {
        var cs = ConnectionString;
        if (cs is not null)
            return cs;

        const string message =
            "MOLASLUBES_CACHE_DB_CONN is not set. " +
            "Set it to 'Server=.;Database=MolasCacheDb;User Id=...;Password=...;TrustServerCertificate=True' " +
            "to run DB integration tests. " +
            "Set MOLASLUBES_REQUIRE_CACHE_DB_TESTS=true to make this absence a hard failure in CI.";

        if (RequireDb)
            Assert.Fail(message);    // XunitException → test reported as Failed
        else
            Skip.If(true, message);  // SkipException  → test reported as Skipped

        throw new InvalidOperationException("unreachable — both branches always throw");
    }

    // ----------------------------------------------------------------
    // Tests
    // ----------------------------------------------------------------

    [SkippableFact]
    public void AllExpectedColumns_ExistOnLiveDatabase()
    {
        var cs     = RequireConnectionString();
        var actual = GetActualColumns(cs);

        var missing = ExpectedColumns
            .Where(e => !actual.ContainsKey(e.Column))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"CacheLiquiMolyTransfers is missing {missing.Count} column(s) on the live database — " +
            $"migration not applied? Missing: {string.Join(", ", missing.Select(m => m.Column))}");
    }

    [SkippableFact]
    public void AllExpectedColumns_HaveCorrectDataType()
    {
        var cs        = RequireConnectionString();
        var actual    = GetActualColumns(cs);
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

    /// <summary>
    /// Uses EF Core's actual runtime migration discovery to verify that the live
    /// MolasCacheDb has no pending migrations.
    ///
    /// MolasCacheDbContext is now in MolasLubes.Persistence (no COM references),
    /// so dotnet test can reference it directly. GetPendingMigrations() compares
    /// the Migration-derived types found in the Persistence assembly against the
    /// rows in __EFMigrationsHistory — identical to what Database.Migrate() uses
    /// at startup, and automatically inclusive of any future migrations.
    /// </summary>
    [SkippableFact]
    public void NoPendingMigrations_OnLiveDatabase()
    {
        var cs = RequireConnectionString();

        var options = new DbContextOptionsBuilder<MolasCacheDbContext>()
            .UseSqlServer(cs)
            .Options;

        using var ctx = new MolasCacheDbContext(options);

        var pending = ctx.Database.GetPendingMigrations().ToList();

        Assert.True(
            pending.Count == 0,
            $"Live MolasCacheDb has {pending.Count} pending migration(s):\n" +
            string.Join("\n", pending));
    }

    // ----------------------------------------------------------------
    // Infrastructure helpers
    // ----------------------------------------------------------------

    private static Dictionary<string, string> GetActualColumns(string connectionString)
    {
        const string sql = """
            SELECT COLUMN_NAME, DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'CacheLiquiMolyTransfers'
            """;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            result[rdr.GetString(0)] = rdr.GetString(1);

        return result;
    }
}
