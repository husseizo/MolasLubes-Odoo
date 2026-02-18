namespace MolasLubes.Application.Orders;

public class CommitSalesOrderRequest
{
    public string CardCode { get; set; } = null!;
    public List<long> ReservationIds { get; set; } = new();

    /// <summary>
    /// Odoo Sales Order ID (e.g. "S00042"). When provided, takes priority over
    /// any OdooSalesOrderId stored on the individual reservations.
    /// </summary>
    public string? ExternalOrderId { get; set; }
}