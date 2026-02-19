using Microsoft.AspNetCore.Mvc;
using MolasLubes.Infrastructure.Services.Sync;
using Quartz;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/admin/sync")]
public class AdminSyncController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly NeonInvoiceSyncService _invoiceSyncService;

    public AdminSyncController(
        ISchedulerFactory schedulerFactory,
        NeonInvoiceSyncService invoiceSyncService)
    {
        _schedulerFactory = schedulerFactory;
        _invoiceSyncService = invoiceSyncService;
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
}