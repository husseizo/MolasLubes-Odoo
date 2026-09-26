using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

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
///   Connection string present                         → run integration tests normally
///   Connection string absent + REQUIRE flag false/unset → genuinely SKIP (xUnit Skipped)
///   Connection string absent + REQUIRE flag true       → FAIL with actionable message
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
    ///   • Calls Assert.True(false, …) when REQUIRE flag is true   → xUnit reports Failed
    ///
    /// Each test method must carry [SkippableFact] (not [Fact]) for the skip to register
    /// correctly with the runner; [Fact] would treat SkipException as an unexpected failure.
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
            Skip.If(true, message);  // SkipException  → test reported as Skipped (requires [SkippableFact])

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
    /// Verifies that every migration present in the source tree is also recorded in
    /// __EFMigrationsHistory on the live database.
    ///
    /// Migration IDs are discovered by scanning the Infrastructure Migrations directory
    /// on disk (filename stem = migration ID by EF convention). This avoids maintaining
    /// a duplicate hard-coded list: any new migration file is automatically included.
    ///
    /// Note: MolasLubes.Infrastructure cannot be referenced as a ProjectReference from
    /// the test project because it has a COMReference (SAPbobsCOM) that CoreMSBuild
    /// cannot resolve. Filesystem scanning gives us the same dynamic discovery that
    /// context.Database.GetPendingMigrations() would provide.
    /// </summary>
    [SkippableFact]
    public void NoPendingMigrations_OnLiveDatabase()
    {
        var cs = RequireConnectionString();

        var codeIds = DiscoverMigrationIds();

        Assert.True(
            codeIds.Count > 0,
            "No migration files were found — the migrations directory path may be wrong. " +
            $"Looked in: {MigrationsDir}");

        var appliedIds = GetAppliedMigrationIds(cs);

        var pending = codeIds
            .Where(id => !appliedIds.Contains(id))
            .OrderBy(id => id)
            .ToList();

        Assert.True(
            pending.Count == 0,
            $"Live MolasCacheDb has {pending.Count} pending migration(s):\n" +
            string.Join("\n", pending));
    }

    // ----------------------------------------------------------------
    // Infrastructure helpers
    // ----------------------------------------------------------------

    private static string MigrationsDir
    {
        get
        {
            // Navigate from the test assembly output directory to the Infrastructure
            // Migrations folder in the source tree.
            //   AppContext.BaseDirectory = .../tests/MolasLubes.Tests/bin/<cfg>/<tfm>/
            //   Five ".." levels reach the repository root.
            var asmDir = AppContext.BaseDirectory;
            var root   = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
            return Path.Combine(root, "src", "MolasLubes.Infrastructure", "Migrations");
        }
    }

    private static IReadOnlyList<string> DiscoverMigrationIds()
    {
        var dir = MigrationsDir;
        if (!Directory.Exists(dir))
            return [];

        // EF migration filenames: <14-digit-timestamp>_<Name>.cs
        // Exclude Designer files (.Designer.cs → stem ends with "Designer" after the name)
        // and the ModelSnapshot file.
        return Directory.GetFiles(dir, "*.cs")
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .Where(stem =>
                Regex.IsMatch(stem, @"^\d{14}_") &&
                !stem.EndsWith("Designer",      StringComparison.OrdinalIgnoreCase) &&
                !stem.EndsWith("ModelSnapshot", StringComparison.OrdinalIgnoreCase))
            .OrderBy(stem => stem)
            .ToList();
    }

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

    private static HashSet<string> GetAppliedMigrationIds(string connectionString)
    {
        const string sql = "SELECT MigrationId FROM __EFMigrationsHistory";
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
            result.Add(rdr.GetString(0));

        return result;
    }
}
