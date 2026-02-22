namespace MolasLubes.Application.Customers;

public class UpsertCustomerDto
{
    /// <summary>SAP CardCode. For POST you can optionally supply it; otherwise SAP generates from series.</summary>
    public string? CardCode { get; set; }

    public string CardName { get; set; } = default!;

    public string? Phone1 { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }

    public int? PriceList { get; set; }    // OCRD.ListNum — price list assigned to customer
    public int? SlpCode { get; set; }      // OCRD.SlpCode — sales person code

    /// <summary>SAP payment group/terms code (OCRD.GroupNum). e.g. 1 = Net 30.</summary>
    public int? GroupCode { get; set; }

    /// <summary>Credit limit override in company currency (OCRD.CreditLine).</summary>
    public decimal? CreditLine { get; set; }

    /// <summary>
    /// Active state. null = no change.
    /// false = freeze (Frozen=Y) — blocks new transactions.
    /// true  = unfreeze (Frozen=N).
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>Free-text remarks (OCRD.Notes).</summary>
    public string? Remarks { get; set; }

    // Odoo mapping (UDF)
    public string? OdooCustomerId { get; set; }

    // Addresses
    public CustomerAddressDto? BillTo { get; set; }
    public CustomerAddressDto? ShipTo { get; set; }
}

public class CustomerAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; } // e.g. "TZ"
}
