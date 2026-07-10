using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Api.Security;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Api.Controllers.AutoHub;

[ApiController]
[Route("api/admin/autohub")]
[ServiceFilter(typeof(ApiKeyAttribute))]
public class AutoHubPickPackController : ControllerBase
{
    private readonly AutoHubDbContext _db;
    private readonly LiquiMolyRoleService _roleService;

    public AutoHubPickPackController(
        AutoHubDbContext db,
        LiquiMolyRoleService roleService)
    {
        _db          = db;
        _roleService = roleService;
    }

    // ── GET /api/admin/autohub/pick-pack ──────────────────────────────────────
    // Open sales order lines (DocStatus = 'O', OpenQty > 0) ready for picking.

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
            var q = _db.AutoHubSalesOrderLines
                .AsNoTracking()
                .Where(l =>
                    l.SalesOrder!.DocStatus == "O" &&
                    l.OpenQty > 0);

            if (!string.IsNullOrWhiteSpace(whsCode))
                q = q.Where(l => l.WhsCode == whsCode);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var needle = search.Trim().ToLower();
                q = q.Where(l =>
                    l.ItemCode.ToLower().Contains(needle) ||
                    (l.Description != null && l.Description.ToLower().Contains(needle)) ||
                    l.SalesOrder!.CardCode.ToLower().Contains(needle));
            }

            var total = await q.CountAsync(ct);

            var rows = await q
                .OrderBy(l => l.SalesOrder!.DocDate)
                .ThenBy(l => l.SalesOrder!.DocEntry)
                .ThenBy(l => l.LineNum)
                .Skip(skip)
                .Take(take)
                .Select(l => new
                {
                    DocNum   = l.SalesOrder!.DocNum,
                    DocEntry = l.SalesOrder.DocEntry,
                    DocDate  = l.SalesOrder.DocDate,
                    CardCode = l.SalesOrder.CardCode,
                    CardName = l.SalesOrder.CardName,
                    l.ItemCode,
                    Description = l.Description,
                    l.Quantity,
                    l.OpenQty,
                    l.WhsCode
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
            return StatusCode(500, new { message = "Failed to load AutoHub pick-pack list.", detail = ex.Message });
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
