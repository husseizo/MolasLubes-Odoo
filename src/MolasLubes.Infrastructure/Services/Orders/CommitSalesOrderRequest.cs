namespace MolasLubes.Application.Orders;

public class CommitSalesOrderRequest
{
    public string CardCode { get; set; } = null!;
    public List<long> ReservationIds { get; set; } = new();
}