using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Low-level SAP DI API writer for setting the default inventory counting UoM on items.
///
/// Intentionally narrow: it only touches DefaultCountingUoMEntry.
/// It never modifies UoMGroupEntry or any other item master field.
/// Group repair must be handled as a separate controlled process.
///
/// Every COM business object created here is released in a finally block so that
/// large batch operations (hundreds of items) do not accumulate COM objects on
/// the DI API session.
/// </summary>
[SupportedOSPlatform("windows")]
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
        Recordset? rs = null;

        try
        {
            rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
            var safe = uomCode.Trim().Replace("'", "''");

            rs.DoQuery($@"
SELECT TOP 1 UomEntry
FROM OUOM
WHERE UomCode = '{safe}' OR UomName = '{safe}'
");

            if (rs.EoF)
            {
                _logger.LogWarning(
                    "SapItemUomWriter: UoM not found in OUOM | Code={Code}", uomCode);
                return null;
            }

            return Convert.ToInt32((object)rs.Fields.Item("UomEntry").Value);
        }
        finally
        {
            if (rs != null) Marshal.ReleaseComObject(rs);
        }
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
        Items? items = null;

        try
        {
            items = (Items)company.GetBusinessObject(BoObjectTypes.oItems);

            if (!items.GetByKey(itemCode))
                return new ItemUomPreflightResult(itemCode, ItemUomOutcome.NOT_FOUND, null, null);

            var currentUomEntry = items.DefaultCountingUoMEntry;
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
            // UGP1 is keyed by UgpEntry (FK → OUGP.UgpEntry) — not AbsEntry.
            if (!UomExistsInGroup(company, groupEntry, targetUomEntry))
                return new ItemUomPreflightResult(itemCode, ItemUomOutcome.FAIL_TARGET_UOM_MISSING,
                    currentUomEntry, groupEntry);

            var bindingSnapshot = ReadBindingSnapshot(company, itemCode);
            if (HasBrokenHistoricalBindings(bindingSnapshot))
                return new ItemUomPreflightResult(itemCode, ItemUomOutcome.FAIL_BROKEN_UOM_BINDINGS,
                    currentUomEntry, groupEntry);

            return new ItemUomPreflightResult(itemCode, ItemUomOutcome.OK_TO_UPDATE,
                currentUomEntry, groupEntry);
        }
        finally
        {
            if (items != null) Marshal.ReleaseComObject(items);
        }
    }

    // =====================================================
    // APPLY  (write — only call after Preflight=OK_TO_UPDATE)
    // =====================================================

    /// <summary>
    /// Sets DefaultCountingUoMEntry on the item and calls Update().
    /// Returns UPDATED on success, FAIL_SAP_ERROR on SAP rejection.
    /// </summary>
    public ItemUomApplyResult Apply(string itemCode, int targetUomEntry)
    {
        var company = _connection.GetConnectedCompany();
        var targetUom = ReadUomIdentity(company, targetUomEntry);
        var bindingSnapshot = ReadBindingSnapshot(company, itemCode);

        var directAttempt = TryApply(itemCode, targetUomEntry);
        if (directAttempt.Outcome == ItemUomOutcome.UPDATED)
            return directAttempt;

        if (!ShouldRetryWithNonInventoryRepair(directAttempt, bindingSnapshot))
            return directAttempt;

        _logger.LogInformation(
            "SapItemUomWriter: retrying counting UoM update with non-inventory binding repair | ItemCode={Code} | TargetUomEntry={Entry}",
            itemCode,
            targetUomEntry);

        return TryApply(itemCode, targetUomEntry, items =>
        {
            RepairMatchingNonInventoryUomEntries(items, bindingSnapshot, targetUomEntry, targetUom);
        });
    }

    /// <summary>
    /// Reads a small SAP item-master snapshot to help investigate update failures.
    /// </summary>
    public SapItemUomDiagnostics? ReadDiagnostics(string itemCode)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            throw new ArgumentException("itemCode is required");

        var company = _connection.GetConnectedCompany();
        Recordset? rs = null;

        try
        {
            rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
            var safe = itemCode.Trim().Replace("'", "''");

            rs.DoQuery($@"
SELECT TOP 1
    ItemCode,
    InvntryUom,
    SalUnitMsr,
    BuyUnitMsr,
    IUoMEntry,
    SUoMEntry,
    PUoMEntry,
    INUoMEntry,
    UgpEntry,
    EvalSystem,
    ManBtchNum,
    ManSerNum
FROM OITM
WHERE ItemCode = '{safe}'
");

            if (rs.EoF)
                return null;

            return new SapItemUomDiagnostics(
                ItemCode: rs.Fields.Item("ItemCode").Value?.ToString() ?? itemCode,
                InventoryUom: rs.Fields.Item("InvntryUom").Value?.ToString(),
                SalesUom: rs.Fields.Item("SalUnitMsr").Value?.ToString(),
                PurchaseUom: rs.Fields.Item("BuyUnitMsr").Value?.ToString(),
                InventoryUomEntry: TryGetInt(rs.Fields.Item("IUoMEntry").Value),
                SalesUomEntry: TryGetInt(rs.Fields.Item("SUoMEntry").Value),
                PurchaseUomEntry: TryGetInt(rs.Fields.Item("PUoMEntry").Value),
                CountingUomEntry: TryGetInt(rs.Fields.Item("INUoMEntry").Value),
                UomGroupEntry: TryGetInt(rs.Fields.Item("UgpEntry").Value),
                ValMethod: rs.Fields.Item("EvalSystem").Value?.ToString(),
                ManageBatchNumbers: IsYes(rs.Fields.Item("ManBtchNum").Value),
                ManageSerialNumbers: IsYes(rs.Fields.Item("ManSerNum").Value));
        }
        finally
        {
            if (rs != null) Marshal.ReleaseComObject(rs);
        }
    }

    // ── Private helpers ──────────────────────────────────

    private static bool UomExistsInGroup(Company company, int groupEntry, int uomEntry)
    {
        Recordset? rs = null;

        try
        {
            rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

            // UGP1 foreign-key back to the UoM group is UgpEntry, not AbsEntry.
            // OUGP primary key is also UgpEntry; Items.UoMGroupEntry maps to OITM.UgpEntry.
            rs.DoQuery($@"
SELECT TOP 1 UomEntry
FROM UGP1
WHERE UgpEntry = {groupEntry}
  AND UomEntry = {uomEntry}
");
            return !rs.EoF;
        }
        finally
        {
            if (rs != null) Marshal.ReleaseComObject(rs);
        }
    }

    private ItemUomApplyResult TryApply(
        string itemCode,
        int targetUomEntry,
        Action<Items>? beforeUpdate = null)
    {
        var company = _connection.GetConnectedCompany();
        Items? items = null;

        try
        {
            items = (Items)company.GetBusinessObject(BoObjectTypes.oItems);

            if (!items.GetByKey(itemCode))
                return new ItemUomApplyResult(itemCode, ItemUomOutcome.NOT_FOUND, null, null);

            beforeUpdate?.Invoke(items);
            items.DefaultCountingUoMEntry = targetUomEntry;

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
                "SapItemUomWriter: DefaultCountingUoMEntry set | ItemCode={Code} | UomEntry={Entry}",
                itemCode, targetUomEntry);

            return new ItemUomApplyResult(itemCode, ItemUomOutcome.UPDATED, null, null);
        }
        finally
        {
            if (items != null) Marshal.ReleaseComObject(items);
        }
    }

    private void RepairMatchingNonInventoryUomEntries(
        Items items,
        ItemUomBindingSnapshot? snapshot,
        int targetUomEntry,
        UomIdentity? targetUom)
    {
        if (snapshot == null || targetUom == null)
            return;

        if (ShouldRepair(snapshot.SalesUom, snapshot.SalesUomEntry, targetUom))
        {
            items.DefaultSalesUoMEntry = targetUomEntry;
            _logger.LogInformation(
                "SapItemUomWriter: repaired DefaultSalesUoMEntry before counting UoM update | ItemCode={Code} | UomEntry={Entry}",
                snapshot.ItemCode,
                targetUomEntry);
        }

        if (ShouldRepair(snapshot.PurchaseUom, snapshot.PurchaseUomEntry, targetUom))
        {
            items.DefaultPurchasingUoMEntry = targetUomEntry;
            _logger.LogInformation(
                "SapItemUomWriter: repaired DefaultPurchasingUoMEntry before counting UoM update | ItemCode={Code} | UomEntry={Entry}",
                snapshot.ItemCode,
                targetUomEntry);
        }
    }

    private static bool ShouldRetryWithNonInventoryRepair(
        ItemUomApplyResult attempt,
        ItemUomBindingSnapshot? snapshot)
    {
        if (attempt.Outcome != ItemUomOutcome.FAIL_SAP_ERROR || snapshot == null)
            return false;

        if (attempt.SapErrorCode == -1029 &&
            attempt.SapErrorMessage?.Contains("cannot change inventory UoM", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        return snapshot.SalesUomEntry.GetValueOrDefault() <= 0 ||
               snapshot.PurchaseUomEntry.GetValueOrDefault() <= 0;
    }

    private static bool ShouldRepair(string? uomText, int? entry, UomIdentity targetUom)
    {
        if (entry.HasValue && entry.Value > 0)
            return false;

        if (string.IsNullOrWhiteSpace(uomText))
            return false;

        return string.Equals(uomText.Trim(), targetUom.UomCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uomText.Trim(), targetUom.UomName, StringComparison.OrdinalIgnoreCase);
    }

    private static UomIdentity? ReadUomIdentity(Company company, int uomEntry)
    {
        Recordset? rs = null;

        try
        {
            rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
            rs.DoQuery($@"
SELECT TOP 1 UomEntry, UomCode, UomName
FROM OUOM
WHERE UomEntry = {uomEntry}
");

            if (rs.EoF)
                return null;

            return new UomIdentity(
                TryGetInt(rs.Fields.Item("UomEntry").Value) ?? uomEntry,
                rs.Fields.Item("UomCode").Value?.ToString(),
                rs.Fields.Item("UomName").Value?.ToString());
        }
        finally
        {
            if (rs != null) Marshal.ReleaseComObject(rs);
        }
    }

    private static ItemUomBindingSnapshot? ReadBindingSnapshot(Company company, string itemCode)
    {
        Recordset? rs = null;

        try
        {
            rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
            var safe = itemCode.Trim().Replace("'", "''");

            rs.DoQuery($@"
SELECT TOP 1
    ItemCode,
    InvntryUom,
    SalUnitMsr,
    BuyUnitMsr,
    CntUnitMsr,
    IUoMEntry,
    SUoMEntry,
    PUoMEntry,
    INUoMEntry
FROM OITM
WHERE ItemCode = '{safe}'
");

            if (rs.EoF)
                return null;

            return new ItemUomBindingSnapshot(
                ItemCode: rs.Fields.Item("ItemCode").Value?.ToString() ?? itemCode,
                InventoryUom: rs.Fields.Item("InvntryUom").Value?.ToString(),
                SalesUom: rs.Fields.Item("SalUnitMsr").Value?.ToString(),
                PurchaseUom: rs.Fields.Item("BuyUnitMsr").Value?.ToString(),
                CountingUom: rs.Fields.Item("CntUnitMsr").Value?.ToString(),
                InventoryUomEntry: TryGetInt(rs.Fields.Item("IUoMEntry").Value),
                SalesUomEntry: TryGetInt(rs.Fields.Item("SUoMEntry").Value),
                PurchaseUomEntry: TryGetInt(rs.Fields.Item("PUoMEntry").Value),
                CountingUomEntry: TryGetInt(rs.Fields.Item("INUoMEntry").Value));
        }
        finally
        {
            if (rs != null) Marshal.ReleaseComObject(rs);
        }
    }

    private static bool HasBrokenHistoricalBindings(ItemUomBindingSnapshot? snapshot)
    {
        if (snapshot == null)
            return false;

        return HasBrokenBinding(snapshot.InventoryUom, snapshot.InventoryUomEntry) ||
               HasBrokenBinding(snapshot.SalesUom, snapshot.SalesUomEntry) ||
               HasBrokenBinding(snapshot.PurchaseUom, snapshot.PurchaseUomEntry) ||
               HasBrokenBinding(snapshot.CountingUom, snapshot.CountingUomEntry);
    }

    private static bool HasBrokenBinding(string? uomText, int? uomEntry)
    {
        if (string.IsNullOrWhiteSpace(uomText))
            return false;

        return uomEntry.GetValueOrDefault() <= 0;
    }

    private static int? TryGetInt(object? value)
    {
        if (value == null) return null;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? IsYes(object? value)
    {
        var raw = value?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return string.Equals(raw, "Y", StringComparison.OrdinalIgnoreCase);
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
    FAIL_BROKEN_UOM_BINDINGS,
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

public record SapItemUomDiagnostics(
    string ItemCode,
    string? InventoryUom,
    string? SalesUom,
    string? PurchaseUom,
    int? InventoryUomEntry,
    int? SalesUomEntry,
    int? PurchaseUomEntry,
    int? CountingUomEntry,
    int? UomGroupEntry,
    string? ValMethod,
    bool? ManageBatchNumbers,
    bool? ManageSerialNumbers);

internal record UomIdentity(
    int UomEntry,
    string? UomCode,
    string? UomName);

internal record ItemUomBindingSnapshot(
    string ItemCode,
    string? InventoryUom,
    string? SalesUom,
    string? PurchaseUom,
    string? CountingUom,
    int? InventoryUomEntry,
    int? SalesUomEntry,
    int? PurchaseUomEntry,
    int? CountingUomEntry);
