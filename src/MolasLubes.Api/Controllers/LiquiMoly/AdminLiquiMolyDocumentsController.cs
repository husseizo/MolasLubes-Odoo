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
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string? brand = null,
        [FromQuery] string? profile = null)
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
            var profileOverride = ResolveProfileOverride(brand, profile);
            var document = _reader.GetDocument(docType, docEntry, profileOverride);
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

    [HttpGet("{docType}/{docEntry:int}/lines/{lineNum:int}")]
    public IActionResult GetLine(
        string docType,
        int docEntry,
        int lineNum,
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string? brand = null,
        [FromQuery] string? profile = null)
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
            var profileOverride = ResolveProfileOverride(brand, profile);
            var line = _reader.GetLine(docType, docEntry, lineNum, profileOverride);
            if (line == null)
            {
                return NotFound(new
                {
                    message = $"Line {lineNum} not found in '{docType}' DocEntry {docEntry}.",
                    docType = docType.ToUpperInvariant(),
                    docEntry,
                    lineNum
                });
            }

            return Ok(line);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "Failed to load SAP document line.",
                detail = ex.Message
            });
        }
    }

    private static string? ResolveProfileOverride(string? brand, string? profile)
    {
        if (!string.IsNullOrWhiteSpace(profile))
            return profile.Trim();

        if (string.IsNullOrWhiteSpace(brand))
            return null;

        return brand.Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant() switch
        {
            "AUTOHUB" => "AutoHub",
            "LIQUIMOLY" => "MolasLubes",
            _ => null
        };
    }
}
