namespace MolasLubes.Domain.Entities.Neon;

public class NeonCustomer
{
    public string CardCode { get; set; } = null!;
    public string CardName { get; set; } = null!;

    public int? PriceList { get; set; }
    
    // =========================
    // 🔗 ODOO / SAP SYNC
    // =========================
    public string? OdooPartnerId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
    public string? OdooSyncDir { get; set; }

    public decimal CreditLimit { get; set; }
    public decimal OutstandingBalance { get; set; }
    public decimal AvailableCredit { get; set; }

    public bool IsActive { get; set; }

    public int? SalesPersonCode { get; set; }
    public string? SalesPersonName { get; set; }

    public DateTime SyncedAt { get; set; }
}