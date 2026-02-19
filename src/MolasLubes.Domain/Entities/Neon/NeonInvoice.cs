namespace MolasLubes.Domain.Entities.Neon;

public class NeonInvoice
{
    public int SapDocEntry { get; set; }
    public int DocNum { get; set; }

    public string CustomerCode { get; set; } = null!;
    public string? CardName { get; set; }

    // =========================
    // 🔗 ODOO / SAP SYNC
    // =========================
    public string? OdooInvoiceId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
    public string? OdooSyncDir { get; set; }

    public decimal DocTotal { get; set; }
    public decimal VatSum { get; set; }
    public decimal PaidAmount { get; set; }
    public bool IsPaid { get; set; }

    public DateTime InvoiceDate { get; set; }
    public DateTime SyncedAt { get; set; }

    // =========================
    // 🔗 PAYMENTS
    // =========================
    public ICollection<NeonPayment> Payments { get; set; }
        = new List<NeonPayment>();

    // =========================
    // 🔗 LINES (INV1)
    // =========================
    public ICollection<NeonInvoiceLine> Lines { get; set; }
        = new List<NeonInvoiceLine>();
}