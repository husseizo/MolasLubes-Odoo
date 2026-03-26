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

    public AdminItemsController(InventoryCountingUomBackfillService backfill)
    {
        _backfill = backfill;
    }

    /// <summary>
    /// Pass A — classify items and return the preflight report without making any changes.
    /// </summary>
    /// <remarks>
    /// Body example:
    /// <code>
    /// { "itemCodes": ["LR100001","LR100002"], "targetUomCode": "EA" }
    /// </code>
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

    /// <summary>
    /// Pass B — preflight then update all OK_TO_UPDATE items.
    /// Items with an invalid UoM group are never touched.
    /// </summary>
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
}

public class UomBackfillRequest
{
    public List<string> ItemCodes { get; set; } = new();
    public string TargetUomCode   { get; set; } = string.Empty;
}
