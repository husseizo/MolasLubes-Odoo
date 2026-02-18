namespace MolasLubes.Application.Customers;

public class UpsertCustomerDto
{
    /// <summary>SAP CardCode. For POST you can optionally supply it; otherwise SAP can generate if your setup allows.</summary>
    public string? CardCode { get; set; }

    public string CardName { get; set; } = default!;

    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }

    public int? PriceList { get; set; }
    public int? SlpCode { get; set; }

    // Odoo mapping (UDF)
    public string? OdooCustomerId { get; set; }

    // Addresses (simple)
    public CustomerAddressDto? BillTo { get; set; }
    public CustomerAddressDto? ShipTo { get; set; }
}

public class CustomerAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; } // e.g. "TZ"
}