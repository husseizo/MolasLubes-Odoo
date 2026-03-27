namespace MolasLubes.Application.LiquiMolyReplenishment;

/// <summary>Request payload for POST /api/admin/liquimoly/replenishment/generate-draft</summary>
public class GenerateReplenishmentRequest
{
    public string SourceProfile   { get; set; } = "MolasLubes";
    public string TargetProfile   { get; set; } = "AutoHub";
    public string SourceWarehouse { get; set; } = null!;
    public string TargetWarehouse { get; set; } = null!;

    /// <summary>Target coverage days for the suggested quantity formula. Default 30.</summary>
    public int TargetDays { get; set; } = 30;

    public SapActorContext Actor { get; set; } = null!;
}

/// <summary>Request payload for POST /api/admin/liquimoly/replenishment/{ref}/submit</summary>
public class SubmitReplenishmentRequest
{
    public SapActorContext Actor { get; set; } = null!;
}

/// <summary>Request payload for POST /api/admin/liquimoly/replenishment/{ref}/approve</summary>
public class ApproveReplenishmentRequest
{
    public SapActorContext Actor { get; set; } = null!;

    /// <summary>
    /// Optional per-line quantity overrides. Lines not listed use their SuggestedQty.
    /// </summary>
    public List<LineQuantityOverride>? LineQuantities { get; set; }
}

/// <summary>
/// Supervisor-supplied quantity override for one replenishment line.
/// </summary>
public class LineQuantityOverride
{
    public int     LineId      { get; set; }
    public decimal ApprovedQty { get; set; }
}

/// <summary>Request payload for POST /api/admin/liquimoly/replenishment/{ref}/reject</summary>
public class RejectReplenishmentRequest
{
    public SapActorContext Actor  { get; set; } = null!;
    public string          Reason { get; set; } = null!;
}

/// <summary>Request payload for POST /api/admin/liquimoly/replenishment/{ref}/execute</summary>
public class ExecuteReplenishmentRequest
{
    public SapActorContext Actor { get; set; } = null!;
}
