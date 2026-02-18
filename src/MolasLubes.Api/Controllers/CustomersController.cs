using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MolasLubes.Api.Security; // 👈 HII NI MUHIMU
using MolasLubes.Application.Customers;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
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

        return Ok(new
        {
            Message = "Full customer sync completed",
            TotalSynced = total
        });
    }

    // =====================================================
    // CREATE CUSTOMER (SAP → CACHE)
    // =====================================================
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertCustomerDto dto)
    {
        if (dto == null)
            return BadRequest("Request body is required");

        var cardCode = _sapWriter.CreateCustomer(dto);

        var sapCustomer = _sapReader.ReadCustomerByCardCode(cardCode);
        if (sapCustomer != null)
            await _cache.UpsertCustomerAsync(sapCustomer);

        _logger.LogInformation(
            "✅ Customer created + cache updated | CardCode={CardCode}",
            cardCode);

        return Ok(new
        {
            CardCode = cardCode,
            Message = "Customer created successfully"
        });
    }

    // =====================================================
    // UPDATE CUSTOMER
    // =====================================================
    [HttpPut("{cardCode}")]
    public async Task<IActionResult> Update(
        string cardCode,
        [FromBody] UpsertCustomerDto dto)
    {
        if (dto == null)
            return BadRequest("Request body is required");

        _sapWriter.UpdateCustomer(cardCode, dto);

        var sapCustomer = _sapReader.ReadCustomerByCardCode(cardCode);
        if (sapCustomer != null)
            await _cache.UpsertCustomerAsync(sapCustomer);

        return Ok(new
        {
            CardCode = cardCode,
            Message = "Customer updated successfully"
        });
    }

    // =====================================================
    // GET CUSTOMER
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
    // LIST CUSTOMERS
    // =====================================================
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var customers = await _cache.GetCustomersAsync(page, pageSize);
        return Ok(customers);
    }

    // =====================================================
    // CUSTOMER CREDIT
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
    // MANUAL REFRESH
    // =====================================================
    [HttpPost("{cardCode}/refresh")]
    public async Task<IActionResult> RefreshFromSap(string cardCode)
    {
        var sapCustomer = _sapReader.ReadCustomerByCardCode(cardCode);

        if (sapCustomer == null)
            return NotFound(new { Message = "Customer not found in SAP" });

        await _cache.UpsertCustomerAsync(sapCustomer);

        return Ok(new
        {
            CardCode = cardCode,
            Message = "Customer refreshed from SAP"
        });
    }
}