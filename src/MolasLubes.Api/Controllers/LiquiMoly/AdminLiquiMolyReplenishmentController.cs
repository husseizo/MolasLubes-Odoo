using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Application.LiquiMolyReplenishment;
using MolasLubes.Domain.Entities.Cache;
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

    public AdminLiquiMolyReplenishmentController(
        LiquiMolyReplenishmentService          service,
        LiquiMolyReplenishmentExecutionService execService,
        LiquiMolyReplenishmentAnalyzer         analyzer,
        LiquiMolyRoleService                   roleService)
    {
        _service     = service;
        _execService = execService;
        _analyzer    = analyzer;
        _roleService = roleService;
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

        var (items, hasMore) = await _service.ListAsync(status, skip, take, ct);
        return Ok(new
        {
            items = items.Select(ToRequestSummaryResponse),
            hasMore,
            count = items.Count
        });
    }

    private static object ToRequestResponse(CacheLiquiMolyReplenishmentRequest header) => new
    {
        header.Id,
        header.RequestRef,
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

    private static object ToRequestSummaryResponse(CacheLiquiMolyReplenishmentRequest header) => new
    {
        header.Id,
        header.RequestRef,
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
        lineCount = header.Lines.Count
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
    private readonly LiquiMolyReplenishmentAnalyzer _analyzer;
    private readonly LiquiMolyReplenishmentService  _service;
    private readonly LiquiMolyRoleService           _roleService;

    public AdminLiquiMolyReportsController(
        LiquiMolyReplenishmentAnalyzer analyzer,
        LiquiMolyReplenishmentService  service,
        LiquiMolyRoleService           roleService)
    {
        _analyzer    = analyzer;
        _service     = service;
        _roleService = roleService;
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

        var (items, hasMore) = await _service.ListAsync("EXECUTED", skip, take, ct);
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
        var (items, hasMore) = await _service.ListByStatusesAsync(
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
}
