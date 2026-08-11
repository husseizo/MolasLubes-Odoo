using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Backfill;
using Quartz;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/admin/items")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminItemsController : ControllerBase
{
    private readonly InventoryCountingUomBackfillService _backfill;
    private readonly BulkInventoryCountingUomBackfillService _bulkBackfill;
    private readonly SapProductReader _sapProductReader;
    private readonly SapItemBarcodeWriteService _barcodeWriter;
    private readonly ISchedulerFactory _schedulerFactory;

    public AdminItemsController(
        InventoryCountingUomBackfillService backfill,
        BulkInventoryCountingUomBackfillService bulkBackfill,
        SapProductReader sapProductReader,
        SapItemBarcodeWriteService barcodeWriter,
        ISchedulerFactory schedulerFactory)
    {
        _backfill         = backfill;
        _bulkBackfill     = bulkBackfill;
        _sapProductReader = sapProductReader;
        _barcodeWriter    = barcodeWriter;
        _schedulerFactory = schedulerFactory;
    }

    // -------------------------------------------------
    // MISSING BARCODE REPORT
    // GET /api/admin/items/barcodes/missing
    // Returns all active items in MANWHSE missing a packaging
    // barcode, and all in COCWHSE missing a unit barcode.
    // -------------------------------------------------
    [HttpGet("barcodes/missing")]
    public IActionResult GetMissingBarcodes()
    {
        var rows = _sapProductReader.ReadMissingBarcodeReport();
        return Ok(new
        {
            total        = rows.Count,
            manwhseCount = rows.Count(r => r.WhsCode == "MANWHSE"),
            cocwhseCount = rows.Count(r => r.WhsCode == "COCWHSE"),
            items        = rows
        });
    }

    // -------------------------------------------------
    // BARCODE DIAGNOSTIC — UoM group + barcodes for one item
    // GET /api/admin/items/{itemCode}/barcodes
    // -------------------------------------------------
    [HttpGet("{itemCode}/barcodes")]
    public IActionResult GetItemBarcodes(string itemCode)
    {
        var info = _sapProductReader.ReadItemBarcodeInfo(itemCode);
        if (info == null)
            return NotFound(new { Error = $"Item '{itemCode}' not found in SAP." });

        return Ok(info);
    }

    // -------------------------------------------------
    // WRITE BARCODE — register a scanned barcode in SAP OBCD
    // POST /api/admin/items/{itemCode}/barcodes
    // Body: { "barcodeValue": "4100420023163", "uomCode": "Unit" }
    //
    // Mobile flow: show missing-barcodes list → user scans the
    // physical product → POST here → A Plus scanner can now match
    // the scan to the item during inventory counting.
    // -------------------------------------------------
    [HttpPost("{itemCode}/barcodes")]
    public IActionResult WriteBarcode(string itemCode, [FromBody] WriteBarcodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BarcodeValue))
            return BadRequest(new { Error = "barcodeValue is required" });
        if (string.IsNullOrWhiteSpace(request.UomCode))
            return BadRequest(new { Error = "uomCode is required (e.g. 'Unit' for COCWHSE items, or the packaging UoM code for MANWHSE items)" });

        var result = _barcodeWriter.WriteBarcode(itemCode, request.BarcodeValue, request.UomCode);

        if (!result.Success)
            return UnprocessableEntity(new { result.Error });

        return Ok(new
        {
            itemCode,
            request.BarcodeValue,
            request.UomCode,
            wasAlreadyPresent = result.WasAlreadyPresent,
            message = result.WasAlreadyPresent
                ? "Barcode already registered for this UoM — no change made"
                : "Barcode written to SAP successfully"
        });
    }

    // -------------------------------------------------
    // BULK MANUAL BARCODE WRITE
    // POST /api/admin/items/barcodes/fill-bulk
    // Body: [{ "itemCode":"1015","barcodeValue":"410042...","uomCode":"Unit" }, ...]
    //
    // Writes barcodes for multiple items in one call.
    // Each entry is independent — one failure doesn't abort the rest.
    // Returns per-item results + totals.
    // -------------------------------------------------
    [HttpPost("barcodes/fill-bulk")]
    public IActionResult FillBulk([FromBody] List<WriteBarcodeRequest> items)
    {
        if (items == null || items.Count == 0)
            return BadRequest(new { Error = "Request body must contain at least one item." });

        var results  = new List<object>();
        int written  = 0;
        int already  = 0;
        int failed   = 0;

        foreach (var req in items)
        {
            if (string.IsNullOrWhiteSpace(req.ItemCode) ||
                string.IsNullOrWhiteSpace(req.BarcodeValue) ||
                string.IsNullOrWhiteSpace(req.UomCode))
            {
                results.Add(new
                {
                    req.ItemCode,
                    req.BarcodeValue,
                    req.UomCode,
                    status  = "FAILED",
                    message = "itemCode, barcodeValue and uomCode are all required."
                });
                failed++;
                continue;
            }

            try
            {
                var r = _barcodeWriter.WriteBarcode(req.ItemCode, req.BarcodeValue, req.UomCode);

                if (!r.Success)
                {
                    results.Add(new { req.ItemCode, req.BarcodeValue, req.UomCode, status = "FAILED",   message = r.Error });
                    failed++;
                }
                else if (r.WasAlreadyPresent)
                {
                    results.Add(new { req.ItemCode, req.BarcodeValue, req.UomCode, status = "ALREADY_EXISTS", message = "Barcode already registered — no change." });
                    already++;
                }
                else
                {
                    results.Add(new { req.ItemCode, req.BarcodeValue, req.UomCode, status = "WRITTEN",  message = "Barcode written successfully." });
                    written++;
                }
            }
            catch (Exception ex)
            {
                results.Add(new { req.ItemCode, req.BarcodeValue, req.UomCode, status = "ERROR", message = ex.Message });
                failed++;
            }
        }

        return Ok(new
        {
            total   = items.Count,
            written,
            already,
            failed,
            results
        });
    }

    // -------------------------------------------------
    // CSV IMPORT — write barcodes by UomEntry integer
    // POST /api/admin/items/barcodes/import
    //
    // Accepts rows from UNIT_ONLY_Barcodes_CORRECT.csv and
    // CASE_ONLY_Barcodes_CORRECT.csv combined.
    // UomEntry comes straight from the CSV — no OUOM code lookup.
    // -------------------------------------------------
    [HttpPost("barcodes/import")]
    public IActionResult Import([FromBody] List<BarcodeImportRow> rows)
    {
        if (rows == null || rows.Count == 0)
            return BadRequest(new { Error = "Request body must contain at least one row." });

        int written = 0;
        int already = 0;
        int failed  = 0;
        var results = new List<object>();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ItemCode) ||
                string.IsNullOrWhiteSpace(row.Barcode)  ||
                row.UomEntry <= 0)
            {
                failed++;
                results.Add(new { row.ItemCode, row.UomEntry, row.Barcode,
                    status = "FAILED", error = "ItemCode, UomEntry and Barcode are required." });
                continue;
            }

            try
            {
                var r = _barcodeWriter.WriteBarcode(row.ItemCode, row.Barcode, row.UomEntry);

                if (!r.Success)
                {
                    failed++;
                    results.Add(new { row.ItemCode, row.UomEntry, row.Barcode,
                        status = "FAILED", error = r.Error });
                }
                else if (r.WasAlreadyPresent)
                {
                    already++;
                    results.Add(new { row.ItemCode, row.UomEntry, row.Barcode,
                        status = "ALREADY_EXISTS" });
                }
                else
                {
                    written++;
                    results.Add(new { row.ItemCode, row.UomEntry, row.Barcode,
                        status = "WRITTEN" });
                }
            }
            catch (Exception ex)
            {
                failed++;
                results.Add(new { row.ItemCode, row.UomEntry, row.Barcode,
                    status = "ERROR", error = ex.Message });
            }
        }

        return Ok(new { total = rows.Count, written, already, failed, results });
    }

    // -------------------------------------------------
    // BACKFILL FROM OITM.CodeBars → OBCD
    // POST /api/admin/items/barcodes/fill-from-codebars
    //
    // Writes the legacy OITM.CodeBars value into OBCD (Unit UoM)
    // for every COCWHSE item that already has a CodeBars but is
    // missing the OBCD entry.  No physical scan required.
    //
    // Run this once to immediately fix all items where SAP already
    // holds the barcode in the old single-barcode field so that
    // A Plus Barcode Scanner can match them during inventory counting.
    // -------------------------------------------------
    [HttpPost("barcodes/fill-from-codebars")]
    public IActionResult FillFromCodeBars()
    {
        var items = _sapProductReader.ReadCocwhseItemsWithLegacyCodeBars();

        if (items.Count == 0)
            return Ok(new
            {
                total   = 0,
                written = 0,
                failed  = 0,
                message = "No COCWHSE items have a CodeBars value that is missing from OBCD Unit barcode."
            });

        int written = 0;
        int already = 0;
        int failed  = 0;
        var results = new List<object>();

        foreach (var item in items)
        {
            try
            {
                var r = _barcodeWriter.WriteBarcode(item.ItemCode, item.CodeBars, "Unit");

                if (!r.Success)
                {
                    failed++;
                    results.Add(new { item.ItemCode, item.ItemName, item.CodeBars, status = "FAILED",       error = r.Error });
                }
                else if (r.WasAlreadyPresent)
                {
                    already++;
                    results.Add(new { item.ItemCode, item.ItemName, item.CodeBars, status = "ALREADY_EXISTS" });
                }
                else
                {
                    written++;
                    results.Add(new { item.ItemCode, item.ItemName, item.CodeBars, status = "WRITTEN" });
                }
            }
            catch (Exception ex)
            {
                failed++;
                results.Add(new { item.ItemCode, item.ItemName, item.CodeBars, status = "ERROR", error = ex.Message });
            }
        }

        return Ok(new { total = items.Count, written, already, failed, results });
    }

    // -------------------------------------------------
    // NO-BARCODE-ANYWHERE REPORT
    // GET /api/admin/items/barcodes/none
    // Returns all active inventory items with neither
    // OITM.CodeBars nor any OBCD entry at all.
    // -------------------------------------------------
    [HttpGet("barcodes/none")]
    public IActionResult GetNoBarcodeItems()
    {
        var rows = _sapProductReader.ReadItemsWithNoBarcodeAnywhere();
        return Ok(new
        {
            total = rows.Count,
            items = rows
        });
    }

    // -------------------------------------------------
    // MANUAL TRIGGER — run SapBarcodeFillJob now
    // POST /api/admin/items/barcodes/fill-auto
    // Kicks off the same Quartz job that runs nightly at 03:30 UTC.
    // -------------------------------------------------
    [HttpPost("barcodes/fill-auto")]
    public async Task<IActionResult> TriggerBarcodeFillJob()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("SapBarcodeFillJob"));
        return Accepted(new { Message = "SapBarcodeFillJob triggered — check logs for progress." });
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
    // GET UOM ISSUES — select by filter, return only problem items
    // -------------------------------------------------
    /// <remarks>
    /// Example:
    /// GET /api/admin/items/uom/issues?targetUomCode=EA&itemGroupNames=Liqui%20Moly&take=100&skip=0
    /// </remarks>
    [HttpGet("uom/issues")]
    public IActionResult GetUomIssues([FromQuery] BulkUomBackfillRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetUomCode))
            return BadRequest(new { Error = "targetUomCode is required" });

        var report = _bulkBackfill.RunIssues(request);

        if (report.Error != null)
            return UnprocessableEntity(new { report.Error });

        return Ok(new
        {
            report.TargetUomCode,
            report.TargetUomEntry,
            scannedItems = report.Selection?.MatchedItems ?? 0,
            issueCount = report.Rows.Count,
            report.Totals,
            report.Summary,
            report.Selection,
            rows = report.Rows
        });
    }

    // -------------------------------------------------
    // GET UOM READY — select by filter, return only OK_TO_UPDATE items
    // -------------------------------------------------
    /// <remarks>
    /// Example:
    /// GET /api/admin/items/uom/ready?targetUomCode=EA&itemGroupNames=Liqui%20Moly&take=100&skip=0
    /// </remarks>
    [HttpGet("uom/ready")]
    public IActionResult GetUomReadyToUpdate([FromQuery] BulkUomBackfillRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetUomCode))
            return BadRequest(new { Error = "targetUomCode is required" });

        var report = _bulkBackfill.RunReadyToUpdate(request);

        if (report.Error != null)
            return UnprocessableEntity(new { report.Error });

        return Ok(new
        {
            report.TargetUomCode,
            report.TargetUomEntry,
            scannedItems = report.Selection?.MatchedItems ?? 0,
            readyCount = report.Rows.Count,
            report.Totals,
            report.Summary,
            report.Selection,
            rows = report.Rows
        });
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

public class WriteBarcodeRequest
{
    public string? ItemCode     { get; set; }              // used by bulk endpoint; ignored by single-item route
    public string  BarcodeValue { get; set; } = string.Empty;
    public string  UomCode      { get; set; } = string.Empty;
}

public class BarcodeImportRow
{
    public string ItemCode { get; set; } = string.Empty;
    public int    UomEntry { get; set; }   // integer from CSV — matches OBCD.UomEntry directly
    public string Barcode  { get; set; } = string.Empty;
}
