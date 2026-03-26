#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Creates Goods Receipt (oInventoryGenEntry / OIGN) documents in a named SAP profile.
/// Each call opens a fresh STA-thread connection and releases it after the document is created.
/// </summary>
public class SapGoodsReceiptWriter
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapGoodsReceiptWriter> _logger;

    public SapGoodsReceiptWriter(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapGoodsReceiptWriter> logger)
    {
        _profiles = profileOptions.Value;
        _logger   = logger;
    }

    /// <returns>SAP DocEntry and DocNum of the created Goods Receipt.</returns>
    public SapDocumentRef CreateGoodsReceipt(
        string profileKey,
        string warehouseCode,
        string transferRef,
        string sourceProfile,
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
            Documents? gr    = null;

            try
            {
                company = CreateAndConnect(profile.Sap);
                gr = (Documents)company.GetBusinessObject(BoObjectTypes.oInventoryGenEntry);

                gr.DocDate  = DateTime.Today;
                gr.TaxDate  = DateTime.Today;
                gr.Comments = $"LM Transfer {transferRef} ← {sourceProfile} | {comments}".Trim();

                foreach (var line in lines)
                {
                    gr.Lines.ItemCode      = line.TargetItemCode ?? line.ItemCode;
                    gr.Lines.Quantity      = (double)line.Quantity;
                    gr.Lines.WarehouseCode = warehouseCode;
                    gr.Lines.Add();
                }

                int rc = gr.Add();
                if (rc != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception(
                        $"Goods Receipt Add() failed [{code}]: {msg} | Ref={transferRef}");
                }

                var docEntry = int.Parse(company.GetNewObjectKey());

                var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                try
                {
                    rs.DoQuery($"SELECT DocNum FROM OIGN WHERE DocEntry = {docEntry}");
                    var docNum = rs.EoF ? docEntry.ToString()
                        : rs.Fields.Item("DocNum").Value?.ToString() ?? docEntry.ToString();
                    result = new SapDocumentRef(docEntry, docNum);
                }
                finally
                {
                    Marshal.ReleaseComObject(rs);
                }

                _logger.LogInformation(
                    "SapGoodsReceiptWriter: created | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref}",
                    profileKey, docEntry, result.DocNum, transferRef);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                if (gr != null) Marshal.ReleaseComObject(gr);
                DisconnectAndRelease(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null) throw threadException;
        return result!;
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

// ── Shared DTOs for GI / GR writers ──────────────────────

public record GoodsDocumentLine(
    string  ItemCode,
    decimal Quantity,
    string? TargetItemCode = null);

public record SapDocumentRef(int DocEntry, string DocNum);
