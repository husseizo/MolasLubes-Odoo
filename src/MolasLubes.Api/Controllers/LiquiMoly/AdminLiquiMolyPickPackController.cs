using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Api.Security;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Controllers.LiquiMoly;

[ApiController]
[Route("api/admin/liquimoly")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AdminLiquiMolyPickPackController : ControllerBase
{
    private readonly NeonDbContext _neon;
    private readonly LiquiMolyRoleService _roleService;

    public AdminLiquiMolyPickPackController(
        NeonDbContext neon,
        LiquiMolyRoleService roleService)
    {
        _neon        = neon;
        _roleService = roleService;
    }

    // ── GET /api/admin/liquimoly/pick-pack ────────────────────────────────────
    // Open sales order lines ready for picking and packing.
    // NeonSalesOrderLine has no OpenQty/WhsCode; Quantity is used as the pick qty.

    [HttpGet("pick-pack")]
    public async Task<IActionResult> GetPickPack(
        [FromQuery] string actorSapUserCode = "",
        [FromQuery] string? search = null,
        [FromQuery] string? whsCode = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var auth = AuthorizeViewer(actorSapUserCode);
        if (auth != null) return auth;

        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 500);

        try
        {
            var q = _neon.SalesOrderLines
                .AsNoTracking()
                .Where(l =>
                    l.SalesOrder!.Status == "O" &&
                    l.Quantity > 0);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var needle = search.Trim().ToLower();
                q = q.Where(l =>
                    l.ItemCode.ToLower().Contains(needle) ||
                    l.ItemName.ToLower().Contains(needle) ||
                    l.SalesOrder!.CustomerCode.ToLower().Contains(needle));
            }

            var total = await q.CountAsync(ct);

            var rows = await q
                .OrderBy(l => l.SalesOrder!.DocDate)
                .ThenBy(l => l.SalesOrder!.SapDocEntry)
                .ThenBy(l => l.Id)
                .Skip(skip)
                .Take(take)
                .Select(l => new
                {
                    DocNum            = l.SalesOrder!.DocNum,
                    DocEntry          = l.SalesOrder.SapDocEntry,
                    DocDate           = l.SalesOrder.DocDate,
                    CardCode          = l.SalesOrder.CustomerCode,
                    CardName          = l.SalesOrder.CustomerName,
                    SalesPersonCode   = l.SalesOrder.SalesPersonCode,
                    SalesPersonName   = l.SalesOrder.SalesPersonName,
                    ItemCode          = l.ItemCode,
                    Description       = l.ItemName,
                    Quantity          = l.Quantity,
                    OpenQty           = l.Quantity,   // LiquiMoly lines have no OpenQty column
                    WhsCode           = (string?)null // LiquiMoly lines have no WhsCode column
                })
                .ToListAsync(ct);

            return Ok(new
            {
                total,
                skip,
                take,
                hasMore = skip + rows.Count < total,
                rows
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to load LiquiMoly pick-pack list.", detail = ex.Message });
        }
    }

    private IActionResult? AuthorizeViewer(string actorSapUserCode)
    {
        try
        {
            _roleService.AuthorizeAny(HttpContext.Items["CurrentUser"] as InternalUser, actorSapUserCode, LiquiMolyRole.Viewer);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { message = ex.Message });
        }
    }
}
