using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.Orders;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Services.Finance;
using MolasLubes.Infrastructure.Services.Orders;
using MolasLubes.Infrastructure.Services.Caching;
using MolasLubes.Infrastructure.Services.Stock;
using MolasLubes.Api.Security; // 👈 REQUIRED
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/sales-orders")]
[ServiceFilter(typeof(ApiKeyAttribute))] // 🔐 NOW 100% ENFORCED
public class SalesOrdersController : ControllerBase
{
    private readonly SapSalesOrderCreator _sap;
    private readonly SalesOrderCacheService _cache;
    private readonly OrderLifecycleService _lifecycle;
    private readonly InvoiceBalanceService _invoiceBalance;
    private readonly ILogger<SalesOrdersController> _logger;

    public SalesOrdersController(
        SapSalesOrderCreator sap,
        SalesOrderCacheService cache,
        OrderLifecycleService lifecycle,
        InvoiceBalanceService invoiceBalance,
        ILogger<SalesOrdersController> logger)
    {
        _sap = sap;
        _cache = cache;
        _lifecycle = lifecycle;
        _invoiceBalance = invoiceBalance;
        _logger = logger;
    }

    // =====================================================
    // 1️⃣ CREATE SALES ORDER
    // =====================================================
    [HttpPost]
    public async Task<IActionResult> Create(
     [FromBody] CreateSalesOrderDto dto,
     [FromServices] StockReservationService reservationSvc,
     [FromServices] ReservationCommitService commitSvc)
    {
        if (dto == null)
            return BadRequest("Request body is required");

        if (dto.Lines == null || dto.Lines.Count == 0)
            return BadRequest("Order must contain at least one line");

        // ─────────────────────────────────────────────
        // 0️⃣ PRE-VALIDATE STOCK FOR ALL LINES
        // Check every line before touching any reservation.
        // This prevents partial reservations and gives the caller
        // a full list of out-of-stock items in one shot.
        // ─────────────────────────────────────────────
        var checks = await reservationSvc.CheckAvailabilityAsync(
            dto.Lines.Select(l => (l.ItemCode, l.WarehouseCode, l.Quantity)));

        var unavailable = checks.Where(c => !c.IsAvailable).ToList();

        if (unavailable.Any())
        {
            _logger.LogWarning(
                "🚫 Order blocked | Customer={Customer} | OutOfStockItems={Items}",
                dto.CustomerCode,
                string.Join(", ", unavailable.Select(u => u.ItemCode)));

            return UnprocessableEntity(new
            {
                Error = "Order cannot be created: one or more items are out of stock.",
                OutOfStockItems = unavailable.Select(u => new
                {
                    u.ItemCode,
                    u.RequestedWarehouse,
                    u.RequestedQty,
                    u.TotalAvailable
                })
            });
        }

        // ─────────────────────────────────────────────
        // 1️⃣ RESERVE STOCK
        // All items passed the pre-check. Reserve them now.
        // If an unexpected failure (race/concurrency) occurs mid-loop,
        // release every reservation already made so no stock is leaked.
        // ─────────────────────────────────────────────
        var reservationIds = new List<long>();

        try
        {
            foreach (var line in dto.Lines)
            {
                var res = await reservationSvc.ReserveAsync(
                    line.ItemCode,
                    line.WarehouseCode,
                    line.Quantity,
                    dto.ExternalOrderId ?? "DirectOrder");

                reservationIds.Add(res.Id);
            }
        }
        catch
        {
            // Release any reservations made before the failure so stock
            // is not silently locked.
            foreach (var id in reservationIds)
            {
                try { await reservationSvc.ReleaseAsync(id); }
                catch (Exception releaseEx)
                {
                    _logger.LogError(releaseEx,
                        "⚠ Cleanup failed for ReservationId={Id} after partial order failure", id);
                }
            }

            throw;
        }

        // ─────────────────────────────────────────────
        // 2️⃣ COMMIT RESERVATIONS → SAP
        // ─────────────────────────────────────────────
        var result = await commitSvc.CommitAsync(
            dto.CustomerCode,
            reservationIds,
            dto.ExternalOrderId);

        return Ok(new
        {
            result.DocEntry,
            result.DocNum
        });
    }



    [HttpPost("commit")]
    public async Task<IActionResult> CommitFromReservations(
    [FromBody] CommitSalesOrderRequest req,
    [FromServices] ReservationCommitService commitSvc)
    {
        if (req == null || req.ReservationIds.Count == 0)
            return BadRequest("Reservations are required");

        var result = await commitSvc.CommitAsync(
            req.CardCode,
            req.ReservationIds,
            req.ExternalOrderId);

        return Ok(new
        {
            result.DocEntry,
            result.DocNum
        });
    }

    // =====================================================
    // 2️⃣ ORDER LIFECYCLE (Open / Delivered / Invoiced)
    // =====================================================
    [HttpGet("{sapDocEntry}/lifecycle")]
    public async Task<IActionResult> GetLifecycle(int sapDocEntry)
    {
        var result = await _lifecycle.GetLifecycleAsync(sapDocEntry);
        if (result == null)
            return NotFound();

        return Ok(result);
    }

    // =====================================================
    // 3️⃣ INVOICE BALANCE + PAID STATUS (VIA ORDER)
    // =====================================================
    [HttpGet("{sapDocEntry}/invoice-balance")]
    public async Task<IActionResult> GetInvoiceBalance(int sapDocEntry)
    {
        var result = await _invoiceBalance.GetInvoiceBalanceAsync(sapDocEntry);
        if (result == null)
            return NotFound();

        return Ok(result);
    }


    [HttpPost("{sapDocEntry}/cancel")]
    public async Task<IActionResult> Cancel(
    int sapDocEntry,
    [FromBody] CancelSalesOrderRequest req,
    [FromServices] CancelSalesOrderService svc)
    {
        await svc.CancelAsync(sapDocEntry, req.Reason);
        return Ok(new
        {
            SapDocEntry = sapDocEntry,
            Status = "Cancelled"
        });
    }
}