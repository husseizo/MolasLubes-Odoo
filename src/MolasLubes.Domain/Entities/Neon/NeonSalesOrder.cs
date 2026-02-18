using System.ComponentModel.DataAnnotations;

namespace MolasLubes.Domain.Entities.Neon;

public class NeonSalesOrder
{
    public int SapDocEntry { get; set; }

    public int DocNum { get; set; }

    public string CustomerCode { get; set; } = null!;
    public string CustomerName { get; set; } = null!;

    public string Status { get; set; } = null!;



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

    public decimal DocTotal { get; set; }
    public DateTime DocDate { get; set; }

    public int? SalesPersonCode { get; set; }
    public string? SalesPersonName { get; set; }

    public DateTime SyncedAt { get; set; }

    public ICollection<NeonSalesOrderLine> Lines { get; set; }
       = new List<NeonSalesOrderLine>();
}