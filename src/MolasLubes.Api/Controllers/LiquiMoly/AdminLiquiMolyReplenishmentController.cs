using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Application.LiquiMolyReplenishment;
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

    public AdminLiquiMolyReplenishmentController(
        LiquiMolyReplenishmentService          service,
        LiquiMolyReplenishmentExecutionService execService,
        LiquiMolyReplenishmentAnalyzer         analyzer)
    {
        _service     = service;
        _execService = execService;
        _analyzer    = analyzer;
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
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
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
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
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
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
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
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
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
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
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
        catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
        catch (KeyNotFoundException ex)        { return NotFound(ex.Message); }
        catch (InvalidOperationException ex)   { return BadRequest(ex.Message); }
        catch (Exception ex)                   { return StatusCode(500, ex.Message); }
    }

    // ── Read ──────────────────────────────────────────────────────────

    [HttpGet("{requestRef}")]
    public async Task<IActionResult> Get(string requestRef, CancellationToken ct)
    {
        var header = await _service.GetAsync(requestRef, ct);
        return header == null ? NotFound() : Ok(header);
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var (items, hasMore) = await _service.ListAsync(status, skip, take, ct);
        return Ok(new { items, hasMore, count = items.Count });
    }
}

// ── Report endpoints (separate route prefix) ─────────────────────────────────

[ApiController]
[Route("api/admin/liquimoly/reports")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminLiquiMolyReportsController : ControllerBase
{
    private readonly LiquiMolyReplenishmentAnalyzer _analyzer;
    private readonly LiquiMolyReplenishmentService  _service;

    public AdminLiquiMolyReportsController(
        LiquiMolyReplenishmentAnalyzer analyzer,
        LiquiMolyReplenishmentService  service)
    {
        _analyzer = analyzer;
        _service  = service;
    }

    /// <summary>Full recommendation report — all LM items with demand metrics and trend.</summary>
    [HttpGet("recommendations")]
    public IActionResult Recommendations(
        [FromQuery] string sourceProfile   = "MolasLubes",
        [FromQuery] string targetProfile   = "AutoHub",
        [FromQuery] string targetWarehouse = "",
        [FromQuery] int    targetDays      = 30)
    {
        if (string.IsNullOrWhiteSpace(targetWarehouse))
            return BadRequest("targetWarehouse is required.");

        try
        {
            var rows = _analyzer.Analyze(sourceProfile, targetProfile, targetWarehouse, targetDays);
            return Ok(new { count = rows.Count, rows });
        }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    /// <summary>Items with stock on hand but zero sales in 90 days.</summary>
    [HttpGet("dead-stock")]
    public IActionResult DeadStock(
        [FromQuery] string sourceProfile   = "MolasLubes",
        [FromQuery] string targetProfile   = "AutoHub",
        [FromQuery] string targetWarehouse = "")
    {
        if (string.IsNullOrWhiteSpace(targetWarehouse))
            return BadRequest("targetWarehouse is required.");

        try
        {
            var rows = _analyzer.Analyze(sourceProfile, targetProfile, targetWarehouse)
                .Where(r => r.TrendCategory == LiquiMolyTrendCategory.DeadStock)
                .ToList();
            return Ok(new { count = rows.Count, rows });
        }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    /// <summary>Recent executed replenishment requests with GI/GR references.</summary>
    [HttpGet("executions")]
    public async Task<IActionResult> Executions(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
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

    /// <summary>Recent approval/rejection history.</summary>
    [HttpGet("approvals")]
    public async Task<IActionResult> Approvals(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        // Return both APPROVED and REJECTED records
        var (approvedItems, _) = await _service.ListAsync("APPROVED", skip, take / 2, ct);
        var (rejectedItems, _) = await _service.ListAsync("REJECTED", skip, take / 2, ct);

        var rows = approvedItems.Concat(rejectedItems)
            .OrderByDescending(r => r.ApprovedAt ?? r.RejectedAt)
            .Select(r => new
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

        return Ok(new { rows });
    }
}
