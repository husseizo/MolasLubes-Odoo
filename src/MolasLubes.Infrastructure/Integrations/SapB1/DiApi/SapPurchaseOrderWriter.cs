#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Creates Purchase Orders (oPurchaseOrders / OPOR) in AutoHub company.
/// Used for Liqui Moly replenishment SO→PO→GR flow.
/// Each call opens a fresh STA-thread connection and releases it after the document is created.
/// </summary>
public class SapPurchaseOrderWriter
{
    /// <summary>
    /// Looks up the branch ID (BPLId) for a given warehouse code using OWHS table.
    /// </summary>
    private int GetBranchIdForWarehouse(string warehouseCode, Company company)
    {
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
        try
        {
            rs.DoQuery($"SELECT BPLid FROM OWHS WHERE WhsCode = '{warehouseCode.Replace("'", "''")}'");
            if (!rs.EoF && rs.Fields.Item("BPLid").Value != null)
                return Convert.ToInt32(rs.Fields.Item("BPLid").Value);

            throw new Exception($"No BPLid found for warehouse '{warehouseCode}'");
        }
        finally
        {
            Marshal.ReleaseComObject(rs);
        }
    }

    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapPurchaseOrderWriter> _logger;

    // Vendor code for Molas Lubes Ltd in AutoHub company (created as SUP00001)
    private const string MOLASLUBES_VENDOR_CODE = "SUP00001";
    private const string PURCHASE_ORDER_CURRENCY = "TZS";

    public SapPurchaseOrderWriter(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapPurchaseOrderWriter> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <summary>
    /// Creates a Purchase Order in AutoHub company for inter-company replenishment.
    /// Uses vendor SUP00001 (Molas Lubes Ltd) and prices sourced from the corresponding Sales Order.
    /// </summary>
    /// <param name="profileKey">Profile key for AutoHub company (e.g., "AutoHub")</param>
    /// <param name="transferRef">Unique reference matching the Sales Order (e.g., "LM-REP-20250327-001")</param>
    /// <param name="comments">Additional comments to store in the document</param>
    /// <param name="lines">Line items with target item codes, quantities and prices from the SO</param>
    /// <returns>SAP DocEntry and DocNum of the created Purchase Order</returns>
    public SapDocumentRef CreatePurchaseOrder(
        string profileKey,
        string transferRef,
        string comments,
        IReadOnlyList<PurchaseOrderLine> lines)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        if (lines == null || lines.Count == 0)
            throw new ArgumentException("At least one line item is required.", nameof(lines));

        _logger.LogInformation(
            "SapPurchaseOrderWriter: Creating PO | Profile={Profile} | Vendor={Vendor} | Ref={Ref} | LineCount={Count}",
            profileKey, MOLASLUBES_VENDOR_CODE, transferRef, lines.Count);

        SapDocumentRef? result = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company? company = null;
                Documents? po = null;

                try
                {
                    company = CreateAndConnect(profile.Sap);
                    po = (Documents)company.GetBusinessObject(BoObjectTypes.oPurchaseOrders);

                    // Header
                    po.CardCode    = MOLASLUBES_VENDOR_CODE;
                    po.DocDate     = DateTime.Today;
                    po.TaxDate     = DateTime.Today;
                    po.DocDueDate  = DateTime.Today;
                    po.DocCurrency = PURCHASE_ORDER_CURRENCY;  // Local currency
                    po.Comments    = $"LM Replenishment {transferRef} | {comments}".Trim();

                    var purchaseWarehouse = lines
                        .Select(l => l.WarehouseCode?.Trim())
                        .FirstOrDefault(w => !string.IsNullOrWhiteSpace(w));

                    if (!string.IsNullOrWhiteSpace(purchaseWarehouse))
                    {
                        var branchId = GetBranchIdForWarehouse(purchaseWarehouse!, company);
                        po.BPL_IDAssignedToInvoice = branchId;
                        _logger.LogDebug(
                            "SapPurchaseOrderWriter: set BPLId={BPLId} from warehouse={Warehouse} | Ref={Ref}",
                            branchId, purchaseWarehouse, transferRef);
                    }
                    else if (profile.Sap.BranchId.HasValue)
                    {
                        po.BPL_IDAssignedToInvoice = profile.Sap.BranchId.Value;
                        _logger.LogDebug(
                            "SapPurchaseOrderWriter: set fallback BPLId={BPLId} from profile | Ref={Ref}",
                            profile.Sap.BranchId.Value, transferRef);
                    }

                    // User-defined fields for tracking (mirrors SO UDFs)
                    po.UserFields.Fields.Item("U_TransferRef").Value = transferRef;
                    po.UserFields.Fields.Item("U_FromDb").Value      = "MolasLubes";
                    po.UserFields.Fields.Item("U_ToDb").Value        = profileKey;

                    // Lines
                    int lineIndex    = 0;
                    int skippedLines = 0;

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line.TargetItemCode))
                        {
                            _logger.LogWarning(
                                "SapPurchaseOrderWriter: Skipping line {Index} | Reason=Empty TargetItemCode",
                                lineIndex);
                            skippedLines++;
                            lineIndex++;
                            continue;
                        }

                        if (line.Quantity <= 0)
                        {
                            _logger.LogWarning(
                                "SapPurchaseOrderWriter: Skipping line {Index} | Item={Item} | Reason=Invalid quantity {Qty}",
                                lineIndex, line.TargetItemCode, line.Quantity);
                            skippedLines++;
                            lineIndex++;
                            continue;
                        }

                        // Add() advances to next line — must NOT be called after the last written line
                        var writtenCount = lineIndex - skippedLines;
                        if (writtenCount > 0)
                            po.Lines.Add();

                        po.Lines.ItemCode  = line.TargetItemCode;
                        po.Lines.Quantity  = (double)line.Quantity;
                        po.Lines.Currency  = PURCHASE_ORDER_CURRENCY;

                        if (line.UnitPrice > 0)
                            po.Lines.Price = (double)line.UnitPrice;

                        if (!string.IsNullOrWhiteSpace(line.WarehouseCode))
                            po.Lines.WarehouseCode = line.WarehouseCode;

                        _logger.LogDebug(
                            "SapPurchaseOrderWriter: Line {Index} | Item={Item} | Qty={Qty} | Price={Price:F2}",
                            lineIndex, line.TargetItemCode, line.Quantity, line.UnitPrice);

                        lineIndex++;
                    }

