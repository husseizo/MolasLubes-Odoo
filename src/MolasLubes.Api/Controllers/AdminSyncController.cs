using Microsoft.AspNetCore.Mvc;
using Quartz;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/admin/sync")]
public class AdminSyncController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;

    public AdminSyncController(ISchedulerFactory schedulerFactory)
    {
        _schedulerFactory = schedulerFactory;
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
}