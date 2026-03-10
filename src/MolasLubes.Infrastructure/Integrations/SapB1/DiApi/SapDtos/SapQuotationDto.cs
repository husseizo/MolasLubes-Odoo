namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapQuotationDto
{
    public int DocEntry { get; set; }
    public int DocNum { get; set; }
    public string CardCode { get; set; } = null!;
    public string CardName { get; set; } = null!;
    public DateTime DocDate { get; set; }
    public decimal DocTotal { get; set; }
    public string DocStatus { get; set; } = null!;
    public DateTime UpdateDate { get; set; }
}
