using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Caching;

public class AutoHubNeonDeliveryService
{
    private readonly AutoHubDbContext _db;

    public AutoHubNeonDeliveryService(AutoHubDbContext db) => _db = db;

    // ── /inventory/deliveries ─────────────────────────────────────────────

    public async Task<InventoryDeliveryAggregateResponse> GetDeliveriesAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? search,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var (from, to) = ResolveRange(dateFrom, dateTo);
        var rows = await BuildAggregatesAsync(from, to, search, ct);

        var paged = rows
            .OrderByDescending(r => r.LastDeliveredAt ?? DateTime.MinValue)
            .ThenByDescending(r => r.LastDeliveryDocEntry)
            .ThenBy(r => r.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Skip(skip)
            .Take(take)
            .ToList();

        return new InventoryDeliveryAggregateResponse
        {
            DateFrom = from,
            DateTo   = to,
            AsOfUtc  = DateTime.UtcNow,
            Version  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Total    = rows.Count,
            Rows     = paged
        };
    }

    // ── /inventory/deliveries/today-summary ──────────────────────────────

    public async Task<InventoryTodayDeliveryResponse> GetTodayDeliveriesAsync(
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            GetDarEsSalaamTimeZone()));

        var rows = await BuildAggregatesAsync(today, today, search: null, ct);

        var byItem = rows
            .GroupBy(r => r.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var best = g.OrderByDescending(r => r.LastDeliveryDocEntry ?? 0).First();
                return new InventoryTodayDeliveryItem
                {
                    ItemCode             = g.Key,
                    ItemName             = best.ItemName,
                    Brand                = "AutoHub",
                    ArticleNumber        = g.Key,
                    TanNumber            = null,
                    DeliveredQty         = g.Sum(r => r.DeliveredQty),
                    DeliveryCount        = g.Sum(r => r.DeliveryCount),
                    LastDeliveredAt      = g.Max(r => r.LastDeliveredAt),
                    LastDeliveryDocEntry = best.LastDeliveryDocEntry,
                    LastDeliveryDocNum   = best.LastDeliveryDocNum,
                    Warehouses           = g.Select(r => r.Warehouse)
                                           .Where(w => !string.IsNullOrWhiteSpace(w))
                                           .Distinct(StringComparer.OrdinalIgnoreCase)
                                           .ToList()
                };
            })
            .OrderByDescending(x => x.LastDeliveredAt ?? DateTime.MinValue)
            .ThenByDescending(x => x.LastDeliveryDocEntry ?? 0)
            .ThenBy(x => x.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InventoryTodayDeliveryResponse
        {
            AsOfUtc = DateTime.UtcNow,
            Version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Date    = today,
            Total   = byItem.Count,
            Items   = byItem
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private async Task<List<InventoryDeliveryAggregateRow>> BuildAggregatesAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        CancellationToken ct)
    {
        var tz      = GetDarEsSalaamTimeZone();
        var utcFrom = TimeZoneInfo.ConvertTimeToUtc(dateFrom.ToDateTime(TimeOnly.MinValue), tz);
        var utcTo   = TimeZoneInfo.ConvertTimeToUtc(dateTo.ToDateTime(TimeOnly.MaxValue), tz);

        var linesQuery = _db.AutoHubDeliveryLines
            .AsNoTracking()
            .Where(l =>
                !l.Delivery!.IsCancelled &&
                l.Delivery.DocDate >= utcFrom &&
                l.Delivery.DocDate <= utcTo);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim().ToLower();
            linesQuery = linesQuery.Where(l =>
                l.ItemCode.ToLower().Contains(needle) ||
                l.Description.ToLower().Contains(needle));
        }

        var lines = await linesQuery
            .Select(l => new
            {
                l.ItemCode,
                l.Description,
                l.Quantity,
                l.WhsCode,
                l.Delivery!.CardCode,
                l.Delivery.CardName,
                l.Delivery.DocDate,
                l.Delivery.DocEntry,
                l.Delivery.DocNum
            })
            .ToListAsync(ct);

        if (lines.Count == 0)
            return new List<InventoryDeliveryAggregateRow>();

        return lines
            .GroupBy(l => l.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(l => l.DocDate).ThenByDescending(l => l.DocEntry);
                var last    = ordered.First();

                return new InventoryDeliveryAggregateRow
                {
                    ItemCode             = g.Key,
                    ItemName             = g.First().Description,
                    Brand                = "AutoHub",
                    ItemBrand            = "AutoHub",
                    ArticleNumber        = g.Key,
                    Warehouse            = last.WhsCode ?? "",
                    DeliveredQty         = g.Sum(l => l.Quantity),
                    DeliveryCount        = g.Select(l => l.DocEntry).Distinct().Count(),
                    LastDeliveredAt      = g.Max(l => (DateTime?)l.DocDate),
                    LastDeliveryDocEntry = last.DocEntry,
                    LastDeliveryDocNum   = last.DocNum.ToString(),
                    CustomerCode         = last.CardCode,
                    CustomerName         = last.CardName,
                    AsOfUtc              = DateTime.UtcNow,
                    Version              = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            })
            .ToList();
    }

    private static (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to)
    {
        var today    = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetDarEsSalaamTimeZone()));
        var resolved = (from ?? today.AddDays(-30), to ?? today);
        if (resolved.Item1 > resolved.Item2)
            resolved = (resolved.Item2, resolved.Item1);
        return resolved;
    }

    private static TimeZoneInfo GetDarEsSalaamTimeZone() =>
        TimeZoneInfo.FindSystemTimeZoneById("E. Africa Standard Time");
}
