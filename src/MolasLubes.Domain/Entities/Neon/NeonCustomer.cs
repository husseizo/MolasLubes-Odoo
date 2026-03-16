namespace MolasLubes.Domain.Entities.Neon;

public class NeonCustomer
{
    public string CardCode { get; set; } = null!;
    public string CardName { get; set; } = null!;

    public int? PriceList { get; set; }

    // =========================
    // CONTACT (from OCRD)
    // =========================
    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }

    // =========================
    // ADDRESSES (from CRD1)
    // =========================
    public string? BillToStreet { get; set; }
    public string? BillToCity { get; set; }
    public string? BillToCountry { get; set; }

    public string? ShipToStreet { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToCountry { get; set; }
    
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