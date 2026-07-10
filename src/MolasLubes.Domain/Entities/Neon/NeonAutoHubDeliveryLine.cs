namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubDeliveryLine
{
    public long    Id          { get; set; }
    public int     DocEntry    { get; set; }
    public int     LineNum     { get; set; }
    public string  ItemCode    { get; set; } = null!;
    public string  Description { get; set; } = null!;
    public decimal Quantity    { get; set; }
    public decimal Price       { get; set; }
    public decimal LineTotal   { get; set; }
    public string? WhsCode     { get; set; }
    public NeonAutoHubDelivery? Delivery { get; set; }
}
