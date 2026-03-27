namespace MolasLubes.Application.LiquiMolyReplenishment;

/// <summary>
/// Represents the human or system actor driving a mutating workflow step.
/// Callers supply this in request payloads; the role service validates and authorizes.
/// </summary>
public class SapActorContext
{
    /// <summary>SAP user code (e.g. "manager1").</summary>
    public string SapUserCode { get; init; } = null!;

    /// <summary>Optional free-text comment recorded in the audit trail.</summary>
    public string? Comment    { get; init; }
}
