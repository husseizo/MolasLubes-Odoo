using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Controllers.LiquiMoly;

[ApiController]
[Route("api/admin/liquimoly/documents")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminLiquiMolyDocumentsController : ControllerBase
{
    private readonly SapLiquiMolyDocumentReader _reader;
    private readonly LiquiMolyRoleService _roleService;

    public AdminLiquiMolyDocumentsController(
        SapLiquiMolyDocumentReader reader,
        LiquiMolyRoleService roleService)
    {
        _reader = reader;
        _roleService = roleService;
    }

    [HttpGet("{docType}/{docEntry:int}")]
    public IActionResult GetDocument(
        string docType,
        int docEntry,
        [FromQuery] string actorSapUserCode = "")
    {
        try
        {
            _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }

        try
        {
            var document = _reader.GetDocument(docType, docEntry);
            if (document == null)
            {
                return NotFound(new
                {
                    message = $"Document '{docType}' with DocEntry {docEntry} was not found.",
                    docType = docType.ToUpperInvariant(),
                    docEntry
                });
            }

            return Ok(document);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "Failed to load SAP document details.",
                detail = ex.Message
            });
        }
    }
}
