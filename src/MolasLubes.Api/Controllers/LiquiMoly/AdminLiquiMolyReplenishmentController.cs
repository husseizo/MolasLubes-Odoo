using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Application.LiquiMolyReplenishment;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Security;
using MolasLubes.Infrastructure.Services.LiquiMolyReplenishment;

namespace MolasLubes.Api.Controllers.LiquiMoly;

[ApiController]
[Route("api/admin/liquimoly/replenishment")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminLiquiMolyReplenishmentController : ControllerBase
{
    private readonly LiquiMolyReplenishmentService          _service;
    private readonly LiquiMolyReplenishmentExecutionService _execService;
    private readonly LiquiMolyReplenishmentAnalyzer         _analyzer;
    private readonly LiquiMolyRoleService                   _roleService;
    private readonly SapWarehouseReader                     _warehouseReader;

    public AdminLiquiMolyReplenishmentController(
        LiquiMolyReplenishmentService          service,
        LiquiMolyReplenishmentExecutionService execService,
        LiquiMolyReplenishmentAnalyzer         analyzer,
        LiquiMolyRoleService                   roleService,
        SapWarehouseReader                     warehouseReader)
    {
        _service         = service;
        _execService     = execService;
        _analyzer        = analyzer;
        _roleService     = roleService;
        _warehouseReader = warehouseReader;
    }

    // ── Generate Draft ────────────────────────────────────────────────

    /// <summary>
    /// Analyze demand and create a DRAFT replenishment request.
    /// Only items with SuggestedQty > 0 become lines in the draft.
    /// The full recommendation report (all items) is returned alongside the ref.
    /// </summary>
    [HttpPost("generate-draft")]
    public async Task<IActionResult> GenerateDraft(
        [FromBody] GenerateReplenishmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var (requestRef, rows) = await _service.GenerateDraftAsync(request, ct);
            return Ok(new { requestRef, rowCount = rows.Count, rows });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (ArgumentException ex)           { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex)   { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    // ── Submit for Approval ───────────────────────────────────────────

    [HttpPost("{requestRef}/submit")]
    public async Task<IActionResult> Submit(
        string requestRef,
        [FromBody] SubmitReplenishmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var header = await _service.SubmitForApprovalAsync(requestRef, request, ct);
            return Ok(new { header.RequestRef, header.Status, header.SubmittedAt });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex)        { return NotFound(ex.Message); }
        catch (InvalidOperationException ex)   { return BadRequest(ex.Message); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    // ── Approve ───────────────────────────────────────────────────────

    [HttpPost("{requestRef}/approve")]
    public async Task<IActionResult> Approve(
        string requestRef,
        [FromBody] ApproveReplenishmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var header = await _service.ApproveAsync(requestRef, request, ct);
            return Ok(new
            {
                header.RequestRef,
                header.Status,
                header.ApprovedBySapUser,
                header.ApprovedAt,
                lineCount = header.Lines.Count
            });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex)        { return NotFound(ex.Message); }
        catch (InvalidOperationException ex)   { return BadRequest(ex.Message); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    // ── Reject ────────────────────────────────────────────────────────

    [HttpPost("{requestRef}/reject")]
    public async Task<IActionResult> Reject(
        string requestRef,
        [FromBody] RejectReplenishmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var header = await _service.RejectAsync(requestRef, request, ct);
            return Ok(new
            {
                header.RequestRef,
                header.Status,
                header.RejectedBySapUser,
                header.RejectedAt,
                header.RejectionReason
            });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex)        { return NotFound(ex.Message); }
        catch (InvalidOperationException ex)   { return BadRequest(ex.Message); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    // ── Execute ───────────────────────────────────────────────────────

    [HttpPost("{requestRef}/execute")]
    public async Task<IActionResult> Execute(
        string requestRef,
        [FromBody] ExecuteReplenishmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _execService.ExecuteApprovedRequestAsync(requestRef, request, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex)        { return NotFound(ex.Message); }
        catch (InvalidOperationException ex)   { return BadRequest(ex.Message); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    /// <summary>
    /// Executes an APPROVED replenishment request via inter-company SO → PO → GR flow.
    /// Phase 1: Creates Sales Order in MolasLubes (customer SHP00118).
    /// Phase 2: Creates Purchase Order in AutoHub (vendor SUP00001).
    /// Phase 3: Creates Goods Receipt PO in AutoHub against the PO.
    /// </summary>
    [HttpPost("{requestRef}/execute-intercompany")]
    public async Task<IActionResult> ExecuteInterCompany(
        string requestRef,
        [FromBody] ExecuteReplenishmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _execService.ExecuteWithSalesAndPurchaseAsync(requestRef, request, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex)        { return NotFound(ex.Message); }
        catch (InvalidOperationException ex)   { return BadRequest(ex.Message); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    /// <summary>
    /// Retry GR posting for a PARTIAL or FAILED request (delegates to transfer retry-receipt).
    /// </summary>
    [HttpPost("{requestRef}/retry")]
    public async Task<IActionResult> Retry(
        string requestRef,
        [FromBody] ExecuteReplenishmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _execService.RetryExecutionAsync(requestRef, request, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex)        { return NotFound(ex.Message); }
        catch (InvalidOperationException ex)   { return BadRequest(ex.Message); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    // ── Draft line edits ──────────────────────────────────────────────

    /// <summary>
    /// Atomically applies SET_QTY and DELETE_LINE operations to a DRAFT request.
    /// Uses optimistic concurrency via expectedVersion.
    /// </summary>
    [HttpPost("{requestRef}/draft-lines/apply")]
    public async Task<IActionResult> ApplyDraftLines(
        string requestRef,
        [FromBody] DraftLineApplyRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.ApplyDraftLinesAsync(requestRef, request, ct);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (KeyNotFoundException ex)        { return NotFound(ex.Message); }
        catch (ArgumentException ex)           { return UnprocessableEntity(new { message = "Validation failed.", errors = new { operations = ex.Message } }); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("DRAFT_VERSION_CONFLICT"))
        {
            var parts = ex.Message.Split('|');
            var detail = parts.Length > 1 ? parts[1] : ex.Message;
            // Re-read current version for the 409 response
            var current = await _service.GetAsync(requestRef, ct);
            return Conflict(new { message = "Draft version mismatch.", code = "DRAFT_VERSION_CONFLICT", currentVersion = current?.Version });
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("DRAFT_STATUS_CONFLICT"))
        {
            return Conflict(new { message = "Request status mismatch.", code = "DRAFT_STATUS_CONFLICT" });
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex)                 { return StatusCode(500, ex.Message); }
    }

    // ── Warehouse options ─────────────────────────────────────────────

    /// <summary>
    /// Returns active warehouses from SAP for source and target profiles.
    /// Frontend uses this to populate warehouse selectors in the create flow.
    /// </summary>
    [HttpGet("warehouse-options")]
    public IActionResult GetWarehouseOptions(
        [FromQuery] string actorSapUserCode,
        [FromQuery] string sourceProfile = "MolasLubes",
        [FromQuery] string targetProfile = "AutoHub")
    {
        try
        {
            _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Planner);
            var result = _warehouseReader.GetWarehouseOptions(sourceProfile, targetProfile);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    // ── Read ──────────────────────────────────────────────────────────

    [HttpGet("{requestRef}")]
    public async Task<IActionResult> Get(
        string requestRef,
        [FromQuery] string actorSapUserCode = "",
        CancellationToken ct = default)
    {
        try { _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }

        var header = await _service.GetAsync(requestRef, ct);
        return header == null ? NotFound() : Ok(ToRequestResponse(header));
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string? status          = null,
        [FromQuery] int skip                = 0,
        [FromQuery] int take                = 50,
        CancellationToken ct = default)
    {
        try { _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }

        try
        {
            var (items, lineCounts, hasMore) = await _service.ListSummariesAsync(status, skip, take, ct);
            return Ok(new
            {
                items = items.Select(item => ToRequestSummaryResponse(item, lineCounts.GetValueOrDefault(item.Id, 0))),
                hasMore,
                count = items.Count
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(499, "Request was canceled.");
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, "Timed out while loading replenishment requests.");
        }
    }

    /// <summary>
    /// Mobile compatibility fallback for executed replenishment history.
    /// Returns 200 with rows: [] when no executed requests are found.
    /// </summary>
    [HttpGet("executed")]
    public async Task<IActionResult> Executed(
        [FromQuery] string actorSapUserCode,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorSapUserCode))
            return BadRequest(new { message = "actorSapUserCode is required." });

        try { _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }

        try
        {
            var (items, _, hasMore) = await _service.ListSummariesAsync("EXECUTED", skip, take, ct);
            var rows = items.Select(r => new
            {
                r.RequestRef,
                r.Status,
                r.TransferRef,
                r.GoodsIssueDocNum,
                r.GoodsIssueDocEntry,
                SalesOrderDocNum = r.SalesOrderDocNum?.ToString(),
                r.SalesOrderDocEntry,
                PurchaseOrderDocNum = r.PurchaseOrderDocNum?.ToString(),
                r.PurchaseOrderDocEntry,
                r.GoodsReceiptDocNum,
                r.GoodsReceiptDocEntry,
                r.ExecutedBySapUser,
                r.ExecutedAt,
                r.SourceWarehouse,
                r.TargetWarehouse
            });

            return Ok(new { rows, hasMore });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(499, "Request was canceled.");
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, "Timed out while loading executed replenishment requests.");
        }
    }

    private static object ToRequestResponse(CacheLiquiMolyReplenishmentRequest header) => new
    {
        header.Id,
        header.RequestRef,
        header.Version,
        header.SourceProfile,
        header.TargetProfile,
        header.SourceWarehouse,
        header.TargetWarehouse,
        header.Status,
        header.RequestedBySapUser,
        header.ApprovedBySapUser,
        header.RejectedBySapUser,
        header.ExecutedBySapUser,
        header.CreatedAt,
        header.SubmittedAt,
        header.ApprovedAt,
        header.RejectedAt,
        header.ExecutedAt,
        header.Comments,
        header.RejectionReason,
        header.TransferRef,
        header.GoodsIssueDocEntry,
        header.GoodsIssueDocNum,
        header.GoodsReceiptDocEntry,
        header.GoodsReceiptDocNum,
        header.ExecutionMode,
        header.SalesOrderDocEntry,
        header.SalesOrderDocNum,
        header.PurchaseOrderDocEntry,
        header.PurchaseOrderDocNum,
        header.ErrorMessage,
        Lines = header.Lines
            .OrderBy(l => l.Id)
            .Select(ToLineResponse)
            .ToList()
    };

    private static object ToRequestSummaryResponse(CacheLiquiMolyReplenishmentRequest header, int lineCount) => new
    {
        header.Id,
        header.RequestRef,
        header.Version,
        header.SourceProfile,
        header.TargetProfile,
        header.SourceWarehouse,
        header.TargetWarehouse,
        header.Status,
        header.RequestedBySapUser,
        header.ApprovedBySapUser,
        header.RejectedBySapUser,
        header.ExecutedBySapUser,
        header.CreatedAt,
        header.SubmittedAt,
        header.ApprovedAt,
        header.RejectedAt,
        header.ExecutedAt,
        header.Comments,
        header.RejectionReason,
        header.TransferRef,
        header.GoodsIssueDocNum,
        header.GoodsReceiptDocNum,
        header.ExecutionMode,
        header.SalesOrderDocNum,
        header.PurchaseOrderDocNum,
        header.ErrorMessage,
        lineCount
    };

    private static object ToLineResponse(CacheLiquiMolyReplenishmentRequestLine line) => new
    {
        line.Id,
        line.RequestId,
        line.SourceItemCode,
        line.TargetItemCode,
        line.ArticleNumber,
        line.ItemName,
        line.CurrentStockTarget,
        line.AvailableSupplierStock,
        line.QtySold30d,
        line.QtySold60d,
        line.QtySold90d,
        line.AvgDailySales30d,
        line.DaysOfStock,
        line.SuggestedQty,
        line.TrendCategory,
        line.Priority,
        line.ApprovedQty,
        line.ExecutionStatus,
        line.ExecutionMessage
    };
}

// ── Report endpoints (separate route prefix) ─────────────────────────────────

[ApiController]
[Route("api/admin/liquimoly/reports")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminLiquiMolyReportsController : ControllerBase
{
    private readonly LiquiMolyReplenishmentAnalyzer    _analyzer;
    private readonly LiquiMolyReplenishmentService     _service;
    private readonly LiquiMolyRoleService              _roleService;
    private readonly SapLiquiMolySalesOrderReportReader _soReader;

    public AdminLiquiMolyReportsController(
        LiquiMolyReplenishmentAnalyzer     analyzer,
        LiquiMolyReplenishmentService      service,
        LiquiMolyRoleService               roleService,
        SapLiquiMolySalesOrderReportReader soReader)
    {
        _analyzer    = analyzer;
        _service     = service;
        _roleService = roleService;
        _soReader    = soReader;
    }

    /// <summary>Full recommendation report — all LM items with demand metrics and trend.</summary>
    [HttpGet("recommendations")]
    public IActionResult Recommendations(
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string sourceProfile    = "MolasLubes",
        [FromQuery] string targetProfile    = "AutoHub",
        [FromQuery] string sourceWarehouse  = "",
        [FromQuery] string targetWarehouse  = "",
        [FromQuery] int    targetDays       = 30)
    {
        if (string.IsNullOrWhiteSpace(sourceWarehouse)) return BadRequest("sourceWarehouse is required.");
        if (string.IsNullOrWhiteSpace(targetWarehouse)) return BadRequest("targetWarehouse is required.");

        try
        {
            _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer);
            var rows = _analyzer.Analyze(sourceProfile, targetProfile, sourceWarehouse, targetWarehouse, targetDays);
            return Ok(new { count = rows.Count, rows });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    /// <summary>Items with stock on hand but zero sales in 90 days.</summary>
    [HttpGet("dead-stock")]
    public IActionResult DeadStock(
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string sourceProfile    = "MolasLubes",
        [FromQuery] string targetProfile    = "AutoHub",
        [FromQuery] string sourceWarehouse  = "",
        [FromQuery] string targetWarehouse  = "")
    {
        if (string.IsNullOrWhiteSpace(sourceWarehouse)) return BadRequest("sourceWarehouse is required.");
        if (string.IsNullOrWhiteSpace(targetWarehouse)) return BadRequest("targetWarehouse is required.");

        try
        {
            _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer);
            var rows = _analyzer.Analyze(sourceProfile, targetProfile, sourceWarehouse, targetWarehouse)
                .Where(r => r.TrendCategory == LiquiMolyTrendCategory.DeadStock)
                .ToList();
            return Ok(new { count = rows.Count, rows });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    /// <summary>Recent executed replenishment requests with GI/GR references.</summary>
    [HttpGet("executions")]
    public async Task<IActionResult> Executions(
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        try { _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }

        try
        {
            var (items, _, hasMore) = await _service.ListSummariesAsync("EXECUTED", skip, take, ct);
            var rows = items.Select(r => new
            {
                r.RequestRef,
                r.Status,
                r.TransferRef,
                r.GoodsIssueDocNum,
                r.GoodsReceiptDocNum,
                r.ExecutedBySapUser,
                r.ExecutedAt,
                r.SourceWarehouse,
                r.TargetWarehouse
            });
            return Ok(new { rows, hasMore });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(499, "Request was canceled.");
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, "Timed out while loading execution history.");
        }
    }

    /// <summary>
    /// Chronological approval/rejection history across both APPROVED and REJECTED records.
    /// skip/take operate over the combined result set, not per-status.
    /// </summary>
    [HttpGet("approvals")]
    public async Task<IActionResult> Approvals(
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        try { _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }

        // Include all statuses that carry an approval decision so execution
        // transitions (EXECUTING → EXECUTED/PARTIAL/FAILED) never erase the audit trail.
        try
        {
            var (items, _, hasMore) = await _service.ListSummaryByStatusesAsync(
                new[] { "APPROVED", "REJECTED", "EXECUTING", "EXECUTED", "PARTIAL", "FAILED" }, skip, take, ct);

            var rows = items.Select(r => new
            {
                r.RequestRef,
                r.Status,
                r.RequestedBySapUser,
                r.ApprovedBySapUser,
                r.RejectedBySapUser,
                r.RejectionReason,
                r.SubmittedAt,
                r.ApprovedAt,
                r.RejectedAt,
                r.Comments
            });

            return Ok(new { rows, hasMore, count = items.Count });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(499, "Request was canceled.");
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, "Timed out while loading approval history.");
        }
    }

    [HttpGet("sales-order-lines")]
    public IActionResult GetSalesOrderLines(
        [FromQuery] string  actorSapUserCode    = "",
        [FromQuery] string  profile             = "MolasLubes",
        [FromQuery] string? brand               = null,
        [FromQuery] DateOnly? dateFrom          = null,
        [FromQuery] DateOnly? dateTo            = null,
        [FromQuery] string  dateField           = "orderDate",
        [FromQuery] string  lineStatus          = "open",
        [FromQuery] string  fulfillmentStatus   = "notDelivered",
        [FromQuery] string? warehouse           = null,
        [FromQuery] string? customerCode        = null,
        [FromQuery] string? salesPersonCode     = null,
        [FromQuery] string? search              = null,
        [FromQuery] int     skip                = 0,
        [FromQuery] int     take                = 50,
        [FromQuery] string  sort                = "orderDate",
        [FromQuery] string  sortDirection       = "desc")
    {
        try { _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }

        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 500);

        try
        {
            var result = _soReader.GetSalesOrderLines(
                profile, brand, dateFrom, dateTo, dateField,
                lineStatus, fulfillmentStatus,
                warehouse, customerCode, salesPersonCode, search,
                skip, take, sort, sortDirection);

            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex)                 { return StatusCode(500, new { message = "Failed to load sales order lines.", detail = ex.Message }); }
    }

    [HttpGet("sales-order-lines/summary")]
    public IActionResult GetSalesOrderLinesSummary(
        [FromQuery] string  actorSapUserCode = "",
        [FromQuery] string  profile          = "MolasLubes",
        [FromQuery] string? brand            = null,
        [FromQuery] DateOnly? dateFrom       = null,
        [FromQuery] DateOnly? dateTo         = null,
        [FromQuery] string  dateField        = "orderDate",
        [FromQuery] string? warehouse        = null,
        [FromQuery] string? customerCode     = null,
        [FromQuery] string? salesPersonCode  = null,
        [FromQuery] string? search           = null)
    {
        try { _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, ex.Message); }

        try
        {
            var result = _soReader.GetSalesOrderLinesSummary(
                profile, brand, dateFrom, dateTo, dateField,
                warehouse, customerCode, salesPersonCode, search);

            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex)                 { return StatusCode(500, new { message = "Failed to load sales order lines summary.", detail = ex.Message }); }
    }
}
