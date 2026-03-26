using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Infrastructure.Services.Backfill;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/admin/items")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminItemsController : ControllerBase
{
    private readonly InventoryCountingUomBackfillService _backfill;
    private readonly BulkInventoryCountingUomBackfillService _bulkBackfill;

    public AdminItemsController(
        InventoryCountingUomBackfillService backfill,
        BulkInventoryCountingUomBackfillService bulkBackfill)
    {
        _backfill     = backfill;
        _bulkBackfill = bulkBackfill;
    }

    // -------------------------------------------------
    // EXPLICIT DRY-RUN — classify a supplied list, no writes
    // -------------------------------------------------
    /// <remarks>
    /// Body: { "itemCodes": ["LR100001","LR100002"], "targetUomCode": "EA" }
    /// </remarks>
    [HttpPost("uom/dry-run")]
    public IActionResult DryRun([FromBody] UomBackfillRequest request)
    {
        if (request.ItemCodes == null || request.ItemCodes.Count == 0)
            return BadRequest(new { Error = "itemCodes must not be empty" });

        if (string.IsNullOrWhiteSpace(request.TargetUomCode))
            return BadRequest(new { Error = "targetUomCode is required" });

        var report = _backfill.Run(request.ItemCodes, request.TargetUomCode, dryRun: true);

        if (report.Error != null)
            return UnprocessableEntity(new { report.Error });

        return Ok(report);
    }

    // -------------------------------------------------
    // EXPLICIT APPLY — preflight then update supplied list
    // -------------------------------------------------
    [HttpPost("uom/apply")]
    public IActionResult Apply([FromBody] UomBackfillRequest request)
    {
        if (request.ItemCodes == null || request.ItemCodes.Count == 0)
            return BadRequest(new { Error = "itemCodes must not be empty" });

        if (string.IsNullOrWhiteSpace(request.TargetUomCode))
            return BadRequest(new { Error = "targetUomCode is required" });

        var report = _backfill.Run(request.ItemCodes, request.TargetUomCode, dryRun: false);

        if (report.Error != null)
            return UnprocessableEntity(new { report.Error });

        return Ok(report);
    }

    // -------------------------------------------------
    // BULK DRY-RUN — select by filter, classify, no writes
    // -------------------------------------------------
    [HttpPost("uom/bulk/dry-run")]
    public IActionResult BulkDryRun([FromBody] BulkUomBackfillRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetUomCode))
            return BadRequest(new { Error = "targetUomCode is required" });

        var report = _bulkBackfill.Run(request, dryRun: true);

        if (report.Error != null)
            return UnprocessableEntity(new { report.Error });

        return Ok(report);
    }

    // -------------------------------------------------
    // BULK APPLY — select by filter, update OK_TO_UPDATE
    // -------------------------------------------------
    [HttpPost("uom/bulk/apply")]
    public IActionResult BulkApply([FromBody] BulkUomBackfillRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetUomCode))
            return BadRequest(new { Error = "targetUomCode is required" });

        var report = _bulkBackfill.Run(request, dryRun: false);

        if (report.Error != null)
            return UnprocessableEntity(new { report.Error });

        return Ok(report);
    }
}

public class UomBackfillRequest
{
    public List<string> ItemCodes { get; set; } = new();
    public string TargetUomCode   { get; set; } = string.Empty;
}
