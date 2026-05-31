#pragma warning disable CA1416 // COM interop — Windows only

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Creates Inventory Transfer Request documents (OWTQ / oInventoryTransferRequest)
/// in a named SAP profile.
/// </summary>
public class SapInventoryTransferRequestWriter
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapInventoryTransferRequestWriter> _logger;

    public SapInventoryTransferRequestWriter(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapInventoryTransferRequestWriter> logger)
    {
        _profiles = profileOptions.Value;
        _logger = logger;
    }

    public SapDocumentRef CreateTransferRequest(
        string profileKey,
        string sourceWarehouse,
        string targetWarehouse,
        string requestRef,
        string comments,
        IReadOnlyList<InventoryTransferRequestLine> lines)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        if (lines == null || lines.Count == 0)
            throw new ArgumentException("At least one transfer-request line is required.");

        SapDocumentRef? result = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company? company = null;
                StockTransfer? request = null;

                try
                {
                    company = CreateAndConnect(profile.Sap);
                    request = (StockTransfer)company.GetBusinessObject(BoObjectTypes.oInventoryTransferRequest);

                    var sourceBplId = GetBranchIdForWarehouse(sourceWarehouse, company);
                    var targetBplId = GetBranchIdForWarehouse(targetWarehouse, company);

                    request.DocDate = DateTime.Today;
                    request.TaxDate = DateTime.Today;
                    request.FromWarehouse = sourceWarehouse;
                    request.ToWarehouse = targetWarehouse;
                    request.Comments = $"LM Transfer Request {requestRef} | {comments}".Trim();

                    // Branch policy: header BPL is derived from source warehouse BPL.
                    TrySetBplId(request, sourceBplId);

                    // Non-blocking validation signal for operations visibility.
                    if (sourceBplId != targetBplId)
                    {
                        _logger.LogInformation(
                            "SapInventoryTransferRequestWriter: cross-branch transfer request | Ref={Ref} | SourceWh={SourceWh} BPL={SourceBpl} | TargetWh={TargetWh} BPL={TargetBpl}",
                            requestRef, sourceWarehouse, sourceBplId, targetWarehouse, targetBplId);
                    }

                    TrySetUserField(request.UserFields, "U_TransferRef", requestRef);
                    TrySetUserField(request.UserFields, "U_FromDb", profileKey);
                    TrySetUserField(request.UserFields, "U_ToDb", profileKey);

                    for (var i = 0; i < lines.Count; i++)
                    {
                        if (i > 0) request.Lines.Add();
                        var line = lines[i];

                        request.Lines.ItemCode = line.ItemCode;
                        request.Lines.Quantity = (double)line.Quantity;
                        request.Lines.FromWarehouseCode = sourceWarehouse;
                        request.Lines.WarehouseCode = targetWarehouse;
                    }

                    var rc = request.Add();
                    if (rc != 0)
                    {
                        company.GetLastError(out var code, out var msg);
                        throw new Exception(
                            $"Inventory Transfer Request Add() failed [{code}]: {msg} | Ref={requestRef}");
                    }

                    var docEntry = int.Parse(company.GetNewObjectKey());
                    var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                    try
                    {
                        rs.DoQuery($"SELECT DocNum FROM OWTQ WHERE DocEntry = {docEntry}");
                        var docNum = rs.EoF
                            ? docEntry.ToString()
                            : rs.Fields.Item("DocNum").Value?.ToString() ?? docEntry.ToString();
                        result = new SapDocumentRef(docEntry, docNum);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(rs);
                    }

                    _logger.LogInformation(
                        "SapInventoryTransferRequestWriter: created | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref} | SourceWh={SourceWh} | TargetWh={TargetWh} | SourceBPL={SourceBpl}",
                        profileKey, result.DocEntry, result.DocNum, requestRef, sourceWarehouse, targetWarehouse, sourceBplId);
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
                finally
                {
                    if (request != null) Marshal.ReleaseComObject(request);
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

    private static int GetBranchIdForWarehouse(string warehouseCode, Company company)
    {
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
        try
        {
            rs.DoQuery($"SELECT BPLid FROM OWHS WHERE WhsCode = '{warehouseCode.Replace("'", "''")}'");
            if (rs.EoF || rs.Fields.Item("BPLid").Value == null)
                throw new Exception($"No BPLid found for warehouse '{warehouseCode}'.");

            return Convert.ToInt32(rs.Fields.Item("BPLid").Value);
        }
        finally
        {
            Marshal.ReleaseComObject(rs);
        }
    }

    private void TrySetBplId(StockTransfer request, int sourceBplId)
    {
        try
        {
            var prop = request.GetType().GetProperty("BPLID", BindingFlags.Public | BindingFlags.Instance);
            if (prop?.CanWrite == true)
            {
                prop.SetValue(request, sourceBplId);
                return;
            }

            _logger.LogDebug(
                "SapInventoryTransferRequestWriter: BPLID setter not available on DI object. Header branch will be inferred from source warehouse.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "SapInventoryTransferRequestWriter: could not set BPLID explicitly. Header branch will be inferred from source warehouse.");
        }
    }

    private void TrySetUserField(UserFields userFields, string fieldName, string value)
    {
        try
        {
            userFields.Fields.Item(fieldName).Value = value;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SapInventoryTransferRequestWriter: UDF not set | Field={Field}", fieldName);
        }
    }

    private static Company CreateAndConnect(SapSettings sap)
    {
        var company = new Company
        {
            Server = sap.Server,
            CompanyDB = sap.CompanyDB,
            UserName = sap.UserName,
            Password = sap.Password,
            DbServerType = Enum.Parse<BoDataServerTypes>($"dst_{sap.DbServerType}"),
            language = BoSuppLangs.ln_English,
            UseTrusted = false,
            LicenseServer = sap.LicenseServer,
            SLDServer = sap.SLDServer
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
        try { if (company.Connected) company.Disconnect(); } catch { }
        Marshal.ReleaseComObject(company);
    }
}

public record InventoryTransferRequestLine(string ItemCode, decimal Quantity);

