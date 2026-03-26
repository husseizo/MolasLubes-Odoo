using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Application.LiquiMolyTransfers;
using MolasLubes.Infrastructure.Services.LiquiMolyTransfers;

namespace MolasLubes.Api.Controllers.LiquiMoly;

[ApiController]
[Route("api/admin/liquimoly/transfers")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminLiquiMolyTransfersController : ControllerBase
{
    private readonly LiquiMolyTransferService _service;

    public AdminLiquiMolyTransfersController(LiquiMolyTransferService service)
    {
        _service = service;
    }

    // -------------------------------------------------
    // DRY-RUN — preflight only, no SAP writes
    // -------------------------------------------------
    [HttpPost("dry-run")]
    public IActionResult DryRun([FromBody] CreateLiquiMolyTransferRequest request)
    {
        if (request.Lines == null || request.Lines.Count == 0)
            return BadRequest(new { Error = "At least one line is required." });

        var result = _service.DryRun(request);

        if (result.Error != null)
            return UnprocessableEntity(new { result.Error });

        return Ok(result);
    }

    // -------------------------------------------------
    // APPLY — create GI in source + GR in target
    // -------------------------------------------------
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(
        [FromBody] CreateLiquiMolyTransferRequest request,
        CancellationToken ct)
    {
        if (request.Lines == null || request.Lines.Count == 0)
            return BadRequest(new { Error = "At least one line is required." });

        var result = await _service.ApplyAsync(request, ct);

        if (result.Status == "FAILED")
            return UnprocessableEntity(result);

        return Ok(result);
    }

    // -------------------------------------------------
    // RETRY RECEIPT — re-attempt GR on RECEIPT_PENDING
    // -------------------------------------------------
    [HttpPost("{transferRef}/retry-receipt")]
    public async Task<IActionResult> RetryReceipt(string transferRef, CancellationToken ct)
    {
        var result = await _service.RetryReceiptAsync(transferRef, ct);

        if (result.Status == "FAILED")
            return UnprocessableEntity(result);

        return Ok(result);
    }

    // -------------------------------------------------
    // GET — read audit record for a specific transfer
    // -------------------------------------------------
    [HttpGet("{transferRef}")]
    public async Task<IActionResult> Get(string transferRef, CancellationToken ct)
    {
        var transfer = await _service.GetTransferAsync(transferRef, ct);

        if (transfer == null)
            return NotFound(new { Error = $"Transfer '{transferRef}' not found." });

        return Ok(transfer);
    }
}