                    if (lineIndex == skippedLines)
                    {
                        throw new InvalidOperationException(
                            $"All {lines.Count} lines were skipped due to invalid data | Ref={transferRef}");
                    }

                    // Commit
                    int rc = po.Add();
                    if (rc != 0)
                    {
                        company.GetLastError(out var code, out var msg);
                        throw new Exception(
                            $"Purchase Order Add() failed [{code}]: {msg} | Ref={transferRef} | Vendor={MOLASLUBES_VENDOR_CODE}");
                    }

                    var docEntry = int.Parse(company.GetNewObjectKey());

                    // Retrieve DocNum via Recordset
                    var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                    try
                    {
                        rs.DoQuery($"SELECT DocNum FROM OPOR WHERE DocEntry = {docEntry}");
                        var docNum = rs.EoF ? docEntry.ToString()
                            : rs.Fields.Item("DocNum").Value?.ToString() ?? docEntry.ToString();
                        result = new SapDocumentRef(docEntry, docNum);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(rs);
                    }

                    _logger.LogInformation(
                        "SapPurchaseOrderWriter: created | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref} | ProcessedLines={Processed}/{Total}",
                        profileKey, docEntry, result.DocNum, transferRef, (lineIndex - skippedLines), lines.Count);
                }
                catch (Exception ex)
                {
                    threadException = ex;
                    _logger.LogError(ex,
                        "SapPurchaseOrderWriter: failed | Profile={Profile} | Ref={Ref}",
                        profileKey, transferRef);
                }
                finally
                {
                    if (po != null) Marshal.ReleaseComObject(po);
                    DisconnectAndRelease(company);
                }
            });
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
        return result!;
    }

    /// <summary>
    /// Checks AutoHub company for an OPOR whose U_TransferRef matches the given ref.
    /// Returns the existing document ref if found, or null if no match.
    /// Used to prevent duplicate PO creation when retrying after a partial failure.
    /// </summary>
    public SapDocumentRef? FindPurchaseOrderByTransferRef(string profileKey, string transferRef)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        SapDocumentRef? found = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Recordset? rs    = null;

            try
            {
                company = CreateAndConnect(profile.Sap);
                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                var safeRef = transferRef.Replace("'", "''");
                rs.DoQuery($"SELECT DocEntry, DocNum FROM OPOR WHERE U_TransferRef = '{safeRef}'");

                if (!rs.EoF)
                {
                    var docEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value);
                    var docNum   = rs.Fields.Item("DocNum").Value?.ToString() ?? docEntry.ToString();
                    found = new SapDocumentRef(docEntry, docNum);
                }
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                if (rs != null) Marshal.ReleaseComObject(rs);
                DisconnectAndRelease(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;

        if (found != null)
            _logger.LogInformation(
                "SapPurchaseOrderWriter: found existing PO | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref}",
                profileKey, found.DocEntry, found.DocNum, transferRef);

        return found;
    }

    private static Company CreateAndConnect(SapSettings sap)
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
            throw new Exception($"SAP connect failed ({code}): {msg} — DB={sap.CompanyDB}");
        }

        return company;
    }

    private static void DisconnectAndRelease(Company? company)
    {
        if (company == null) return;
        try { if (company.Connected) company.Disconnect(); } catch { /* best effort */ }
        Marshal.ReleaseComObject(company);
    }
}

// ── DTOs for purchase order creation ──────────────────────

/// <summary>
/// Line item for inter-company purchase order creation.
/// Uses TargetItemCode (AutoHub item code e.g., "3682") with price sourced from the SO.
/// </summary>
public record PurchaseOrderLine(
    string  TargetItemCode,
    decimal Quantity,
    decimal UnitPrice,
    string? WarehouseCode = null);

