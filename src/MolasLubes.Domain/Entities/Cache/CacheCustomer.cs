using System;
using System.Collections.Generic;
using System.Text;

namespace MolasLubes.Domain.Entities.Cache;

public class CacheCustomer
{
    public string CardCode { get; set; } = null!;
    public string CardName { get; set; } = null!;

    public bool IsActive { get; set; }

    public int? PriceList { get; set; }
    public int? SlpCode { get; set; }

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

    // SAP → Odoo mapping (UDF)
    public string? OdooCustomerId { get; set; }
    // =========================
    // 🔗 ODOO / SAP SYNC
    // =========================
    public string? OdooPartnerId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
    public string? OdooSyncDir { get; set; }
    public DateTime LastSapDeltaAt { get; set; }
    public decimal? CreditLimit { get; set; }
    public decimal? OutstandingBalance { get; set; }
    public decimal? AvailableCredit { get; set; }
    public DateTime? CreditUpdatedAt { get; set; }



}