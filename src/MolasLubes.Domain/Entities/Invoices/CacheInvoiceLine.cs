using MolasLubes.Domain.Entities.Cache;

namespace MolasLubes.Domain.Entities.Invoices;

public class CacheInvoiceLine
{
    public int Id { get; set; }

    // 🔗 FK → CacheInvoices.SapDocEntry
    public int SapDocEntry { get; set; }

    public CacheInvoice? Invoice { get; set; }

    public string ItemCode { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }

    public int BaseEntry { get; set; }   // ODLN / ORDR DocEntry
    public int BaseLine { get; set; }

    // =========================
    // 🔗 ODOO LINE UDF
    // =========================
    public string? OdooInvoiceLineId { get; set; }
}
