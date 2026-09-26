#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Creates Inventory Transfer (OWTR / oStockTransfer) documents in a named SAP profile,
/// optionally linked to a Transfer Request (OWTQ) via BaseType/BaseEntry/BaseLine.
/// Also supports closing a Transfer Request (OWTQ) for the cross-company flow.
/// </summary>
public class SapInventoryTransferWriter
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapInventoryTransferWriter> _logger;

    public SapInventoryTransferWriter(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapInventoryTransferWriter> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <summary>
    /// Creates an Inventory Transfer (OWTR) in the given SAP profile.
    /// When <paramref name="baseRequestDocEntry"/> is provided, each line is linked to
    /// the corresponding OWTQ line via BaseType=InventoryTransferRequest/BaseEntry/BaseLine.
    /// </summary>
    public SapDocumentRef CreateInventoryTransfer(
        string profileKey,
        string fromWarehouseCode,
        string toWarehouseCode,
        string transferRef,
        string comments,
        IReadOnlyList<InventoryTransferLine> lines,
        int? baseRequestDocEntry = null)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        SapDocumentRef? result          = null;
        Exception?      threadException = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company?       company = null;
                StockTransfer? it      = null;

                try
                {
                    company = CreateAndConnect(profile.Sap);
                    it = (StockTransfer)company.GetBusinessObject(BoObjectTypes.oStockTransfer);

                    it.DocDate       = DateTime.Today;
                    it.TaxDate       = DateTime.Today;
                    it.Comments      = $"LM Transfer {transferRef} | {comments}".Trim();
                    it.FromWarehouse = fromWarehouseCode;
                    it.ToWarehouse   = toWarehouseCode;

                    try { it.UserFields.Fields.Item("U_TransferRef").Value = transferRef; }
                    catch { /* UDF may not exist on OWTR */ }

                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (i > 0) it.Lines.Add();
                        var line = lines[i];

                        it.Lines.ItemCode          = line.ItemCode;
                        it.Lines.Quantity          = (double)line.Quantity;
                        it.Lines.FromWarehouseCode = fromWarehouseCode;
                        it.Lines.WarehouseCode     = toWarehouseCode;

                        if (baseRequestDocEntry.HasValue && line.BaseRequestLineNum.HasValue)
                        {
                            it.Lines.BaseType  = InvBaseDocTypeEnum.InventoryTransferRequest;
                            it.Lines.BaseEntry = baseRequestDocEntry.Value;
                            it.Lines.BaseLine  = line.BaseRequestLineNum.Value;
                        }
                    }

                    int rc = it.Add();
                    if (rc != 0)
                    {
                        company.GetLastError(out var code, out var msg);
                        throw new Exception(
                            $"InventoryTransfer Add() failed [{code}]: {msg} | Ref={transferRef}");
                    }

                    var docEntry = int.Parse(company.GetNewObjectKey());

                    var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                    try
                    {
                        rs.DoQuery($"SELECT DocNum FROM OWTR WHERE DocEntry = {docEntry}");
                        var docNum = rs.EoF ? docEntry.ToString()
                            : rs.Fields.Item("DocNum").Value?.ToString() ?? docEntry.ToString();
                        result = new SapDocumentRef(docEntry, docNum);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(rs);
                    }

                    _logger.LogInformation(
                        "SapInventoryTransferWriter: created OWTR | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref} | BaseOwtq={BaseOwtq}",
                        profileKey, docEntry, result.DocNum, transferRef, baseRequestDocEntry?.ToString() ?? "-");
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
                finally
                {
                    if (it != null) Marshal.ReleaseComObject(it);
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
    /// Closes an Inventory Transfer Request (OWTQ) in the given profile.
    /// Used for the cross-company flow where GI+GR does not automatically close the request.
    /// </summary>
    public void CloseTransferRequest(string profileKey, int owtqDocEntry)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company?       company = null;
                StockTransfer? req     = null;

                try
                {
                    company = CreateAndConnect(profile.Sap);
                    req = (StockTransfer)company.GetBusinessObject(BoObjectTypes.oInventoryTransferRequest);

                    bool getOk = req.GetByKey(owtqDocEntry);
                    if (!getOk)
                    {
                        company.GetLastError(out var code, out var msg);
                        throw new Exception(
                            $"OWTQ GetByKey({owtqDocEntry}) failed [{code}]: {msg}");
                    }

                    int closeResult = req.Close();
                    if (closeResult != 0)
                    {
                        company.GetLastError(out var code, out var msg);
                        throw new Exception(
                            $"OWTQ Close({owtqDocEntry}) failed [{code}]: {msg}");
                    }

                    _logger.LogInformation(
                        "SapInventoryTransferWriter: closed OWTQ | Profile={Profile} | DocEntry={DocEntry}",
                        profileKey, owtqDocEntry);
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
                finally
                {
                    if (req != null) Marshal.ReleaseComObject(req);
                    DisconnectAndRelease(company);
                }
            });
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
    }

    /// <summary>
    /// Checks the SAP company for an OWTR whose U_TransferRef matches the given ref.
    /// Returns the existing document ref if found, or null if no match.
    /// </summary>
    public SapDocumentRef? FindInventoryTransferByTransferRef(string profileKey, string transferRef)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        SapDocumentRef? found           = null;
        Exception?      threadException = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company?   company = null;
                Recordset? rs      = null;

                try
                {
                    company = CreateAndConnect(profile.Sap);
                    rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                    var safeRef = transferRef.Replace("'", "''");
                    rs.DoQuery($"SELECT DocEntry, DocNum FROM OWTR WHERE U_TransferRef = '{safeRef}'");

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
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;

        if (found != null)
            _logger.LogInformation(
                "SapInventoryTransferWriter: found existing OWTR | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref}",
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

/// <summary>
/// A single line for an Inventory Transfer (OWTR).
/// </summary>
public record InventoryTransferLine(
    string  ItemCode,
    decimal Quantity,
    int?    BaseRequestLineNum = null);
