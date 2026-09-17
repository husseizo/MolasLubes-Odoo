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

}