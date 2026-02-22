using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Api.Security;
using MolasLubes.Application.Invoices;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Integrations.SapB1.Errors;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Services.Finance;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/v1/invoices")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class InvoicesCommandController : ControllerBase
{
    private readonly SapInvoiceWriter _writer;
    private readonly SapCreditMemoWriter _creditMemoWriter;
    private readonly NeonDbContext _neonDb;
    private readonly InvoiceBalanceService _balanceService;

    public InvoicesCommandController(
        SapInvoiceWriter writer,
        SapCreditMemoWriter creditMemoWriter,
        NeonDbContext neonDb,
        InvoiceBalanceService balanceService)
    {
        _writer = writer;
        _creditMemoWriter = creditMemoWriter;
        _neonDb = neonDb;
        _balanceService = balanceService;
    }

    // =====================================================
    // POST /api/v1/invoices/create
    // =====================================================
    [HttpPost("create")]
    public IActionResult Create([FromBody] CreateInvoiceDto dto)
    {
        try
        {
            var r = _writer.CreateInvoice(dto);
            return Ok(new { r.DocEntry, r.DocNum, r.AlreadyExists });
        }
        catch (SapIntegrationException ex)
        {
            return BadRequest(new
            {
                error_code = ex.ErrorCode,
                sap_message = ex.SapMessage,
                user_message = ex.UserMessage,
                retryable = ex.Retryable
            });
        }
    }

    // =====================================================
    // GET /api/v1/invoices/{sapDocEntry}
    // Returns invoice header + lines + balance
    // =====================================================
    [HttpGet("{sapDocEntry:int}")]
    public async Task<IActionResult> GetInvoice(int sapDocEntry)
    {
        var invoice = await _neonDb.Invoices
            .AsNoTracking()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.SapDocEntry == sapDocEntry);

        if (invoice == null)
            return NotFound(new { message = $"Invoice {sapDocEntry} not found" });

        var balance = await _balanceService.GetInvoiceBalanceAsync(sapDocEntry);

        return Ok(new
        {
            invoice.SapDocEntry,
            invoice.DocNum,
            invoice.CustomerCode,
            invoice.CardName,
            invoiceDate   = invoice.InvoiceDate,
            invoice.DocTotal,
            invoice.VatSum,
            invoice.PaidAmount,
            balance       = balance?.Balance ?? invoice.DocTotal,
            status        = balance?.Status ?? "Unpaid",
            invoice.IsPaid,
            invoice.OdooInvoiceId,
            invoice.OdooStatus,
            lines = invoice.Lines.Select(l => new
            {
                l.ItemCode,
                l.Description,
                l.Quantity,
                l.LineTotal,
                l.GrossBuyPr,
                l.BaseEntry,
                l.BaseLine,
                l.OdooInvoiceLineId,
                l.OdooStatus
            })
        });
    }

    // =====================================================
    // GET /api/v1/invoices?cardCode=C0001&page=1&pageSize=50
    // Returns paginated invoice headers for a customer
    // =====================================================
    [HttpGet]
    public async Task<IActionResult> ListInvoices(
        [FromQuery] string? cardCode,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (pageSize > 200) pageSize = 200;
        if (page < 1) page = 1;

        var query = _neonDb.Invoices.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(cardCode))
            query = query.Where(i => i.CustomerCode == cardCode);

        var total = await query.CountAsync();

        var invoices = await query
            .OrderByDescending(i => i.InvoiceDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new
            {
                i.SapDocEntry,
                i.DocNum,
                i.CustomerCode,
                i.CardName,
                invoiceDate = i.InvoiceDate,
                i.DocTotal,
                i.VatSum,
                i.PaidAmount,
                balance = i.DocTotal - i.PaidAmount,
                i.IsPaid,
                i.OdooInvoiceId,
                i.OdooStatus
            })
            .ToListAsync();

        return Ok(new
        {
            total,
            page,
            pageSize,
            items = invoices
        });
    }

    // =====================================================
    // GET /api/v1/invoices/{sapDocEntry}/balance
    // Returns balance summary only (lightweight)
    // =====================================================
    [HttpGet("{sapDocEntry:int}/balance")]
    public async Task<IActionResult> GetBalance(int sapDocEntry)
    {
        var result = await _balanceService.GetInvoiceBalanceAsync(sapDocEntry);

        if (result == null)
            return NotFound(new { message = $"Invoice {sapDocEntry} not found" });

        return Ok(result);
    }

    // =====================================================
    // POST /api/v1/invoices/{sapDocEntry}/credit-memo
    // Creates a credit note (ORIN) based on the invoice.
    // =====================================================
    [HttpPost("{sapDocEntry:int}/credit-memo")]
    public IActionResult CreateCreditMemo(int sapDocEntry, [FromBody] CreateCreditMemoDto dto)
    {
        dto.InvoiceDocEntry = sapDocEntry;

        try
        {
            var r = _creditMemoWriter.CreateCreditMemo(dto);
            return Ok(new { r.DocEntry, r.DocNum });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error_code = "VALIDATION_ERROR", user_message = ex.Message });
        }
        catch (SapIntegrationException ex)
        {
            return BadRequest(new
            {
                error_code = ex.ErrorCode,
                sap_message = ex.SapMessage,
                user_message = ex.UserMessage,
                retryable = ex.Retryable
            });
        }
    }

    // =====================================================
    // POST /api/v1/invoices/{sapDocEntry}/cancel
    // Cancels an open invoice in SAP.
    // =====================================================
    [HttpPost("{sapDocEntry:int}/cancel")]
    public IActionResult CancelInvoice(int sapDocEntry)
    {
        try
        {
            _writer.CancelInvoice(sapDocEntry);
            return Ok(new { Message = $"Invoice {sapDocEntry} cancelled successfully" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error_code = "VALIDATION_ERROR", user_message = ex.Message });
        }
        catch (SapIntegrationException ex)
        {
            return BadRequest(new
            {
                error_code = ex.ErrorCode,
                sap_message = ex.SapMessage,
                user_message = ex.UserMessage,
                retryable = ex.Retryable
            });
        }
    }
}