namespace MolasLubes.Domain.Entities.Cache;

public class CacheInvoiceLine
{
    public int Id { get; set; }

    // 🔗 FK → CacheInvoices.SapDocEntry
    public int SapDocEntry { get; set; }

    public CacheInvoice? Invoice { get; set; }

    public string ItemCode { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public decimal GrossBuyPr { get; set; }

    public int BaseEntry { get; set; }   // ODLN / ORDR DocEntry
    public int BaseLine { get; set; }

    // =========================
    // 🔗 ODOO LINE UDFs
    // =========================
    public string? OdooInvoiceLineId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
}
