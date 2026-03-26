using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

namespace MolasLubes.Infrastructure.Services.Backfill;

/// <summary>
/// Orchestrates bulk UoM backfill: selects candidate items from SAP by filter,
/// then delegates to <see cref="InventoryCountingUomBackfillService"/> for the
/// preflight / apply logic.  Preflight and update logic is never duplicated here.
/// </summary>
public class BulkInventoryCountingUomBackfillService
{
    /// Hard maximum for a single bulk call regardless of what the caller requests.
    private const int MaxTake = 1000;

    private readonly SapItemSelector _selector;
    private readonly InventoryCountingUomBackfillService _backfill;
    private readonly ILogger<BulkInventoryCountingUomBackfillService> _logger;

    public BulkInventoryCountingUomBackfillService(
        SapItemSelector selector,
        InventoryCountingUomBackfillService backfill,
        ILogger<BulkInventoryCountingUomBackfillService> logger)
    {
        _selector = selector;
        _backfill = backfill;
        _logger   = logger;
    }

    /// <param name="dryRun">
    ///   true  = select + preflight only; no items are changed.
    ///   false = select + preflight + update OK_TO_UPDATE items.
    /// </param>
    public UomBackfillReport Run(BulkUomBackfillRequest request, bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(request.TargetUomCode))
            return UomBackfillReport.Failed("targetUomCode is required.", dryRun);

        // Clamp take to the hard maximum
        var take = Math.Min(request.Take <= 0 ? MaxTake : request.Take, MaxTake);
        var skip = Math.Max(request.Skip, 0);

        // ── 1. Select candidate item codes ────────────────────────────────
        SapItemSelectorResult selection;

        try
        {
            selection = _selector.SelectItemCodes(
                activeOnly:     request.ActiveOnly,
                itemGroupNames: request.ItemGroupNames,
                itemCodes:      request.ItemCodes,
                take:           take,
                skip:           skip,
                confirmAll:     request.ConfirmAll);
        }
        catch (InvalidOperationException ex)
        {
            return UomBackfillReport.Failed(ex.Message, dryRun);
        }

        if (selection.ItemCodes.Count == 0)
        {
            return new UomBackfillReport
            {
                DryRun         = dryRun,
                TargetUomCode  = request.TargetUomCode,
                Summary        = "NO_ITEMS_MATCHED",
                Selection      = BuildSelectionMeta(request, take, skip, 0, hasMore: false)
            };
        }

        _logger.LogInformation(
            "BulkUomBackfill: {Mode} | TargetUoM={Uom} | Items={Count} | HasMore={HasMore}",
            dryRun ? "DRY-RUN" : "APPLY",
            request.TargetUomCode, selection.ItemCodes.Count, selection.HasMore);

        // ── 2. Delegate to explicit-list service (preflight + optional apply) ─
        var report = _backfill.Run(selection.ItemCodes, request.TargetUomCode, dryRun);

        // ── 3. Attach selection metadata ──────────────────────────────────
        return report with
        {
            Selection = BuildSelectionMeta(request, take, skip, selection.ItemCodes.Count, selection.HasMore)
        };
    }

    private static UomSelectionMetadata BuildSelectionMeta(
        BulkUomBackfillRequest request,
        int take,
        int skip,
        int matched,
        bool hasMore) => new()
    {
        MatchedItems   = matched,
        Take           = take,
        Skip           = skip,
        ActiveOnly     = request.ActiveOnly,
        ItemGroupNames = request.ItemGroupNames,
        HasMore        = hasMore
    };
}

// ── Request model ────────────────────────────────────────

public class BulkUomBackfillRequest
{
    /// <summary>UoM code or name as it appears in OUOM (e.g. "Unit", "EA").</summary>
    public string TargetUomCode { get; set; } = string.Empty;

    /// <summary>Filter by SAP item group name (OITB.ItmsGrpNam). Optional.</summary>
    public List<string>? ItemGroupNames { get; set; }

    /// <summary>Further narrow to specific ItemCodes. Optional.</summary>
    public List<string>? ItemCodes { get; set; }

    /// <summary>Only include non-frozen items (OITM.frozenFor = 'N'). Default: true.</summary>
    public bool ActiveOnly { get; set; } = true;

    /// <summary>Max items to process in one call. Capped at 1000 server-side.</summary>
    public int Take { get; set; } = 500;

    /// <summary>Rows to skip for paging. Default: 0.</summary>
    public int Skip { get; set; } = 0;

    /// <summary>
    /// Must be true to run without any narrowing filter (itemGroupNames + itemCodes both empty).
    /// Protects against accidentally touching the entire item master.
    /// </summary>
    public bool ConfirmAll { get; set; } = false;
}
