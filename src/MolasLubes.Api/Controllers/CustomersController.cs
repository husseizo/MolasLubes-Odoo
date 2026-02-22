using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MolasLubes.Api.Security;
using MolasLubes.Application.Customers;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Integrations.SapB1.Errors;
using MolasLubes.Infrastructure.Services.Caching;
using MolasLubes.Infrastructure.Services.Finance;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/customers")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class CustomersController : ControllerBase
{
    private readonly SapCustomerWriter _sapWriter;
    private readonly SapCustomerReader _sapReader;
    private readonly CustomerCacheService _cache;
    private readonly CustomerCreditService _credit;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(
        SapCustomerWriter sapWriter,
        SapCustomerReader sapReader,
        CustomerCacheService cache,
        CustomerCreditService credit,
        ILogger<CustomersController> logger)
    {
        _sapWriter = sapWriter;
        _sapReader = sapReader;
        _cache = cache;
        _credit = credit;
        _logger = logger;
    }

    // =====================================================
    // POST /api/customers
    // Create customer in SAP, then pull back to cache.
    // Idempotent by OdooCustomerId (updates if already exists).
    // =====================================================
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertCustomerDto dto)
    {
        if (dto == null)
            return BadRequest(new { error_code = "VALIDATION_ERROR", user_message = "Request body is required" });

        try
        {
            var cardCode = _sapWriter.CreateCustomer(dto);

            var sapCustomer = _sapReader.ReadCustomerByCardCode(cardCode);
            if (sapCustomer != null)
                await _cache.UpsertCustomerAsync(sapCustomer);

            _logger.LogInformation("✅ Customer created | CardCode={CardCode}", cardCode);

            return Ok(new { CardCode = cardCode, Message = "Customer created successfully" });
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
    // PUT /api/customers/{cardCode}
    // =====================================================
    [HttpPut("{cardCode}")]
    public async Task<IActionResult> Update(string cardCode, [FromBody] UpsertCustomerDto dto)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            return BadRequest(new { error_code = "VALIDATION_ERROR", user_message = "cardCode is required" });

        if (dto == null)
            return BadRequest(new { error_code = "VALIDATION_ERROR", user_message = "Request body is required" });

        try
        {
            _sapWriter.UpdateCustomer(cardCode, dto);

            var sapCustomer = _sapReader.ReadCustomerByCardCode(cardCode);
            if (sapCustomer != null)
                await _cache.UpsertCustomerAsync(sapCustomer);

            return Ok(new { CardCode = cardCode, Message = "Customer updated successfully" });
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
    // POST /api/customers/{cardCode}/deactivate
    // Freezes the BP in SAP (blocks new transactions).
    // =====================================================
    [HttpPost("{cardCode}/deactivate")]
    public async Task<IActionResult> Deactivate(string cardCode)
    {
        try
        {
            _sapWriter.DeactivateCustomer(cardCode);

            // Refresh cache so IsActive = false is reflected immediately
            var sapCustomer = _sapReader.ReadCustomerByCardCode(cardCode);
            if (sapCustomer != null)
                await _cache.UpsertCustomerAsync(sapCustomer);

            return Ok(new { CardCode = cardCode, Message = "Customer deactivated (frozen) successfully" });
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
    // POST /api/customers/{cardCode}/reactivate
    // Unfreezes the BP in SAP.
    // =====================================================
    [HttpPost("{cardCode}/reactivate")]
    public async Task<IActionResult> Reactivate(string cardCode)
    {
        try
        {
            _sapWriter.ReactivateCustomer(cardCode);

            var sapCustomer = _sapReader.ReadCustomerByCardCode(cardCode);
            if (sapCustomer != null)
                await _cache.UpsertCustomerAsync(sapCustomer);

            return Ok(new { CardCode = cardCode, Message = "Customer reactivated successfully" });
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
    // GET /api/customers/{cardCode}
    // =====================================================
    [HttpGet("{cardCode}")]
    public async Task<IActionResult> GetByCardCode(string cardCode)
    {
        var customer = await _cache.GetCustomerAsync(cardCode);

        if (customer == null)
            return NotFound(new { Message = "Customer not found" });

        return Ok(customer);
    }

    // =====================================================
    // GET /api/customers?search=john&activeOnly=true&page=1&pageSize=50
    // Returns total count + paged items.
    // search: filters CardCode or CardName (contains, case-insensitive)
    // activeOnly: if true, excludes frozen/inactive customers
    // =====================================================
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] bool? activeOnly = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var (total, items) = await _cache.GetCustomersAsync(page, pageSize, search, activeOnly);

        return Ok(new
        {
            total,
            page,
            pageSize,
            items
        });
    }

    // =====================================================
    // GET /api/customers/{cardCode}/credit
    // =====================================================
    [HttpGet("{cardCode}/credit")]
    public async Task<IActionResult> GetCustomerCredit(string cardCode)
    {
        var customer = await _credit.RefreshAndGetCustomerCreditAsync(cardCode);

        if (customer == null)
            return NotFound(new { Message = "Customer not found" });

        return Ok(new
        {
            customer.CardCode,
            customer.CreditLimit,
            customer.OutstandingBalance,
            customer.AvailableCredit,
            customer.CreditUpdatedAt
        });
    }

    // =====================================================
    // POST /api/customers/{cardCode}/refresh
    // Pulls latest data from SAP into the cache.
    // =====================================================
    [HttpPost("{cardCode}/refresh")]
    public async Task<IActionResult> RefreshFromSap(string cardCode)
    {
        var sapCustomer = _sapReader.ReadCustomerByCardCode(cardCode);

        if (sapCustomer == null)
            return NotFound(new { Message = "Customer not found in SAP" });

        await _cache.UpsertCustomerAsync(sapCustomer);

        return Ok(new { CardCode = cardCode, Message = "Customer refreshed from SAP" });
    }

    // =====================================================
    // POST /api/customers/force-full
    // Clears cache and rebuilds from SAP entirely.
    // =====================================================
    [HttpPost("force-full")]
    public async Task<IActionResult> ForceFull()
    {
        _logger.LogWarning("⚠ FORCE FULL CUSTOMER SYNC triggered manually");

        await _cache.ClearAllCustomersFastAsync();

        const int batchSize = 500;
        string lastCardCode = "";
        int total = 0;

        while (true)
        {
            var batch = _sapReader.ReadCustomerBatchAfter(lastCardCode, batchSize);

            if (batch.Count == 0)
                break;

            await _cache.UpsertCustomersAsync(batch);

            total += batch.Count;
            lastCardCode = batch.Last().CardCode;

            if (batch.Count < batchSize)
                break;
        }

        return Ok(new { Message = "Full customer sync completed", TotalSynced = total });
    }
}
