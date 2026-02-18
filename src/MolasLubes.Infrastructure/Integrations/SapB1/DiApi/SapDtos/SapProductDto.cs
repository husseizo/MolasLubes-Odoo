using System;
using System.Collections.Generic;
using System.Text;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapProductDto
{
    public string ItemCode { get; set; } = null!;
    public string WarehouseCode { get; set; } = null!;
    public string ItemName { get; set; } = null!;
    public decimal OnHand { get; set; }
    public string? Barcode { get; set; }

    // 💰 Price lists
    public Dictionary<int, decimal> PriceLists { get; set; } = new();

    // 🔗 ODOO UDFS
    public string? OdooProductId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
}