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