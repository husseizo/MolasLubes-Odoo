namespace MolasLubes.Domain.Entities.Neon;

public class NeonInvoiceLine
{
    public long Id { get; set; }

    // 🔗 FK → NeonInvoices.SapDocEntry
    public int InvoiceEntry { get; set; }

    public NeonInvoice? Invoice { get; set; }

    // =========================
    // PRODUCT
    // =========================
    public string ItemCode { get; set; } = null!;
    public string Description { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public decimal GrossBuyPr { get; set; }

    public int BaseEntry { get; set; }
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
