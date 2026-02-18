namespace MolasLubes.Infrastructure.Integrations.SapB1.Idempotency;

public static class IdempotencyKeys
{
    // Recommended:
    // Delivery  : ODLN.NumAtCard (external ref)
    // Invoice   : OINV.U_SourceODLN (delivery docEntry)
    // Payment   : ORCT.CounterReference or U_SourceInvoice + Amount

    public static string PaymentCounterRef(int invoiceEntry, decimal amount)
        => $"INV:{invoiceEntry}|AMT:{amount:0.00}";
}