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

    public PaymentsCommandController(SapPaymentWriter writer)
    {
        _writer = writer;
    }

    [HttpPost("create")]
    public IActionResult Create([FromBody] CreatePaymentDto dto)
    {
        try
        {
            var r = _writer.CreateIncomingPayment(dto);
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
}