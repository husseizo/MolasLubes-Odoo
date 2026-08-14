using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/admin/sync")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminSyncController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly NeonInvoiceSyncService _invoiceSyncService;
    private readonly TantivyPartsSyncService _tantivySyncService;

    public AdminSyncController(
        ISchedulerFactory schedulerFactory,
        NeonInvoiceSyncService invoiceSyncService,
        TantivyPartsSyncService tantivySyncService)
    {
        _schedulerFactory   = schedulerFactory;
        _invoiceSyncService = invoiceSyncService;
        _tantivySyncService = tantivySyncService;
    }

    // -------------------------------------------------
    // PRODUCT FULL
    // -------------------------------------------------
    [HttpPost("products")]
    public async Task<IActionResult> RunProductFullSync()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("ProductFullSyncJob"));

        return Ok(new
        {
            Message = "Product Full Sync triggered successfully"
        });
    }

    // -------------------------------------------------
    // CUSTOMER DELTA
    // -------------------------------------------------
    [HttpPost("customers")]
    public async Task<IActionResult> RunCustomerDeltaSync()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("CustomerDeltaSyncJob"));

        return Ok(new
        {
            Message = "Customer Delta Sync triggered successfully"
        });
    }

    // -------------------------------------------------
    // 🔥 CUSTOMER FULL (NEW)
    // -------------------------------------------------
    [HttpPost("customers/full")]
    public async Task<IActionResult> RunCustomerFullSync()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("CustomerFullSyncJob"));

        return Ok(new
        {
            Message = "Customer FULL Sync triggered successfully"
        });
    }

    // -------------------------------------------------
    // 🔥 INVOICE FULL
    // -------------------------------------------------
    [HttpPost("invoices/full")]
    public async Task<IActionResult> RunInvoiceFullSync()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("InvoiceFullSyncJob"));

        return Ok(new
        {
            Message = "Invoice FULL Sync triggered successfully"
        });
    }

    // -------------------------------------------------
    // 🩹 INVOICE ORPHAN LINE BACKFILL
    // Explicitly backfills invoice lines that exist in
    // the cache but were never propagated to Neon.
    // Runs immediately (not via Quartz).
    // -------------------------------------------------
    [HttpPost("invoices/backfill-lines")]
    public async Task<IActionResult> BackfillOrphanedInvoiceLines()
    {
        await _invoiceSyncService.SyncOrphanedLinesAsync();

        return Ok(new
        {
            Message = "Orphaned invoice line backfill completed"
        });
    }

    // -------------------------------------------------
    // SALES ORDER LINE DESCRIPTIONS
    // Writes "U_ItemName/U_Manufacturer/OriginalDescription"
    // into RDR1.Dscription for every open Sales Order line
    // that is missing the prefix.
    // -------------------------------------------------

    /// <summary>
    /// Batch-formats Dscription on all currently Open Sales Orders.
    /// SQL is used read-only (to get DocEntry list); all writes go through DI API.
    /// </summary>
    [HttpPost("sap/sales-orders/format-descriptions")]
    public IActionResult FormatAllOpenSalesOrderDescriptions(
        [FromServices] SapSalesOrderLineDescriptionUpdater updater)
    {
        updater.UpdateAllOpenSalesOrderDescriptions();
        return Ok(new { message = "Sales Order line description formatting completed — check logs for details" });
    }

    /// <summary>
    /// Formats Dscription on a single Sales Order by DocEntry.
    /// </summary>
    [HttpPost("sap/sales-orders/{docEntry:int}/format-descriptions")]
    public IActionResult FormatSingleSalesOrderDescriptions(
        int docEntry,
        [FromServices] SapSalesOrderLineDescriptionUpdater updater)
    {
        bool ok = updater.UpdateSalesOrderLineDescriptions(docEntry);
        return ok
            ? Ok(new { docEntry, message = "Description update succeeded" })
            : StatusCode(500, new { docEntry, message = "Description update failed — check logs" });
    }

    // -------------------------------------------------
    // TANTIVY PARTS — VIKA / BORSEHUNG / DPA
    // Full upsert from AutoHub SAP into Tantivy_parts
    // -------------------------------------------------
    [HttpPost("autohub/tantivy")]
    public async Task<IActionResult> SyncTantivyParts(CancellationToken ct)
    {
        var (upserted, removed) = await _tantivySyncService.SyncAsync(ct);

        return Ok(new { upserted, removed });
    }

    // -------------------------------------------------
    // TANTIVY CATALOG SCRAPE — VIKA / BORSEHUNG
    // Triggers TantivyScraperJob (Playwright) manually
    // -------------------------------------------------
    [HttpPost("autohub/tantivy/scrape-catalog")]
    public async Task<IActionResult> TriggerTantivyScrape()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("TantivyScraperJob"));

        return Ok(new { message = "TantivyScraperJob triggered — VIKA + Borsehung scraping started" });
    }

    // -------------------------------------------------
    // TANTIVY SCRAPE STATS
    // -------------------------------------------------
    [HttpGet("autohub/tantivy/scrape-stats")]
    public async Task<IActionResult> GetTantivyScrapeStats(
        [FromServices] MolasLubes.Infrastructure.Services.Sync.TantivyScrapeResultSyncService syncSvc,
        CancellationToken ct)
    {
        var (scraped, noMatch, error, pending) = await syncSvc.GetStatsAsync(ct);

        return Ok(new { scraped, noMatch, error, pending, total = scraped + noMatch + error + pending });
    }

    // -------------------------------------------------
    // TANTIVY RESET ERRORS (re-queue for retry)
    // -------------------------------------------------
    [HttpPost("autohub/tantivy/reset-errors")]
    public async Task<IActionResult> ResetTantivyScrapeErrors(
        [FromServices] MolasLubes.Infrastructure.Services.Sync.TantivyScrapeResultSyncService syncSvc,
        CancellationToken ct)
    {
        var count = await syncSvc.ResetErrorsAsync(ct);

        return Ok(new { reset = count });
    }

    // -------------------------------------------------
    // TANTIVY SYNC ARTICLE NUMBERS (TAN Numbers)
    // Copies U_Article_No from Tantivy_parts (SAP cache)
    // into neon_tantivy_scraped.article_no.
    // Creates PENDING seed rows for items not yet seeded.
    // Updates existing rows where article_no is NULL,
    // empty, or different from the SAP TAN Number.
    // -------------------------------------------------
    [HttpPost("autohub/tantivy/sync-article-numbers")]
    public async Task<IActionResult> SyncTantivyArticleNumbers(
        [FromServices] MolasLubes.Infrastructure.Services.Sync.TantivyScrapeResultSyncService syncSvc,
        CancellationToken ct)
    {
        var (seeded, updated) = await syncSvc.SyncArticleNumbersFromPartsAsync(ct);

        return Ok(new
        {
            seeded,
            updated,
            total   = seeded + updated,
            message = $"Seeded {seeded} new PENDING rows, updated {updated} existing article_no values"
        });
    }
}