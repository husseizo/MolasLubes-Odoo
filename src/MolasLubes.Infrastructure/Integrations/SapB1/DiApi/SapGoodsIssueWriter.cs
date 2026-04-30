#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Creates Goods Issue (oInventoryGenExit / OIGE) documents in a named SAP profile.
/// Each call opens a fresh STA-thread connection and releases it after the document is created.
/// </summary>
public class SapGoodsIssueWriter
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapGoodsIssueWriter> _logger;

    public SapGoodsIssueWriter(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapGoodsIssueWriter> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <returns>SAP DocEntry and DocNum of the created Goods Issue.</returns>
    public SapDocumentRef CreateGoodsIssue(
        string profileKey,
        string warehouseCode,
        string transferRef,
        string targetProfile,
        string comments,
        IReadOnlyList<GoodsDocumentLine> lines)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        SapDocumentRef? result = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Documents? gi = null;

            try
            {
                company = CreateAndConnect(profile.Sap);
                gi = (Documents)company.GetBusinessObject(BoObjectTypes.oInventoryGenExit);

                gi.DocDate  = DateTime.Today;
                gi.TaxDate  = DateTime.Today;
                gi.Comments = $"LM Transfer {transferRef} → {targetProfile} | {comments}".Trim();
                gi.UserFields.Fields.Item("U_TransferRef").Value = transferRef;
                gi.UserFields.Fields.Item("U_FromDb").Value      = profileKey;
                gi.UserFields.Fields.Item("U_ToDb").Value        = targetProfile;

                for (int i = 0; i < lines.Count; i++)
                {
                    if (i > 0) gi.Lines.Add();
                    var line = lines[i];
                    gi.Lines.ItemCode      = line.ItemCode;
                    gi.Lines.Quantity      = (double)line.Quantity;
                    gi.Lines.WarehouseCode = warehouseCode;
                }

                int rc = gi.Add();
                if (rc != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception(
                        $"Goods Issue Add() failed [{code}]: {msg} | Ref={transferRef}");
                }

                var docEntry = int.Parse(company.GetNewObjectKey());

                // Retrieve DocNum via Recordset
                var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                try
                {
                    rs.DoQuery($"SELECT DocNum FROM OIGE WHERE DocEntry = {docEntry}");
                    var docNum = rs.EoF ? docEntry.ToString()
                        : rs.Fields.Item("DocNum").Value?.ToString() ?? docEntry.ToString();
                    result = new SapDocumentRef(docEntry, docNum);
                }
                finally
                {
                    Marshal.ReleaseComObject(rs);
                }

                _logger.LogInformation(
                    "SapGoodsIssueWriter: created | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref}",
                    profileKey, docEntry, result.DocNum, transferRef);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                if (gi != null) Marshal.ReleaseComObject(gi);
                DisconnectAndRelease(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
        return result!;
    }

    /// <summary>
    /// Checks the source SAP company for an OIGE whose U_TransferRef matches the given ref.
    /// Returns the existing document ref if found, or null if no match.
    /// Symmetric to SapGoodsReceiptWriter.FindExistingReceipt — used when retrying after a
    /// partial failure to avoid duplicate GI creation.
    /// </summary>
    public SapDocumentRef? FindGoodsIssueByTransferRef(string profileKey, string transferRef)
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
                rs.DoQuery($"SELECT DocEntry, DocNum FROM OIGE WHERE U_TransferRef = '{safeRef}'");

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
                "SapGoodsIssueWriter: found existing GI | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref}",
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
