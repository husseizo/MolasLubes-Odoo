using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Orders;

public class OrderLifecycleService
{
    private readonly MolasCacheDbContext _db;

    public OrderLifecycleService(MolasCacheDbContext db)
    {
        _db = db;
    }

    public async Task<OrderLifecycleDto?> GetLifecycleAsync(int sapDocEntry)
    {
        var order = await _db.CacheSalesOrders
            .FirstOrDefaultAsync(x => x.SapDocEntry == sapDocEntry);

        if (order == null) return null;

        var invoiced = await _db.CacheInvoices
            .AnyAsync(i => i.BaseOrderEntry == order.SapDocEntry);

        return new OrderLifecycleDto
        {
            SapDocEntry = order.SapDocEntry,
            Status =
                order.DocStatus == "X" ? "Cancelled" :
                invoiced ? "Invoiced" :
                order.DocStatus == "D" ? "Delivered" :
                "Open"
        };
    }
}

public class OrderLifecycleDto
{
    public int SapDocEntry { get; set; }
    public string Status { get; set; } = null!;
}