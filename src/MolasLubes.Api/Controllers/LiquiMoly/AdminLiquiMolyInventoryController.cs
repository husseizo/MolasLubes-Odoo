using Microsoft.AspNetCore.Mvc;
using MolasLubes.Api.Security;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Controllers.LiquiMoly;

[ApiController]
[Route("api/admin/liquimoly/inventory")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminLiquiMolyInventoryController : ControllerBase
{
    private const string DefaultProfile = "MolasLubes";

    private readonly SapLiquiMolyInventoryReader _reader;
    private readonly LiquiMolyRoleService _roleService;

    public AdminLiquiMolyInventoryController(
        SapLiquiMolyInventoryReader reader,
        LiquiMolyRoleService roleService)
    {
        _reader = reader;
        _roleService = roleService;
    }

    [HttpGet("stock")]
    public IActionResult GetStock(
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string profile = DefaultProfile,
        [FromQuery] string? search = null,
        [FromQuery] string? warehouseCode = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] bool includeZero = false,
        [FromQuery] bool onlyLiquiMoly = true)
    {
        var auth = AuthorizeViewer(actorSapUserCode);
        if (auth != null) return auth;

        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 1000);

        try
        {
            var data = _reader.GetStock(profile, search, warehouseCode, skip, take, includeZero, onlyLiquiMoly);
            return Ok(new
            {
                data.AsOfUtc,
                data.Version,
                data.Total,
                skip,
                take,
                hasMore = skip + data.Rows.Count < data.Total,
                rows = data.Rows
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to load inventory stock.", detail = ex.Message });
        }
    }

    [HttpGet("stock/{itemCode}")]
    public IActionResult GetStockForItem(
        string itemCode,
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string profile = DefaultProfile,
        [FromQuery] string? warehouseCode = null)
    {
        var auth = AuthorizeViewer(actorSapUserCode);
        if (auth != null) return auth;

        if (string.IsNullOrWhiteSpace(itemCode))
            return BadRequest(new { message = "itemCode is required." });

        try
        {
            var data = _reader.GetStockForItem(profile, itemCode.Trim(), warehouseCode);
            return Ok(data);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to load inventory stock by item.", detail = ex.Message });
        }
    }

    [HttpGet("stock/{itemCode}/movements")]
    public IActionResult GetMovements(
        string itemCode,
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string profile = DefaultProfile,
        [FromQuery] string? warehouseCode = null,
        [FromQuery] int take = 200)
    {
        var auth = AuthorizeViewer(actorSapUserCode);
        if (auth != null) return auth;

        if (string.IsNullOrWhiteSpace(itemCode))
            return BadRequest(new { message = "itemCode is required." });

        take = Math.Clamp(take, 1, 1000);

        try
        {
            var data = _reader.GetMovements(profile, itemCode.Trim(), warehouseCode, take);
            return Ok(data);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to load inventory stock movements.", detail = ex.Message });
        }
    }

    [HttpGet("stock/changes")]
    public IActionResult GetStockChanges(
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string profile = DefaultProfile,
        [FromQuery] long sinceVersion = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? warehouseCode = null,
        [FromQuery] bool includeZero = false,
        [FromQuery] bool onlyLiquiMoly = true)
    {
        var auth = AuthorizeViewer(actorSapUserCode);
        if (auth != null) return auth;

        if (sinceVersion < 0)
            return BadRequest(new { message = "sinceVersion must be >= 0." });

        try
        {
            var data = _reader.GetChanges(profile, sinceVersion, search, warehouseCode, includeZero, onlyLiquiMoly);
            return Ok(data);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to load inventory stock changes.", detail = ex.Message });
        }
    }

    [HttpGet("stock/summary")]
    public IActionResult GetStockSummary(
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string profile = DefaultProfile,
        [FromQuery] string? warehouseCode = null,
        [FromQuery] bool includeZero = false,
        [FromQuery] bool onlyLiquiMoly = true)
    {
        var auth = AuthorizeViewer(actorSapUserCode);
        if (auth != null) return auth;

        try
        {
            var data = _reader.GetSummary(profile, warehouseCode, includeZero, onlyLiquiMoly);
            return Ok(data);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to load inventory stock summary.", detail = ex.Message });
        }
    }

    private IActionResult? AuthorizeViewer(string actorSapUserCode)
    {
        try
        {
            _roleService.Authorize(actorSapUserCode, LiquiMolyRole.Viewer);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
