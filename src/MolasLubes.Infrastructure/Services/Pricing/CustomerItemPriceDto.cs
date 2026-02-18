namespace MolasLubes.Infrastructure.Services.Pricing;

public class CustomerItemPriceDto
{
    public string CardCode { get; set; } = null!;
    public string ItemCode { get; set; } = null!;
    public int PriceList { get; set; }
    public decimal Price { get; set; }
    public string Source { get; set; } = "SAP"; // SAP or CACHE
}