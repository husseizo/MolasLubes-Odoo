using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Queries OITM (joined to OITB for group name) to select candidate ItemCodes
/// for bulk operations.  Returns only the codes; classification and updates are
/// handled downstream by SapItemUomWriter / InventoryCountingUomBackfillService.
/// </summary>
[SupportedOSPlatform("windows")]
public class SapItemSelector
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapItemSelector> _logger;

    public SapItemSelector(
        SapDiApiConnection connection,
        ILogger<SapItemSelector> logger)
    {
        _connection = connection;
        _logger     = logger;
    }

    /// <summary>
    /// Returns ItemCodes matching the supplied filter, plus a <c>HasMore</c> flag.
    /// Uses a take+1 probe: fetches one extra row to detect whether additional pages
    /// exist without a separate COUNT query, then trims the result to <paramref name="take"/>.
    /// Throws <see cref="InvalidOperationException"/> when no narrowing filter is
    /// supplied and <paramref name="confirmAll"/> is false.
    /// </summary>
    public SapItemSelectorResult SelectItemCodes(
        bool activeOnly,
        IReadOnlyList<string>? itemGroupNames,
        IReadOnlyList<string>? itemCodes,
        int take,
        int skip,
        bool confirmAll)
    {
        var hasGroupFilter = itemGroupNames is { Count: > 0 };
        var hasCodeFilter  = itemCodes      is { Count: > 0 };

        if (!hasGroupFilter && !hasCodeFilter && !confirmAll)
            throw new InvalidOperationException(
                "Bulk operation requires at least one narrowing filter (itemGroupNames or itemCodes). " +
                "Set confirmAll=true to operate on the entire active item master.");

        // Fetch take+1 rows: if we get more than take we know there is another page.
        var sql = BuildQuery(activeOnly, itemGroupNames, itemCodes, take + 1, skip);

        _logger.LogInformation(
            "SapItemSelector: querying candidates | ActiveOnly={Active} | Groups={Groups} | CodeFilter={Codes} | Take={Take} | Skip={Skip}",
            activeOnly,
            hasGroupFilter ? string.Join(",", itemGroupNames!) : "(none)",
            hasCodeFilter  ? $"{itemCodes!.Count} codes"        : "(none)",
            take, skip);

        var company = _connection.GetConnectedCompany();
        Recordset? rs = null;

        try
        {
            rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
            rs.DoQuery(sql);

            var results = new List<string>();
            while (!rs.EoF)
            {
                var code = rs.Fields.Item("ItemCode").Value?.ToString();
                if (!string.IsNullOrWhiteSpace(code))
                    results.Add(code);
                rs.MoveNext();
            }

            // Trim the probe row and set the flag
            var hasMore = results.Count > take;
            if (hasMore) results.RemoveAt(results.Count - 1);

            _logger.LogInformation(
                "SapItemSelector: matched {Count} item(s) | HasMore={HasMore}",
                results.Count, hasMore);

            return new SapItemSelectorResult(results.AsReadOnly(), hasMore);
        }
        finally
        {
            if (rs != null) Marshal.ReleaseComObject(rs);
        }
    }

    // ── Query builder ────────────────────────────────────

    private static string BuildQuery(
        bool activeOnly,
        IReadOnlyList<string>? itemGroupNames,
        IReadOnlyList<string>? itemCodes,
        int take,
        int skip)
    {
        var where = new List<string>();

        if (activeOnly)
            where.Add("T0.frozenFor = 'N'");

        if (itemGroupNames is { Count: > 0 })
        {
            var inList = string.Join(",",
                itemGroupNames.Select(g => $"'{g.Replace("'", "''")}'"));
            where.Add($"T1.ItmsGrpNam IN ({inList})");
        }

        if (itemCodes is { Count: > 0 })
        {
            var inList = string.Join(",",
                itemCodes.Select(c => $"'{c.Replace("'", "''")}'"));
            where.Add($"T0.ItemCode IN ({inList})");
        }

        var sb = new StringBuilder();
        sb.AppendLine("SELECT T0.ItemCode");
        sb.AppendLine("FROM OITM T0");
        sb.AppendLine("LEFT JOIN OITB T1 ON T1.ItmsGrpCod = T0.ItmsGrpCod");

        if (where.Count > 0)
        {
            sb.AppendLine("WHERE");
            sb.AppendLine(string.Join(" AND ", where.Select(c => $"  ({c})")));
        }

        sb.AppendLine("ORDER BY T0.ItemCode");
        sb.AppendLine($"OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY");

        return sb.ToString();
    }
}

public record SapItemSelectorResult(
    IReadOnlyList<string> ItemCodes,
    bool HasMore);
