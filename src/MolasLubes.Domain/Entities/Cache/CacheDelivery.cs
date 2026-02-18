using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MolasLubes.Domain.Entities.Cache;

public class CacheDelivery
{
    // =========================
    // SAP identity
    // =========================
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int SapDocEntry { get; set; }
    public int SapDocNum { get; set; }

    public string CardCode { get; set; } = null!;
    public DateTime DeliveryDate { get; set; }

    public int? BaseOrderEntry { get; set; }
    public decimal DeliveredQty { get; set; }

    // =========================
    // 🔥 TRUE DELTA TRACKING
    // =========================

    /// <summary>
    /// ODLN.UpdateDate from SAP
    /// Used for delta watermarking
    /// </summary>
    public DateTime SapUpdateDate { get; set; }

    /// <summary>
    /// True if ODLN.CANCELED = 'Y'
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// When this row was synced into cache
    /// </summary>
    public DateTime? LastSapSyncAt { get; set; }

    // =========================
    // 🔗 ODOO FIELDS
    // =========================
    public string? OdooDeliveryId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
    public string? OdooSyncDir { get; set; }
}