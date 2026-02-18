using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MolasLubes.Domain.Entities.Cache;

[Table("CachePayment")] // 👈 MUST match existing table name
public class CachePayment
{
    [Key]
    public int SapDocEntry { get; set; }

    public int SapDocNum { get; set; }

    public string CardCode { get; set; } = null!;
    public DateTime DocDate { get; set; }

    public decimal TotalPaid { get; set; }

    public DateTime CachedAt { get; set; } = DateTime.UtcNow;

    // 🔗 ODOO UDFS
    public string? OdooPaymentId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
}