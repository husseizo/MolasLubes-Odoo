using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MolasLubes.Application.LiquiMolyReplenishment;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Security;
using MolasLubes.Infrastructure.Services.LiquiMolyReplenishment;

namespace MolasLubes.Api.Controllers.LiquiMoly;

[ApiController]
[Route("api/liquimoly/replenishment")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class LiquiMolyReplenishmentController : ControllerBase
{
    private readonly LiquiMolyReplenishmentService _service;
    private readonly SapWarehouseReader _warehouseReader;

    public LiquiMolyReplenishmentController(
        LiquiMolyReplenishmentService service,
        SapWarehouseReader warehouseReader)
    {
        _service = service;
        _warehouseReader = warehouseReader;
    }

    [HttpGet("warehouse-options")]
    public IActionResult GetWarehouseOptions(
        [FromQuery] string sourceProfile = "MolasLubes",
        [FromQuery] string targetProfile = "AutoHub")
    {
        try
        {
            GetCurrentSapUserCode();
            var result = _warehouseReader.GetWarehouseOptions(sourceProfile, targetProfile);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(401, ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpPost("generate-draft")]
    public async Task<IActionResult> GenerateDraft(
        [FromBody] GenerateReplenishmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var sapUserCode = GetCurrentSapUserCode();
            request.Actor = BuildActor(sapUserCode, request.Actor?.Comment);

            var (requestRef, rows) = await _service.GenerateDraftAsync(request, ct, authorizeActor: false);
            return Ok(new { requestRef, rowCount = rows.Count, rows });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(401, ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        try
        {
            var sapUserCode = GetCurrentSapUserCode();

            var (items, lineCounts, hasMore) = await _service.ListOwnSummariesAsync(sapUserCode, status, skip, take, ct);
            return Ok(new
            {
                items = items.Select(item => ToRequestSummaryResponse(item, lineCounts.GetValueOrDefault(item.Id, 0))),
                hasMore,
                count = items.Count
            });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(401, ex.Message); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(499, "Request was canceled.");
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, "Timed out while loading replenishment requests.");
        }
    }

    [HttpGet("{requestRef}")]
    public async Task<IActionResult> Get(
        string requestRef,
        CancellationToken ct = default)
    {
        try
        {
            var sapUserCode = GetCurrentSapUserCode();

            var header = await _service.GetAsync(requestRef, ct);
            if (header == null)
                return NotFound();

            if (!IsOwner(header, sapUserCode))
                return StatusCode(403, "You are not allowed to access this replenishment request.");

            return Ok(ToRequestResponse(header));
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(401, ex.Message); }
    }

    [HttpPost("{requestRef}/draft-lines/apply")]
    public async Task<IActionResult> ApplyDraftLines(
        string requestRef,
        [FromBody] DraftLineApplyRequest request,
        CancellationToken ct)
    {
        try
        {
            var sapUserCode = GetCurrentSapUserCode();

            var current = await _service.GetAsync(requestRef, ct);
            if (current == null)
                return NotFound();

            if (!IsOwner(current, sapUserCode))
                return StatusCode(403, "You are not allowed to update this replenishment request.");

            request.Actor = BuildActor(sapUserCode, request.Actor?.Comment);

            var result = await _service.ApplyDraftLinesAsync(requestRef, request, ct, authorizeActor: false);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(401, ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return UnprocessableEntity(new { message = "Validation failed.", errors = new { operations = ex.Message } }); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("DRAFT_VERSION_CONFLICT"))
        {
            var current = await _service.GetAsync(requestRef, ct);
            return Conflict(new { message = "Draft version mismatch.", code = "DRAFT_VERSION_CONFLICT", currentVersion = current?.Version });
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("DRAFT_STATUS_CONFLICT"))
        {
            return Conflict(new { message = "Request status mismatch.", code = "DRAFT_STATUS_CONFLICT" });
        }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpPost("{requestRef}/submit")]
    public async Task<IActionResult> Submit(
        string requestRef,
        [FromBody] SubmitReplenishmentRequest request,
        CancellationToken ct)
    {
        try
        {
            var sapUserCode = GetCurrentSapUserCode();

            var current = await _service.GetAsync(requestRef, ct);
            if (current == null)
                return NotFound();

            if (!IsOwner(current, sapUserCode))
                return StatusCode(403, "You are not allowed to submit this replenishment request.");

            request.Actor = BuildActor(sapUserCode, request.Actor?.Comment);

            var header = await _service.SubmitForApprovalAsync(requestRef, request, ct, authorizeActor: false);
            return Ok(new { header.RequestRef, header.Status, header.SubmittedAt });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(401, ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    private string GetCurrentSapUserCode()
    {
        var sapUserCode = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(sapUserCode))
            throw new UnauthorizedAccessException("Authenticated SAP user was not found in the access token.");

        return sapUserCode;
    }

    private static bool IsOwner(CacheLiquiMolyReplenishmentRequest header, string sapUserCode) =>
        string.Equals(header.RequestedBySapUser, sapUserCode, StringComparison.OrdinalIgnoreCase);

    private static SapActorContext BuildActor(string sapUserCode, string? comment) => new()
    {
        SapUserCode = sapUserCode,
        Comment = comment
    };

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
