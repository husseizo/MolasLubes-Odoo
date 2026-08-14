namespace MolasLubes.Application.Orders;

public class CreateSalesOrderDto
{
    // 🔑 REQUIRED
    public string CustomerCode { get; set; } = null!;

    // 🔁 IDEMPOTENCY (OPTIONAL but recommended)
    public string? ExternalOrderId { get; set; }

    // 📦 ORDER META
    public DateTime? DeliveryDate { get; set; }
    public string? Currency { get; set; }

    // 🧾 SAP CONFIG
    public int? Series { get; set; }
    public int? BranchId { get; set; }
    public int? SalesPersonCode { get; set; }

    // 📦 LINES
    public List<CreateSalesOrderLineDto> Lines { get; set; } = new();
}

public class CreateSalesOrderLineDto
{
    // 🔑 REQUIRED
    public string ItemCode { get; set; } = null!;

    public decimal Quantity { get; set; }

    // 💰 OPTIONAL (pricing override)
    public decimal? Price { get; set; }

    public string? ExternalLineId { get; set; }

    // 🏬 OPTIONAL
    public string? VatGroup { get; set; }
    public string? WarehouseCode { get; set; }

    // 🏷️ OPTIONAL — used to build Dscription prefix "ItemName/Manufacturer/..."
    // Supply these when creating via API so the description is formatted
    // in the same Add() transaction (no second Update() needed).
    // If omitted, SAP's auto-populated ItemDescription is used as-is.
    public string? ItemName     { get; set; }
    public string? Manufacturer { get; set; }
}