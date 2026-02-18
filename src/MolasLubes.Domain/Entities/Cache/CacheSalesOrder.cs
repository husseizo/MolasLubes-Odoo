using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MolasLubes.Domain.Entities.Cache;

[Table("CacheSalesOrders")]
public class CacheSalesOrder
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    // SAP identity
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int SapDocEntry { get; set; }


    public int SapDocNum { get; set; }

    [Required]
    [MaxLength(20)]
    public string CustomerCode { get; set; } = null!;

    [Required]
    [MaxLength(1)]
    public string DocStatus { get; set; } = "O"; // O = Open, C = Closed

    // =========================
    // 🔗 ODOO UDFS (ORDR)
    // =========================
    [MaxLength(20)]
    public string? OdooSalesOrderId { get; set; }   // U_Odoo_SO_ID

    [MaxLength(10)]
    public string? OdooStatus { get; set; }         // U_Odoo_Status

    [MaxLength(255)]
    public string? OdooErrorMsg { get; set; }       // U_Odoo_ErrorMsg

    public DateTime? OdooLastSync { get; set; }     // U_Odoo_LastSync

    [MaxLength(10)]
    public string? OdooSyncDir { get; set; }        // U_Odoo_SyncDir

    // =========================
    // AUDIT
    // =========================
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdatedAt { get; set; }

    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    // =========================
    // NAVIGATION
    // =========================
    public ICollection<CacheSalesOrderLine> Lines { get; set; }
        = new List<CacheSalesOrderLine>();
}