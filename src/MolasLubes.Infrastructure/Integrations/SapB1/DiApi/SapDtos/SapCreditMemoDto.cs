namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapCreditMemoDto
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }

    public string CardCode { get; set; } = null!;
    public string CardName { get; set; } = null!;
    public DateTime DocDate { get; set; }

    public decimal DocTotal { get; set; }
    public decimal VatSum { get; set; }

    // 🔗 ODOO header UDFs (ORIN)
    public string? OdooCreditId { get; set; }
    public string? OdooStatus { get; set; }
    public string? OdooSyncDir { get; set; }
    public string? OdooErrorMsg { get; set; }
    public DateTime? OdooLastSync { get; set; }

    public List<SapCreditMemoLineDto> Lines { get; set; } = new();
}

public class SapCreditMemoLineDto
{
    public string ItemCode { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }

    // Base links if any (e.g. based on invoice/delivery)
    public int BaseType { get; set; }
    public int BaseEntry { get; set; }
    public int BaseLine { get; set; }
}