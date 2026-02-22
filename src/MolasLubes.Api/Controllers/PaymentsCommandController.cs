using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Application.Payments;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Integrations.SapB1.Errors;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/v1/payments")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class PaymentsCommandController : ControllerBase
{
    private readonly SapPaymentWriter _writer;
    private readonly SapPaymentChannelReader _channelReader;

    public PaymentsCommandController(
        SapPaymentWriter writer,
        SapPaymentChannelReader channelReader)
    {
        _writer = writer;
        _channelReader = channelReader;
    }

    // =====================================================
    // POST /api/v1/payments/create
    // Supports multi-invoice application.
    // CashSum + TransferSum + CardSum must equal sum of SumApplied.
    // =====================================================
    [HttpPost("create")]
    public IActionResult Create([FromBody] CreateIncomingPaymentDto dto)
    {
        try
        {
            var r = _writer.CreateIncomingPaymentMulti(dto);
            return Ok(new { r.DocEntry, r.DocNum, r.AlreadyExists });
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
    // GET /api/v1/payments/channels
    // Returns available payment channels: bank accounts and credit card types.
    // =====================================================
    [HttpGet("channels")]
    public IActionResult GetChannels()
    {
        try
        {
            var channels = _channelReader.ReadPaymentChannels();
            return Ok(channels);
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
