namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapCustomerPriceListDto
{
    public int PriceListId { get; set; }
    public string? PriceListName { get; set; }

    public int ListNum { get; set; }


    // 🔗 ODOO (OPLN)
    public string? OdooPricelistId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }
}

public class SapItemPriceDto
{
    public string ItemCode { get; set; } = null!;
    public int PriceList { get; set; }
    public decimal Price { get; set; }

    // 🔗 ODOO (ITM1)
    public string? OdooPriceItemId { get; set; }
}