using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Builds and writes the formatted Sales Order line description
/// "U_ItemName/U_Manufacturer/OriginalDescription" using SAP DI API only.
///
/// Rule summary
/// ── Both UDFs present  : HOSE/VIKA/8K0121101M
/// ── Only ItemName      : HOSE/8K0121101M
/// ── Only Manufacturer  : VIKA/8K0121101M
/// ── Both blank         : leave unchanged
/// ── Already prefixed   : leave unchanged (idempotent)
///
/// Dependency: SapDiApiConnection (Singleton) — same pattern as every
/// other SAP service in this project.
/// </summary>
public class SapSalesOrderLineDescriptionUpdater
{
    /// <summary>
    /// SAP B1 column RDR1.Dscription.
    /// Base schema: 100 chars. Some installations extend to 254.
    /// Change this constant to match the actual column size in your system.
    /// </summary>
    private const int MaxDescriptionLength = 100;

    private readonly SapDiApiConnection                             _connection;
    private readonly ILogger<SapSalesOrderLineDescriptionUpdater>   _logger;

    public SapSalesOrderLineDescriptionUpdater(
        SapDiApiConnection connection,
        ILogger<SapSalesOrderLineDescriptionUpdater> logger)
    {
        _connection = connection;
        _logger     = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1.  SINGLE-ORDER UPDATE
    //     Call this after a new Sales Order is created, or when patching an
    //     existing open order.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the Sales Order by DocEntry, reads U_ItemName and U_Manufacturer
    /// from every RDR1 line, and updates Dscription where the prefix is absent.
    /// Calls order.Update() only when at least one line was actually changed.
    /// Skips the document silently if it is not Open.
    /// </summary>
    /// <returns>
    /// true  — update succeeded, or no changes were needed.
    /// false — SAP reported an error (full details are logged).
    /// </returns>
    public bool UpdateSalesOrderLineDescriptions(int docEntry)
    {
        _logger.LogInformation(
            "[SalesOrderDescription] Processing DocEntry {DocEntry}", docEntry);

        var company = _connection.GetConnectedCompany();

        Documents? order = null;
        try
        {
            order = (Documents)company.GetBusinessObject(BoObjectTypes.oOrders);

            if (!order.GetByKey(docEntry))
            {
                _logger.LogWarning(
                    "[SalesOrderDescription] DocEntry {DocEntry} not found in SAP", docEntry);
                return false;
            }

            if (order.DocumentStatus != BoStatus.bost_Open)
            {
                _logger.LogInformation(
                    "[SalesOrderDescription] DocEntry {DocEntry} is not Open — skipped", docEntry);
                return true;   // not an error condition
            }

            int docNum       = order.DocNum;
            int changedLines = 0;

            for (int i = 0; i < order.Lines.Count; i++)
            {
                order.Lines.SetCurrentLine(i);

                string? itemName     = ReadUdf(order.Lines.UserFields.Fields.Item("U_ItemName").Value);
                string? manufacturer = ReadUdf(order.Lines.UserFields.Fields.Item("U_Manufacturer").Value);
                string  currentDesc  = (order.Lines.ItemDescription ?? string.Empty).Trim();

                string prefix = BuildPrefix(itemName, manufacturer);

                if (string.IsNullOrEmpty(prefix))
                {
                    _logger.LogInformation(
                        "[SalesOrderDescription] Line {Line} has no ItemName or Manufacturer — skipped", i);
                    continue;
                }

                string prefixSlash = prefix + "/";

                if (currentDesc.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "[SalesOrderDescription] Line {Line} already formatted — skipped", i);
                    continue;
                }

                string newDesc = SafeTruncate(prefixSlash + currentDesc, MaxDescriptionLength);

                _logger.LogInformation(
                    "[SalesOrderDescription] Line {Line} updated: '{Old}' -> '{New}'",
                    i, Clip(currentDesc, 40), Clip(newDesc, 60));

                order.Lines.ItemDescription = newDesc;
                changedLines++;
            }

            if (changedLines == 0)
            {
                _logger.LogInformation(
                    "[SalesOrderDescription] DocEntry {DocEntry} — no lines changed", docEntry);
                return true;
            }

            int rc = order.Update();

            if (rc != 0)
            {
                company.GetLastError(out int errorCode, out string errorMessage);
                _logger.LogError(
                    "[SalesOrderDescription] Update failed | DocEntry={DocEntry} | DocNum={DocNum} | " +
                    "SapCode={Code} | SapMsg={Msg}",
                    docEntry, docNum, errorCode, errorMessage);
                return false;
            }

            _logger.LogInformation(
                "[SalesOrderDescription] Sales Order updated successfully | " +
                "DocEntry={DocEntry} | DocNum={DocNum} | ChangedLines={Changed}",
                docEntry, docNum, changedLines);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[SalesOrderDescription] Unexpected error | DocEntry={DocEntry}", docEntry);
            return false;
        }
        finally
        {
            if (order != null)
                Marshal.ReleaseComObject(order);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2.  BATCH — ALL OPEN ORDERS
    //     SQL (Recordset) is used READ-ONLY to list DocEntry values.
    //     Every document modification still goes through DI API.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches all Open Sales Orders (DocStatus = 'O') via Recordset,
    /// then calls UpdateSalesOrderLineDescriptions() for each one via DI API.
    /// </summary>
    public void UpdateAllOpenSalesOrderDescriptions()
    {
        _logger.LogInformation("[SalesOrderDescription] Batch: querying open Sales Orders");

        var company = _connection.GetConnectedCompany();

        Recordset? rs = null;
        try
        {
            rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
            rs.DoQuery("SELECT DocEntry FROM ORDR WHERE DocStatus = 'O' ORDER BY DocEntry");

            if (rs.EoF)
            {
                _logger.LogInformation(
                    "[SalesOrderDescription] Batch: no open Sales Orders found");
                return;
            }

            var docEntries = new List<int>();
            while (!rs.EoF)
            {
                docEntries.Add(Convert.ToInt32(rs.Fields.Item("DocEntry").Value));
                rs.MoveNext();
            }

            _logger.LogInformation(
                "[SalesOrderDescription] Batch: processing {Count} open Sales Orders",
                docEntries.Count);

            int successCount = 0, failCount = 0;

            foreach (int docEntry in docEntries)
            {
                bool ok = UpdateSalesOrderLineDescriptions(docEntry);
                if (ok) successCount++;
                else    failCount++;
            }

            _logger.LogInformation(
                "[SalesOrderDescription] Batch complete | Success={S} | Failed={F}",
                successCount, failCount);
        }
        finally
        {
            if (rs != null)
                Marshal.ReleaseComObject(rs);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3.  STATIC HELPERS
    //     Pure logic — no SAP dependency.
    //     Call BuildDescription() BEFORE order.Lines.Add() on new orders
    //     to avoid a second Update() transaction.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the "ItemName/Manufacturer" prefix.
    /// Blank values are omitted — no double-slash, no leading/trailing slash.
    /// Returns empty string when both are blank.
    ///
    /// Examples:
    ///   ("HOSE", "VIKA")    → "HOSE/VIKA"
    ///   ("HOSE", null)      → "HOSE"
    ///   (null,   "VIKA")    → "VIKA"
    ///   (null,   null)      → ""
    /// </summary>
    public static string BuildPrefix(string? itemName, string? manufacturer)
    {
        var parts = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(itemName))
            parts.Add(itemName.Trim());

        if (!string.IsNullOrWhiteSpace(manufacturer))
            parts.Add(manufacturer.Trim());

        return string.Join("/", parts);
    }

    /// <summary>
    /// Applies the prefix to currentDescription and returns the final string.
    /// Idempotent: if currentDescription already begins with the prefix, it is
    /// returned unchanged.  Truncates safely to MaxDescriptionLength.
    ///
    /// Use this BEFORE order.Lines.Add() on new orders (pre-Add approach)
    /// so that the correct Dscription is committed in a single SAP transaction.
    /// </summary>
    public static string BuildDescription(
        string? itemName,
        string? manufacturer,
        string  currentDescription)
    {
        string prefix = BuildPrefix(itemName, manufacturer);

        if (string.IsNullOrEmpty(prefix))
            return currentDescription;

        string prefixSlash = prefix + "/";

        if (currentDescription.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase))
            return currentDescription;   // already correct — do not re-prefix

        return SafeTruncate(prefixSlash + currentDescription, MaxDescriptionLength);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE UTILITIES
    // ─────────────────────────────────────────────────────────────────────────

    private static string SafeTruncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string? ReadUdf(object? rawValue)
    {
        if (rawValue == null) return null;
        string? s = rawValue.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static string Clip(string value, int maxLen)
        => value.Length <= maxLen ? value : value[..(maxLen - 3)] + "...";
}
