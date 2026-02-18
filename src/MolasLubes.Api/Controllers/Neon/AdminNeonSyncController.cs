using Microsoft.AspNetCore.Mvc;
using Quartz;
using MolasLubes.Api.Security;

namespace MolasLubes.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/neon-sync")]
[ServiceFilter(typeof(ApiKeyAttribute))] // 🔐 PROTECTED
public class AdminNeonSyncController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;

    public AdminNeonSyncController(ISchedulerFactory schedulerFactory)
    {
        _schedulerFactory = schedulerFactory;
    }

    // POST /api/admin/neon-sync/products
    [HttpPost("products")]
    public async Task<IActionResult> SyncProducts()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("NeonProductDeltaSyncJob"));

        return Ok(new
        {
            Message = "Neon Product Delta Sync triggered"
        });
    }

    // POST /api/admin/neon-sync/prices
    [HttpPost("prices")]
    public async Task<IActionResult> SyncPrices()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("NeonPriceListSyncJob"));

        return Ok(new
        {
            Message = "Neon PriceList Sync triggered"
        });
    }


    // POST /api/admin/neon-sync/pricelist
    [HttpPost("pricelist")]
    public async Task<IActionResult> SyncPriceList()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("NeonPriceListSyncJob"));

        return Ok(new
        {
            Message = "Neon PriceList Sync triggered"
        });
    }

    // POST /api/admin/neon-sync/customers
    // POST /api/admin/neon-sync/customers
    [HttpPost("customers")]
    public async Task<IActionResult> SyncCustomers()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("NeonCustomerSyncJob"));

        return Ok(new
        {
            Message = "Neon Customer Delta Sync triggered"
        });
    }

    // POST /api/admin/neon-sync/sales-orders
    // POST /api/admin/neon-sync/orders
    [HttpPost("orders")]
    public async Task<IActionResult> SyncSalesOrders()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("NeonSalesOrderSyncJob"));

        return Ok(new
        {
            Message = "Neon SalesOrder Delta Sync triggered"
        });
    }

    // POST /api/admin/neon-sync/SalesOrdersLines
    // POST /api/admin/neon-sync/order-lines
    [HttpPost("order-lines")]
    public async Task<IActionResult> SyncSalesOrderLines()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("NeonSalesOrderLineSyncJob"));

        return Ok(new
        {
            Message = "Neon SalesOrderLine Delta Sync triggered"
        });
    }

    // POST /api/admin/neon-sync/invoices
    [HttpPost("invoices")]
    public async Task<IActionResult> SyncInvoices()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("NeonInvoiceSyncJob"));

        return Ok(new { Message = "Neon Invoice Sync triggered" });
    }

    // POST /api/admin/neon-sync/payments
    [HttpPost("payments")]
    public async Task<IActionResult> SyncPayments()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("NeonPaymentSyncJob"));

        return Ok(new { Message = "Neon Payment Sync triggered" });
    }

    // POST /api/admin/neon-sync/deliveries
    [HttpPost("deliveries")]
    public async Task<IActionResult> SyncDeliveries()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey("NeonDeliverySyncJob"));

        return Ok(new { Message = "Neon Delivery Sync triggered" });
    }
}