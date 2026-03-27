namespace MolasLubes.Domain.Entities.Cache;

/// <summary>
/// Audit header for a Liqui Moly replenishment request.
///
/// Status flow:
///   DRAFT → PENDING_APPROVAL → APPROVED → EXECUTING → EXECUTED / PARTIAL / FAILED
///   DRAFT → PENDING_APPROVAL → REJECTED
/// </summary>
public class CacheLiquiMolyReplenishmentRequest
{
    public int    Id         { get; set; }
    public string RequestRef { get; set; } = null!;  // RPL-YYYYMMDD-NNNNNN

    // Direction: MolasLubes (supplier) → AutoHub (consumer)
    public string SourceProfile   { get; set; } = "MolasLubes";
    public string TargetProfile   { get; set; } = "AutoHub";
    public string SourceWarehouse { get; set; } = null!;
    public string TargetWarehouse { get; set; } = null!;

    // Workflow status
    public string Status { get; set; } = "DRAFT";

    // Actor trail
    public string? RequestedBySapUser { get; set; }
    public string? ApprovedBySapUser  { get; set; }
    public string? RejectedBySapUser  { get; set; }
    public string? ExecutedBySapUser  { get; set; }

    // Timestamps
    public DateTime  CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt  { get; set; }
    public DateTime? RejectedAt  { get; set; }
    public DateTime? ExecutedAt  { get; set; }

    // Comments / notes
    public string? Comments         { get; set; }
    public string? RejectionReason  { get; set; }

    // Transfer execution references (populated after EXECUTING → EXECUTED)
    public string? TransferRef          { get; set; }
    public int?    GoodsIssueDocEntry   { get; set; }
    public string? GoodsIssueDocNum     { get; set; }
    public int?    GoodsReceiptDocEntry { get; set; }
    public string? GoodsReceiptDocNum   { get; set; }
    public string? ErrorMessage         { get; set; }

    public List<CacheLiquiMolyReplenishmentRequestLine> Lines { get; set; } = new();
}
