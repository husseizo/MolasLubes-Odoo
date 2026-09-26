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

    /// <summary>OWTQ.DocEntry to link this transfer to an existing Transfer Request.</summary>
    public int? BaseRequestDocEntry { get; set; }

    /// <summary>Caller-supplied idempotency key (max 64 chars). A second call with the same key returns the existing transfer.</summary>
    public string? ClientReference { get; set; }

    /// <summary>SAP user code executing the transfer (stored in audit; does not change the SAP connection user).</summary>
    public string? ActorSapUserCode { get; set; }

    public List<TransferLineRequest> Lines { get; set; } = new();
}

public class TransferLineRequest
{
    /// <summary>ItemCode in the source SAP company.</summary>
    public string  SourceItemCode     { get; set; } = null!;
    public decimal Quantity           { get; set; }

    /// <summary>WTQ1.LineNum on the source OWTQ. Required when BaseRequestDocEntry is set.</summary>
    public int?    BaseRequestLineNum { get; set; }
}
