using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Finance;

public class InvoiceBalanceService
{
    private readonly NeonDbContext _neonDb;

    public InvoiceBalanceService(NeonDbContext neonDb)
    {
        _neonDb = neonDb;
    }

    public async Task<InvoiceBalanceResult?> GetInvoiceBalanceAsync(int sapDocEntry)
    {
        // Read directly from the Neon read-replica which keeps PaidAmount up-to-date
        var invoice = await _neonDb.Invoices
            .FirstOrDefaultAsync(x => x.SapDocEntry == sapDocEntry);

        if (invoice == null)
            return null;

        var balance = invoice.DocTotal - invoice.PaidAmount;

        var status = invoice.IsPaid ? "Paid"
            : invoice.PaidAmount > 0 ? "PartiallyPaid"
            : "Unpaid";

        return new InvoiceBalanceResult
        {
            SapDocEntry = invoice.SapDocEntry,
            DocTotal    = invoice.DocTotal,
            TotalPaid   = invoice.PaidAmount,
            Balance     = balance,
            Status      = status
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