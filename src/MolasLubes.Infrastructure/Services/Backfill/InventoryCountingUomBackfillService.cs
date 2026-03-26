using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

namespace MolasLubes.Infrastructure.Services.Backfill;

/// <summary>
/// Orchestrates a two-pass process for setting DefaultCountingUoMEntry on SAP items.
///
/// Pass A (DryRun=true):  classifies every item, returns the report, makes no changes.
/// Pass B (DryRun=false): preflights then updates only OK_TO_UPDATE items.
///
/// UoMGroupEntry is never modified.  Items with an invalid or incompatible group are
/// recorded as FAIL_INVALID_UOM_GROUP / FAIL_TARGET_UOM_MISSING and left untouched.
/// </summary>
public class InventoryCountingUomBackfillService
{
    private readonly SapItemUomWriter _writer;
    private readonly ILogger<InventoryCountingUomBackfillService> _logger;

    public InventoryCountingUomBackfillService(
        SapItemUomWriter writer,
        ILogger<InventoryCountingUomBackfillService> logger)
    {
        _writer = writer;
        _logger = logger;
    }

    /// <param name="itemCodes">SAP ItemCodes to process.</param>
    /// <param name="targetUomCode">UoM code or name as it appears in OUOM (e.g. "EA", "Each").</param>
    /// <param name="dryRun">
    ///   true  = classify and report only — no items are changed.
    ///   false = classify then update all OK_TO_UPDATE items.
    /// </param>
    public UomBackfillReport Run(
        IReadOnlyList<string> itemCodes,
        string targetUomCode,
        bool dryRun)
    {
        _logger.LogInformation(
            "UomBackfill: started | Items={Count} | TargetUoM={Uom} | DryRun={Dry}",
            itemCodes.Count, targetUomCode, dryRun);

        // ── 1. Resolve target UoM once ────────────────────────────────────
        var targetUomEntry = _writer.ResolveUomEntry(targetUomCode);
        if (targetUomEntry == null)
        {
            _logger.LogError(
                "UomBackfill: target UoM '{Uom}' not found in OUOM — aborting", targetUomCode);
            return UomBackfillReport.Failed(
                $"Target UoM '{targetUomCode}' was not found in SAP (OUOM table). " +
                "Verify the code or name and retry.",
                dryRun);
        }

        // ── 2. Preflight every item ───────────────────────────────────────
        var preflightRows = new List<UomBackfillRow>(itemCodes.Count);

        foreach (var code in itemCodes)
        {
            var result = _writer.Preflight(code, targetUomEntry.Value);

            preflightRows.Add(new UomBackfillRow
            {
                ItemCode    = code,
                Outcome     = result.Outcome.ToString(),
                SapErrorCode    = null,
                SapErrorMessage = null
            });

            _logger.LogDebug(
                "UomBackfill: preflight | ItemCode={Code} | Outcome={Outcome} | Group={Group}",
                code, result.Outcome, result.GroupEntry);
        }

        // ── 3. Dry-run: return report without applying anything ───────────
        if (dryRun)
        {
            var dryReport = BuildReport(preflightRows, targetUomCode, targetUomEntry.Value, dryRun: true);
            _logger.LogInformation(
                "UomBackfill: dry-run complete | {Summary}", dryReport.Summary);
            return dryReport;
        }

        // ── 4. Apply pass: update only OK_TO_UPDATE items ─────────────────
        var finalRows = new List<UomBackfillRow>(itemCodes.Count);

        foreach (var row in preflightRows)
        {
            if (row.Outcome != ItemUomOutcome.OK_TO_UPDATE.ToString())
            {
                finalRows.Add(row); // keep preflight classification as-is
                continue;
            }

            var applyResult = _writer.Apply(row.ItemCode, targetUomEntry.Value);

            finalRows.Add(new UomBackfillRow
            {
                ItemCode        = row.ItemCode,
                Outcome         = applyResult.Outcome.ToString(),
                SapErrorCode    = applyResult.SapErrorCode,
                SapErrorMessage = applyResult.SapErrorMessage
            });
        }

        var report = BuildReport(finalRows, targetUomCode, targetUomEntry.Value, dryRun: false);
        _logger.LogInformation(
            "UomBackfill: apply complete | {Summary}", report.Summary);
        return report;
    }

    private static UomBackfillReport BuildReport(
        List<UomBackfillRow> rows,
        string targetUomCode,
        int targetUomEntry,
        bool dryRun)
    {
        var counts = rows
            .GroupBy(r => r.Outcome)
            .ToDictionary(g => g.Key, g => g.Count());

        return new UomBackfillReport
        {
            DryRun         = dryRun,
            TargetUomCode  = targetUomCode,
            TargetUomEntry = targetUomEntry,
            Rows           = rows,
            Totals         = counts,
            Summary        = string.Join(" | ", counts.Select(kv => $"{kv.Key}={kv.Value}"))
        };
    }
}

// ── Report model ────────────────────────────────────────

public class UomBackfillReport
{
    public bool DryRun { get; init; }
    public string TargetUomCode  { get; init; } = string.Empty;
    public int    TargetUomEntry { get; init; }
    public List<UomBackfillRow> Rows   { get; init; } = new();
    public Dictionary<string, int> Totals { get; init; } = new();
    public string Summary  { get; init; } = string.Empty;
    public string? Error   { get; init; }

    // Set by bulk endpoints; null for explicit-list endpoints.
    public UomSelectionMetadata? Selection { get; init; }

    public static UomBackfillReport Failed(string error, bool dryRun) => new()
    {
        DryRun  = dryRun,
        Error   = error,
        Summary = "FAILED"
    };
}

public class UomSelectionMetadata
{
    public int          MatchedItems    { get; init; }
    public int          Take            { get; init; }
    public int          Skip            { get; init; }
    public bool         ActiveOnly      { get; init; }
    public List<string>? ItemGroupNames { get; init; }
}

public class UomBackfillRow
{
    public string  ItemCode        { get; init; } = string.Empty;
    public string  Outcome         { get; init; } = string.Empty;
    public int?    SapErrorCode    { get; init; }
    public string? SapErrorMessage { get; init; }
}
