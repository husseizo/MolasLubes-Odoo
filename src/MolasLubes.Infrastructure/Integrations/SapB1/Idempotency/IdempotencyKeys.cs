namespace MolasLubes.Infrastructure.Integrations.SapB1.Idempotency;

public static class IdempotencyKeys
{
    // Recommended:
    // Delivery  : ODLN.NumAtCard (external ref)
    // Invoice   : OINV.U_SourceODLN (delivery docEntry)
    // Payment   : ORCT.CounterReference or U_SourceInvoice + Amount

    public static string PaymentCounterRef(int invoiceEntry, decimal amount)
        => $"INV:{invoiceEntry}|AMT:{amount:0.00}";

    // Multi-invoice payment: sorted invoice entries + total
    public static string MultiPaymentCounterRef(IEnumerable<int> invoiceEntries, decimal total)
    {
        var sorted = string.Join("_", invoiceEntries.OrderBy(x => x));
        return $"MULTI:{sorted}|AMT:{total:0.00}";
    }
}