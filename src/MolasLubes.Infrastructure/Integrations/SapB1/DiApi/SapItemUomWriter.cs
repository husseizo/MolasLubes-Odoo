using Microsoft.Extensions.Logging;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Low-level SAP DI API writer for setting the Inventory Counting UoM on items.
///
/// Intentionally narrow: it only touches InventoryCountingUoMEntry.
/// It never modifies UoMGroupEntry or any other item master field.
/// Group repair must be handled as a separate controlled process.
/// </summary>
public class SapItemUomWriter
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapItemUomWriter> _logger;

    public SapItemUomWriter(
        SapDiApiConnection connection,
        ILogger<SapItemUomWriter> logger)
    {
        _connection = connection;
        _logger     = logger;
    }

    // =====================================================
    // UOM RESOLUTION  (call once per operation, not per item)
    // =====================================================

    /// <summary>
    /// Resolves a UoM code or name to its internal UomEntry integer via the OUOM table.
    /// Returns null if the unit is not found.
    /// </summary>
    public int? ResolveUomEntry(string uomCode)
    {
        if (string.IsNullOrWhiteSpace(uomCode))
            throw new ArgumentException("uomCode is required");

        var company = _connection.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
        var safe = uomCode.Trim().Replace("'", "''");

        rs.DoQuery($@"
SELECT TOP 1 UomEntry
FROM OUOM
WHERE UomCode = '{safe}' OR UomName = '{safe}'
");

        if (rs.EoF)
        {
            _logger.LogWarning("SapItemUomWriter: UoM not found in OUOM | Code={Code}", uomCode);
            return null;
        }

        return Convert.ToInt32((object)rs.Fields.Item("UomEntry").Value);
    }

    // =====================================================
    // PREFLIGHT  (classify one item, no writes)
    // =====================================================

    /// <summary>
    /// Classifies a single item without making any changes.
    /// </summary>
    public ItemUomPreflightResult Preflight(string itemCode, int targetUomEntry)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            throw new ArgumentException("itemCode is required");

        var company = _connection.GetConnectedCompany();
        var items   = (Items)company.GetBusinessObject(BoObjectTypes.oItems);

        if (!items.GetByKey(itemCode))
            return new ItemUomPreflightResult(itemCode, ItemUomOutcome.NOT_FOUND, null, null);

        var currentUomEntry = items.InventoryCountingUoMEntry;
        var groupEntry      = items.UoMGroupEntry;

        if (currentUomEntry == targetUomEntry)
            return new ItemUomPreflightResult(itemCode, ItemUomOutcome.SKIP_ALREADY_SET,
                currentUomEntry, groupEntry);

        // A group entry of -1 means the item uses the "manual" UoM mode (no group assigned).
        // We cannot set a group-bound counting UoM in that state.
        if (groupEntry <= 0)
            return new ItemUomPreflightResult(itemCode, ItemUomOutcome.FAIL_INVALID_UOM_GROUP,
                currentUomEntry, groupEntry);

        // Verify the target UoM actually exists inside the item's UoM group (UGP1).
        if (!UomExistsInGroup(company, groupEntry, targetUomEntry))
            return new ItemUomPreflightResult(itemCode, ItemUomOutcome.FAIL_TARGET_UOM_MISSING,
                currentUomEntry, groupEntry);

        return new ItemUomPreflightResult(itemCode, ItemUomOutcome.OK_TO_UPDATE,
            currentUomEntry, groupEntry);
    }

    // =====================================================
    // APPLY  (write — only call after Preflight=OK_TO_UPDATE)
    // =====================================================

    /// <summary>
    /// Sets InventoryCountingUoMEntry on the item and calls Update().
    /// Returns UPDATED on success, FAIL_SAP_ERROR on SAP rejection.
    /// </summary>
    public ItemUomApplyResult Apply(string itemCode, int targetUomEntry)
    {
        var company = _connection.GetConnectedCompany();
        var items   = (Items)company.GetBusinessObject(BoObjectTypes.oItems);

        if (!items.GetByKey(itemCode))
            return new ItemUomApplyResult(itemCode, ItemUomOutcome.NOT_FOUND, null, null);

        items.InventoryCountingUoMEntry = targetUomEntry;

        int rc = items.Update();
        if (rc != 0)
        {
            company.GetLastError(out int code, out string msg);
            _logger.LogWarning(
                "SapItemUomWriter: Update failed | ItemCode={Code} | SapCode={SapCode} | SapMsg={SapMsg}",
                itemCode, code, msg);
            return new ItemUomApplyResult(itemCode, ItemUomOutcome.FAIL_SAP_ERROR, code, msg);
        }

        _logger.LogInformation(
            "SapItemUomWriter: InventoryCountingUoMEntry set | ItemCode={Code} | UomEntry={Entry}",
            itemCode, targetUomEntry);

        return new ItemUomApplyResult(itemCode, ItemUomOutcome.UPDATED, null, null);
    }

    // ── Private helpers ──────────────────────────────────

    private static bool UomExistsInGroup(Company company, int groupEntry, int uomEntry)
    {
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
        rs.DoQuery($@"
SELECT TOP 1 UomEntry
FROM UGP1
WHERE AbsEntry = {groupEntry}
  AND UomEntry = {uomEntry}
");
        return !rs.EoF;
    }
}

// ── Result types ─────────────────────────────────────────

public enum ItemUomOutcome
{
    OK_TO_UPDATE,
    SKIP_ALREADY_SET,
    NOT_FOUND,
    FAIL_INVALID_UOM_GROUP,
    FAIL_TARGET_UOM_MISSING,
    UPDATED,
    FAIL_SAP_ERROR
}

public record ItemUomPreflightResult(
    string ItemCode,
    ItemUomOutcome Outcome,
    int? CurrentUomEntry,
    int? GroupEntry);

public record ItemUomApplyResult(
    string ItemCode,
    ItemUomOutcome Outcome,
    int? SapErrorCode,
    string? SapErrorMessage);
