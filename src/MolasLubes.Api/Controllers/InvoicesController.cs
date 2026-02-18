using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Application.Invoices;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Integrations.SapB1.Errors;

namespace MolasLubes.Api.Controllers;

[ApiController]
[Route("api/v1/invoices")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class InvoicesCommandController : ControllerBase
{
    private readonly SapInvoiceWriter _writer;

    public InvoicesCommandController(SapInvoiceWriter writer)
    {
        _writer = writer;
    }

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
}