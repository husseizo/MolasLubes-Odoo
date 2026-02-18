using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MolasLubes.Domain.Entities.Invoices;

namespace MolasLubes.Domain.Entities.Cache;

[Table("CacheInvoices")]
public class CacheInvoice
{
    [Key]
    public int SapDocEntry { get; set; }
    public int BaseOrderEntry { get; set; }
    

    public int SapDocNum { get; set; }

    [Required]
    [MaxLength(20)]
    public string CardCode { get; set; } = null!;

    public DateTime DocDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DocTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal VatSum { get; set; }

    // =========================
    // 🔗 ODOO / SAP SYNC
    // =========================
    public string? OdooInvoiceId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
    public string? OdooSyncDir { get; set; }




    public DateTime CachedAt { get; set; } = DateTime.UtcNow;

    // =========================
    // NAVIGATION
    // =========================
    public ICollection<CacheInvoiceLine> Lines { get; set; }
        = new List<CacheInvoiceLine>();
}