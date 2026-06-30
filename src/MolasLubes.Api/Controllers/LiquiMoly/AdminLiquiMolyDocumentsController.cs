using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Security;
using MolasLubes.Infrastructure.Services.Caching;

namespace MolasLubes.Api.Controllers.LiquiMoly;

[ApiController]
[Route("api/admin/liquimoly/documents")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminLiquiMolyDocumentsController : ControllerBase
{
    private readonly SapLiquiMolyDocumentReader _reader;
    private readonly LiquiMolyRoleService _roleService;
    private readonly NeonApiCacheService _cache;

    public AdminLiquiMolyDocumentsController(
        SapLiquiMolyDocumentReader reader,
        LiquiMolyRoleService roleService,
        NeonApiCacheService cache)
    {
        _reader = reader;
        _roleService = roleService;
        _cache = cache;
    }

    [HttpGet("{docType}/{docEntry:int}")]
    public async Task<IActionResult> GetDocument(
        string docType,
        int docEntry,
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string? brand = null,
        [FromQuery] string? profile = null)
    {
        try
        {
            _roleService.AuthorizeAny(HttpContext.Items["CurrentUser"] as InternalUser, actorSapUserCode, LiquiMolyRole.Viewer);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }

        var profileOverride = ResolveProfileOverride(brand, profile);
        var cacheKey = $"doc:{docType.ToUpperInvariant()}:{docEntry}:{profileOverride}";
        var cached = await _cache.GetRawAsync(cacheKey);
        if (cached != null) return Content(cached, "application/json");

        try
        {
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

            await _cache.SetAsync(cacheKey, $"documents/{docType}", document, TimeSpan.FromMinutes(30));
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
    public async Task<IActionResult> GetLine(
        string docType,
        int docEntry,
        int lineNum,
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string? brand = null,
        [FromQuery] string? profile = null)
    {
        try
        {
            _roleService.AuthorizeAny(HttpContext.Items["CurrentUser"] as InternalUser, actorSapUserCode, LiquiMolyRole.Viewer);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, ex.Message);
        }

        var profileOverride = ResolveProfileOverride(brand, profile);
        var cacheKey = $"doc:{docType.ToUpperInvariant()}:{docEntry}:line:{lineNum}:{profileOverride}";
        var cached = await _cache.GetRawAsync(cacheKey);
        if (cached != null) return Content(cached, "application/json");

        try
        {
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

            await _cache.SetAsync(cacheKey, $"documents/{docType}/lines", line, TimeSpan.FromMinutes(30));
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
