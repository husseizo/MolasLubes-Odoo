#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Creates Goods Receipt (oInventoryGenEntry / OIGN) documents in a named SAP profile.
/// Supports both standalone receipts (for GI→GR transfer flow) and
/// PO-based receipts (for inter-company SO→PO→GR flow).
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
                _logger.LogDebug("SapGoodsReceiptWriter: connecting to {Profile} | Ref={Ref}", profileKey, transferRef);
                company = CreateAndConnect(profile.Sap);

                _logger.LogDebug("SapGoodsReceiptWriter: connected, getting OIGN object | Ref={Ref}", transferRef);
                gr = (Documents)company.GetBusinessObject(BoObjectTypes.oInventoryGenEntry);

                gr.DocDate  = DateTime.Today;
                gr.TaxDate  = DateTime.Today;
                gr.Comments = $"LM Transfer {transferRef} ← {sourceProfile} | {comments}".Trim();

                _logger.LogDebug("SapGoodsReceiptWriter: setting UDFs | Ref={Ref}", transferRef);
                TrySetUserField(gr, "U_TransferRef", transferRef,   profileKey, transferRef);
                TrySetUserField(gr, "U_FromDb",      sourceProfile, profileKey, transferRef);
                TrySetUserField(gr, "U_ToDb",        profileKey,    profileKey, transferRef);

                _logger.LogDebug("SapGoodsReceiptWriter: adding {Count} line(s) | Ref={Ref}", lines.Count, transferRef);
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i > 0) gr.Lines.Add();
                    var line = lines[i];
                    gr.Lines.ItemCode      = line.TargetItemCode ?? line.ItemCode;
                    gr.Lines.Quantity      = (double)line.Quantity;
                    gr.Lines.WarehouseCode = warehouseCode;
                    _logger.LogDebug(
                        "SapGoodsReceiptWriter: line {I} | ItemCode={ItemCode} | Qty={Qty} | WH={WH}",
                        i, gr.Lines.ItemCode, gr.Lines.Quantity, warehouseCode);
                }

                _logger.LogDebug("SapGoodsReceiptWriter: calling gr.Add() | Ref={Ref}", transferRef);
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

    /// <summary>
    /// Creates a Goods Receipt PO (oPurchaseDeliveryNotes / OPDN) in AutoHub,
    /// based on an existing Purchase Order (OPOR).
    /// SAP will copy all lines automatically via base document linking
    /// (BaseType = 22 = Purchase Order, BaseEntry = PO DocEntry).
    /// Used for the inter-company SO→PO→GR flow.
    /// </summary>
    /// <param name="profileKey">Profile key for AutoHub company (e.g., "AutoHub")</param>
    /// <param name="poDocEntry">DocEntry of the Purchase Order to receive against</param>
    /// <param name="transferRef">Replenishment reference for tracking</param>
    /// <param name="comments">Additional comments to store in the document</param>
    /// <returns>SAP DocEntry and DocNum of the created Goods Receipt PO</returns>
    public SapDocumentRef CreateGoodsReceiptFromPurchaseOrder(
        string profileKey,
        int poDocEntry,
        string transferRef,
        string comments)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        _logger.LogInformation(
            "SapGoodsReceiptWriter: Creating GR from PO | Profile={Profile} | PODocEntry={PoDocEntry} | Ref={Ref}",
            profileKey, poDocEntry, transferRef);

        SapDocumentRef? result = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Documents? grpo  = null;

            try
            {
                company = CreateAndConnect(profile.Sap);

                // oPurchaseDeliveryNotes = Goods Receipt PO (OPDN)
                // This is the correct SAP document type for receiving goods against a PO
                grpo = (Documents)company.GetBusinessObject(BoObjectTypes.oPurchaseDeliveryNotes);

                // Header
                grpo.CardCode = "SUP00001";  // Molas Lubes Ltd vendor
                grpo.DocDate  = DateTime.Today;
                grpo.TaxDate  = DateTime.Today;
                grpo.Comments = $"LM Replenishment {transferRef} — GR from PO {poDocEntry} | {comments}".Trim();
                grpo.UserFields.Fields.Item("U_TransferRef").Value = transferRef;
                grpo.UserFields.Fields.Item("U_FromDb").Value      = "MolasLubes";
                grpo.UserFields.Fields.Item("U_ToDb").Value        = profileKey;

                // Copy all lines from the Purchase Order via base document linking
                // SAP BaseType 22 = Purchase Order (OPOR)
                grpo.Lines.BaseType  = 22;
                grpo.Lines.BaseEntry = poDocEntry;
                grpo.Lines.BaseLine  = 0;  // line index 0 = first PO line
                grpo.Lines.Add();

                // Note: For multi-line POs, SAP copies all open lines when BaseEntry is set
                // on the document header before any Lines.Add() calls.
                // If only specific lines are needed, set BaseType/BaseEntry/BaseLine per line.

                int rc = grpo.Add();
                if (rc != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception(
                        $"Goods Receipt PO Add() failed [{code}]: {msg} | PODocEntry={poDocEntry} | Ref={transferRef}");
                }

                var docEntry = int.Parse(company.GetNewObjectKey());

                var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                try
                {
                    rs.DoQuery($"SELECT DocNum FROM OPDN WHERE DocEntry = {docEntry}");
                    var docNum = rs.EoF ? docEntry.ToString()
                        : rs.Fields.Item("DocNum").Value?.ToString() ?? docEntry.ToString();
                    result = new SapDocumentRef(docEntry, docNum);
                }
                finally
                {
                    Marshal.ReleaseComObject(rs);
                }

                _logger.LogInformation(
                    "SapGoodsReceiptWriter: GR from PO created | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | PODocEntry={PoEntry} | Ref={Ref}",
                    profileKey, docEntry, result.DocNum, poDocEntry, transferRef);
            }
            catch (Exception ex)
            {
                threadException = ex;
                _logger.LogError(ex,
                    "SapGoodsReceiptWriter: GR from PO failed | Profile={Profile} | PODocEntry={PoDocEntry} | Ref={Ref}",
                    profileKey, poDocEntry, transferRef);
            }
            finally
            {
                if (grpo != null) Marshal.ReleaseComObject(grpo);
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
    /// Checks AutoHub company for an OPDN (Goods Receipt PO) whose U_TransferRef matches the given ref.
    /// Returns the existing document ref if found, or null if no match.
    /// Used to prevent duplicate GR creation when retrying after a partial failure.
    /// </summary>
    public SapDocumentRef? FindGoodsReceiptPoByTransferRef(string profileKey, string transferRef)
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
                rs.DoQuery($"SELECT DocEntry, DocNum FROM OPDN WHERE U_TransferRef = '{safeRef}'");

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
                "SapGoodsReceiptWriter: found existing GR PO | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref}",
                profileKey, found.DocEntry, found.DocNum, transferRef);

        return found;
    }

    /// <summary>
    /// Checks the target SAP company for an OIGN whose U_TransferRef matches the given ref.
    /// Returns the existing document ref if found, or null if no match.
    /// Used by RetryReceiptAsync to prevent duplicate GR creation.
    /// </summary>
    public SapDocumentRef? FindExistingReceipt(string profileKey, string transferRef)
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
                rs.DoQuery($"SELECT DocEntry, DocNum FROM OIGN WHERE U_TransferRef = '{safeRef}'");

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
                "SapGoodsReceiptWriter: found existing receipt | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref}",
                profileKey, found.DocEntry, found.DocNum, transferRef);

        return found;
    }

    private void TrySetUserField(Documents doc, string fieldName, string value, string profileKey, string transferRef)
    {
        try
        {
            doc.UserFields.Fields.Item(fieldName).Value = value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "SapGoodsReceiptWriter: UDF '{Field}' not found on OIGN in profile {Profile} | Ref={Ref} | {Msg}",
                fieldName, profileKey, transferRef, ex.Message);
        }
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
