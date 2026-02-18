namespace MolasLubes.Domain.Entities.Neon;

public class NeonPayment
{
    public int SapDocEntry { get; set; }
    public int DocNum { get; set; }

    public string CustomerCode { get; set; } = null!;

    // =========================
    // 🔗 INVOICE LINK (SAP: RCT2.DocEntry)
    // =========================
    public int InvoiceEntry { get; set; }      // FK → NeonInvoice.SapDocEntry
    public NeonInvoice? Invoice { get; set; }

    // =========================
    // 🔗 ODOO / SAP SYNC
    // =========================
    public string? OdooPaymentId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
    public string? OdooSyncDir { get; set; }

    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public DateTime SyncedAt { get; set; }
}