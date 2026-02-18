namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapDeliveryDto
{
    // =========================
    // SAP HEADER
    // =========================
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
    public string CardCode { get; set; } = default!;
    public DateTime DocDate { get; set; }

    /// <summary>
    /// SAP ODLN.UpdateDate (for delta sync)
    /// </summary>
    public DateTime SapUpdateDate { get; set; }

    /// <summary>
    /// ODLN.CANCELED = 'Y'
    /// </summary>
    public bool IsCancelled { get; set; }

    // =========================
    // LINK TO SALES ORDER
    // =========================
    public int BaseOrderEntry { get; set; }   // ORDR.DocEntry

    public decimal DeliveredQuantity { get; set; }

    // =========================
    // 🔗 ODOO HEADER UDFS (ODLN)
    // =========================
    public string? OdooDeliveryId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }

    // =========================
    // 🔗 ODOO LINE UDFS (DLN1)
    // =========================
    public string? OdooMoveId { get; set; }
    public string? OdooSalesOrderLineId { get; set; }
}
