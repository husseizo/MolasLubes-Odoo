namespace MolasLubes.Application.LiquiMolyReplenishment;

// ── Draft line apply ───────────────────────────────────────────────────

/// <summary>Request body for POST /{ref}/draft-lines/apply</summary>
public class DraftLineApplyRequest
{
    public SapActorContext Actor           { get; set; } = null!;
    public string          ExpectedStatus  { get; set; } = "DRAFT";
    public int             ExpectedVersion { get; set; }
    public List<DraftLineOperation> Operations { get; set; } = new();
}

public class DraftLineOperation
{
    /// <summary>"SET_QTY", "DELETE_LINE", or "ADD_LINE"</summary>
    public string  Op          { get; set; } = null!;
    public int     LineId      { get; set; }   // required for SET_QTY / DELETE_LINE
    public decimal? ApprovedQty { get; set; }  // required for SET_QTY / ADD_LINE

    // ADD_LINE payload
    public string? SourceItemCode { get; set; }
    public string? TargetItemCode { get; set; }
    public string? ArticleNumber  { get; set; }
    public string? ItemName       { get; set; }
}

public class DraftLineApplyResponse
{
    public string  RequestRef { get; set; } = null!;
    public string  Status     { get; set; } = null!;
    public int     Version    { get; set; }
    public int     LineCount  { get; set; }
    public DraftLineTotals Totals { get; set; } = null!;
    public List<DraftLineRow> Lines { get; set; } = new();
}

public class DraftLineTotals
{
    public decimal SuggestedQty { get; set; }
    public decimal ApprovedQty  { get; set; }
}

public class DraftLineRow
{
    public int     Id            { get; set; }
    public string  ArticleNumber { get; set; } = null!;
    public string? ItemName      { get; set; }
    public decimal SuggestedQty  { get; set; }
    public decimal? ApprovedQty  { get; set; }
    public string? TrendCategory { get; set; }
    public int     Priority      { get; set; }
}

// ── Warehouse options ─────────────────────────────────────────────────

/// <summary>Response for GET /warehouse-options</summary>
public class WarehouseOptionsResponse
{
    public string SourceProfile { get; set; } = null!;
    public string TargetProfile { get; set; } = null!;
    public List<WarehouseOption> SourceWarehouses { get; set; } = new();
    public List<WarehouseOption> TargetWarehouses { get; set; } = new();
}

public class WarehouseOption
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
}
