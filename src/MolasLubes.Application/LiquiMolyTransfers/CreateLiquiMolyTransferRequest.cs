namespace MolasLubes.Application.LiquiMolyTransfers;

public class CreateLiquiMolyTransferRequest
{
    /// <summary>SAP profile key for the source company (default: "AutoHub").</summary>
    public string SourceProfile { get; set; } = "AutoHub";

    /// <summary>SAP profile key for the target company (default: "MolasLubes").</summary>
    public string TargetProfile { get; set; } = "MolasLubes";

    public string SourceWarehouse { get; set; } = null!;
    public string TargetWarehouse { get; set; } = null!;

    public string? Comments { get; set; }

    public List<TransferLineRequest> Lines { get; set; } = new();
}

public class TransferLineRequest
{
    /// <summary>ItemCode in the source SAP company.</summary>
    public string  SourceItemCode { get; set; } = null!;
    public decimal Quantity       { get; set; }
}
