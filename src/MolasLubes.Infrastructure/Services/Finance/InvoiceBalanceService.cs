using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Finance;

public class InvoiceBalanceService
{
    private readonly MolasCacheDbContext _db;

    public InvoiceBalanceService(MolasCacheDbContext db)
    {
        _db = db;
    }

    public async Task<InvoiceBalanceResult?> GetInvoiceBalanceAsync(int sapDocEntry)
    {
        var invoice = await _db.CacheInvoices
            .FirstOrDefaultAsync(x => x.SapDocEntry == sapDocEntry);

        if (invoice == null)
            return null;

        // ⚠ PAYMENTS NOT ENABLED YET
        // Will be calculated once CachePayment is added
        var totalPaid = 0m;

        var balance = invoice.DocTotal - totalPaid;

        return new InvoiceBalanceResult
        {
            SapDocEntry = invoice.SapDocEntry,
            DocTotal = invoice.DocTotal,
            TotalPaid = totalPaid,
            Balance = balance,
            Status = "Unpaid"
        };
    }
}

public class InvoiceBalanceResult
{
    public int SapDocEntry { get; set; }
    public decimal DocTotal { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Balance { get; set; }
    public string Status { get; set; } = null!;
}