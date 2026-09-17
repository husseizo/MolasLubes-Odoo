#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Domain.Orders;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Builds and writes "U_Item_Name/U_MdlTEST/OriginalDescription" into
/// RDR1.Dscription for open Sales Orders in the AutoHub company (MOLAS_Live_2021).
///
/// Data source: OITM (Item Master) — U_Item_Name (item name), U_MdlTEST (brand).
/// All DI API calls run on a dedicated STA thread (SAP COM requirement).
/// No UDFs on RDR1 are required.
/// </summary>
public class SapSalesOrderLineDescriptionUpdater
{
    private const string ProfileKey = "AutoHub";

    private readonly IntegrationProfilesOptions                   _profiles;
    private readonly ILogger<SapSalesOrderLineDescriptionUpdater> _logger;

    public SapSalesOrderLineDescriptionUpdater(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapSalesOrderLineDescriptionUpdater> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1.  SINGLE-ORDER UPDATE (public API)
    // ─────────────────────────────────────────────────────────────────────────

    public bool UpdateSalesOrderLineDescriptions(int docEntry)
    {
        if (!_profiles.Profiles.TryGetValue(ProfileKey, out var profile))
            throw new InvalidOperationException($"Integration profile '{ProfileKey}' is not configured.");

        bool result     = false;
        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            try
            {
                company = Connect(profile.Sap);
                result  = UpdateSingleOrderCore(company, docEntry);
            }
            catch (Exception ex) { threadEx = ex; }
            finally
            {
                if (company is { Connected: true }) company.Disconnect();
                if (company != null) Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            _logger.LogError(threadEx,
                "[SalesOrderDescription] Unexpected error | DocEntry={DocEntry}", docEntry);
            return false;
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2.  BATCH — ALL OPEN ORDERS (public API)
    //     One STA thread, one connection, process all orders in sequence.
    // ─────────────────────────────────────────────────────────────────────────

    public void UpdateAllOpenSalesOrderDescriptions()
    {
        if (!_profiles.Profiles.TryGetValue(ProfileKey, out var profile))
            throw new InvalidOperationException($"Integration profile '{ProfileKey}' is not configured.");

        Exception? threadEx = null;

        var thread = new Thread(() =>
        {
            Company?   company = null;
            Recordset? rs      = null;
            try
            {
                company = Connect(profile.Sap);

                _logger.LogInformation("[SalesOrderDescription] Batch: querying open Sales Orders");

                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                rs.DoQuery("SELECT DocEntry FROM ORDR WHERE DocStatus = 'O' ORDER BY DocEntry");

                var docEntries = new List<int>();
                while (!rs.EoF)
                {
                    docEntries.Add(Convert.ToInt32(rs.Fields.Item("DocEntry").Value));
                    rs.MoveNext();
                }

                Marshal.ReleaseComObject(rs);
                rs = null;

                _logger.LogInformation(
                    "[SalesOrderDescription] Batch: processing {Count} open Sales Orders",
                    docEntries.Count);

                int successCount = 0, failCount = 0;
                foreach (int docEntry in docEntries)
                {
                    bool ok = UpdateSingleOrderCore(company, docEntry);
                    if (ok) successCount++;
                    else    failCount++;
                }

                _logger.LogInformation(
                    "[SalesOrderDescription] Batch complete | Success={S} | Failed={F}",
                    successCount, failCount);
            }
            catch (Exception ex) { threadEx = ex; }
            finally
            {
                if (rs      != null) Marshal.ReleaseComObject(rs);
                if (company is { Connected: true }) company.Disconnect();
                if (company != null) Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            _logger.LogError(threadEx, "[SalesOrderDescription] Batch: unexpected error");
            throw threadEx;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CORE — runs inside an already-connected STA Company
    // ─────────────────────────────────────────────────────────────────────────

    private bool UpdateSingleOrderCore(Company company, int docEntry)
    {
        _logger.LogInformation(
            "[SalesOrderDescription] Processing DocEntry {DocEntry}", docEntry);

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
                return true;
            }

            int docNum = order.DocNum;

            // Collect all ItemCodes on this order
            var itemCodes = new List<string>(order.Lines.Count);
            for (int j = 0; j < order.Lines.Count; j++)
            {
                order.Lines.SetCurrentLine(j);
                var code = order.Lines.ItemCode;
                if (!string.IsNullOrWhiteSpace(code))
                    itemCodes.Add(code.Trim());
            }

            if (itemCodes.Count == 0)
            {
                _logger.LogInformation(
                    "[SalesOrderDescription] DocEntry {DocEntry} has no item lines — skipped", docEntry);
                return true;
            }

            // Query OITM once for U_Item_Name (name) and U_MdlTEST (brand)
            var oitmData = QueryOitm(company, itemCodes);

            int changedLines = 0;

            for (int i = 0; i < order.Lines.Count; i++)
            {
                order.Lines.SetCurrentLine(i);

                string  itemCode    = (order.Lines.ItemCode        ?? string.Empty).Trim();
                string  currentDesc = (order.Lines.ItemDescription ?? string.Empty).Trim();

                oitmData.TryGetValue(itemCode, out var master);
                string? itemName     = master.ItemName;
                string? manufacturer = master.Manufacturer;

                string prefix = SalesOrderDescriptionBuilder.BuildPrefix(itemName, manufacturer);

                if (string.IsNullOrEmpty(prefix))
                {
                    _logger.LogInformation(
                        "[SalesOrderDescription] Line {Line} ({Code}): " +
                        "U_Item_Name and U_MdlTEST both blank in OITM — skipped",
                        i, itemCode);
                    continue;
                }

                if (currentDesc.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "[SalesOrderDescription] Line {Line} ({Code}): already formatted — skipped",
                        i, itemCode);
                    continue;
                }

                string newDesc = SalesOrderDescriptionBuilder.BuildDescription(
                    itemName, manufacturer, currentDesc);

                _logger.LogInformation(
                    "[SalesOrderDescription] Line {Line} ({Code}): '{Old}' -> '{New}'",
                    i, itemCode, Clip(currentDesc, 40), Clip(newDesc, 60));

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
                    "[SalesOrderDescription] Update failed | DocEntry={DocEntry} | " +
                    "DocNum={DocNum} | SapCode={Code} | SapMsg={Msg}",
                    docEntry, docNum, errorCode, errorMessage);
                return false;
            }

            _logger.LogInformation(
                "[SalesOrderDescription] Updated | DocEntry={DocEntry} | DocNum={DocNum} | Lines={N}",
                docEntry, docNum, changedLines);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[SalesOrderDescription] Error on DocEntry {DocEntry}", docEntry);
            return false;
        }
        finally
        {
            if (order != null) Marshal.ReleaseComObject(order);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OITM QUERY — U_Item_Name + U_MdlTEST for a set of ItemCodes
    // Must be called on the same STA thread as the Company object.
    // ─────────────────────────────────────────────────────────────────────────

    internal static Dictionary<string, (string? ItemName, string? Manufacturer)> QueryOitm(
        Company company,
        IEnumerable<string> itemCodes)
    {
        var result = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);

        var codes = itemCodes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
            return result;

        var inList = string.Join(",",
            codes.Select(c => $"'{c.Replace("'", "''")}'"));

        Recordset? rs = null;
        try
        {
            rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
            rs.DoQuery(
                $"SELECT ItemCode, U_Item_Name, U_MdlTEST " +
                $"FROM OITM " +
                $"WHERE ItemCode IN ({inList})");

            while (!rs.EoF)
            {
                string  code = rs.Fields.Item("ItemCode").Value?.ToString()?.Trim() ?? string.Empty;
                string? name = TrimField(rs.Fields.Item("U_Item_Name").Value);
                string? mfr  = TrimField(rs.Fields.Item("U_MdlTEST").Value);

                if (!string.IsNullOrEmpty(code))
                    result[code] = (name, mfr);

                rs.MoveNext();
            }
        }
        finally
        {
            if (rs != null) Marshal.ReleaseComObject(rs);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SAP CONNECTION HELPER
    // ─────────────────────────────────────────────────────────────────────────

    private static Company Connect(SapSettings sap)
    {
        var company = new Company
        {
            Server        = sap.Server,
            CompanyDB     = sap.CompanyDB,
            UserName      = sap.UserName,
            Password      = sap.Password,
            DbServerType  = Enum.Parse<BoDataServerTypes>($"dst_{sap.DbServerType}"),
            language      = BoSuppLangs.ln_English,
            UseTrusted    = false,
            LicenseServer = sap.LicenseServer,
            SLDServer     = sap.SLDServer
        };

        if (company.Connect() != 0)
        {
            company.GetLastError(out var code, out var msg);
            throw new Exception(
                $"[SalesOrderDescription] SAP connect failed ({code}): {msg}");
        }

        return company;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UTILITIES
    // ─────────────────────────────────────────────────────────────────────────

    private static string? TrimField(object? rawValue)
    {
        if (rawValue == null) return null;
        string? s = rawValue.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static string Clip(string value, int maxLen)
        => value.Length <= maxLen ? value : value[..(maxLen - 3)] + "...";
}
