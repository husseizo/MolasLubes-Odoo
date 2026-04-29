namespace MolasLubes.Application.LiquiMolyTransfers;

// ── Dry-run ──────────────────────────────────────────────

public class LiquiMolyTransferDryRunResult
{
    public bool   CanApply  { get; init; }
    public string? Error    { get; init; }

    public List<TransferLinePreflightRow> Lines { get; init; } = new();
    public Dictionary<string, int> Totals       { get; init; } = new();
}

public class TransferLinePreflightRow
{
    public string  SourceItemCode { get; init; } = string.Empty;
    public string? ArticleNumber  { get; init; }
    public string? SourceItemName { get; init; }
    public string? TargetItemCode { get; init; }
    public string? TargetItemName { get; init; }
    public decimal RequestedQty   { get; init; }
    public decimal AvailableQty   { get; init; }
    public string  Outcome        { get; init; } = string.Empty;
    public string? Message        { get; init; }
}

// ── Apply ────────────────────────────────────────────────

public class LiquiMolyTransferApplyResult
{
    public string  TransferRef           { get; init; } = string.Empty;
    public string  Status                { get; init; } = string.Empty;
    public string  ExecutionMode         { get; init; } = "TRANSFER";

    // TRANSFER flow (GI → GR)
    public int?    GoodsIssueDocEntry    { get; init; }
    public string? GoodsIssueDocNum      { get; init; }
    public int?    GoodsReceiptDocEntry  { get; init; }
    public string? GoodsReceiptDocNum    { get; init; }

    // SALES_PURCHASE flow (SO → PO → GR PO)
    public int?    SalesOrderDocEntry    { get; init; }
    public int?    SalesOrderDocNum      { get; init; }
    public int?    PurchaseOrderDocEntry { get; init; }
    public int?    PurchaseOrderDocNum   { get; init; }

    public string? ErrorMessage          { get; init; }
    public List<TransferLinePreflightRow> Lines { get; init; } = new();
}

// ── Line outcomes ────────────────────────────────────────

public static class TransferLineOutcome
{
    public const string OK              = "OK";
    public const string NOT_FOUND       = "NOT_FOUND";
    public const string NOT_LIQUI_MOLY  = "NOT_LIQUI_MOLY";
    public const string FROZEN          = "FROZEN";
    public const string NO_ARTICLE_NUM  = "NO_ARTICLE_NUM";
    public const string TARGET_NOT_FOUND = "TARGET_NOT_FOUND";
    public const string INSUFFICIENT_STOCK = "INSUFFICIENT_STOCK";
    public const string INVALID_QTY     = "INVALID_QTY";
}
