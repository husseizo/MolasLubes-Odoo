namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapCustomerDto
{
    // =========================
    // CORE
    // =========================
    public string CardCode { get; set; } = default!;
    public string CardName { get; set; } = default!;
    public string CardType { get; set; } = "C"; // Customer

    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }

    public int? PriceList { get; set; }     // OCRD.ListNum
    public int? SlpCode { get; set; }       // OCRD.SlpCode


    // =========================
    // ADDRESSES (CRD1 snapshot)
    // =========================
    public string? BillToStreet { get; set; }
    public string? BillToCity { get; set; }
    public string? BillToCountry { get; set; }

    public string? ShipToStreet { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToCountry { get; set; }

    // =========================
    // 🔗 ODOO UDFS (OCRD)
    // =========================
    public string? OdooPartnerId { get; set; }     // U_Odoo_Partner_ID
    public string? OdooStatus { get; set; }        // U_Odoo_Status
    public string? OdooErrorMsg { get; set; }      // U_Odoo_ErrorMsg
    public string? OdooSyncDir { get; set; }       // U_Odoo_SyncDir
    public DateTime? OdooLastSync { get; set; }    // U_Odoo_LastSync

    // =========================
    // DELTA SUPPORT
    // =========================
    public DateTime? UpdateDate { get; set; }
    public DateTime? UpdateTime { get; set; }
}