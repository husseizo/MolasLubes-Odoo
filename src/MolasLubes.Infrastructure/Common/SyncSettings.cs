namespace MolasLubes.Infrastructure.Common;

public class SyncSettings
{
    public bool EnableInvoiceCacheSync { get; set; }
    public bool EnablePaymentCacheSync { get; set; }
    public bool EnableNeonInvoiceSync { get; set; }
    public bool EnableNeonPaymentSync { get; set; }

    public bool EnableNeonCustomerSync { get; set; } = true;
}