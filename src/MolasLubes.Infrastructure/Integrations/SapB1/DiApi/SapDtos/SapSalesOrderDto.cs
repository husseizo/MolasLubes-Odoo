namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapSalesOrderDto
{
    // HEADER
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
    public string CardCode { get; set; } = null!;
    public string CardName { get; set; } = null!;
    public DateTime DocDate { get; set; }
    public decimal DocTotal { get; set; }
    public string DocStatus { get; set; } = null!;

    public DateTime UpdateDate { get; set; }

    // 🔗 ODOO HEADER
    public string? OdooSalesOrderId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }

    public List<SapSalesOrderLineDto> Lines { get; set; } = new();
}