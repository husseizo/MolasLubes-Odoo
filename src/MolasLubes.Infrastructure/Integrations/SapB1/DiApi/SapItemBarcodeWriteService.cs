#pragma warning disable CA1416

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

[SupportedOSPlatform("windows")]
public class SapItemBarcodeWriteService
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapItemBarcodeWriteService> _logger;

    public SapItemBarcodeWriteService(
        SapDiApiConnection connection,
        ILogger<SapItemBarcodeWriteService> logger)
    {
        _connection = connection;
        _logger     = logger;
    }

    /// <summary>
    /// Writes a barcode to OBCD using the UomEntry integer directly (CSV import path).
    /// Skips the OUOM code→entry lookup since the caller already has the numeric entry.
    /// Idempotent: returns AlreadyExists if that UomEntry already has a barcode for the item.
    /// </summary>
    public SapBarcodeWriteResult WriteBarcode(string itemCode, string barcodeValue, int uomEntry)
    {
        SapBarcodeWriteResult? result   = null;
        Exception?             threadEx = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company?         company        = null;
                Recordset?       rs             = null;
                CompanyService?  cs             = null;
                BarCodesService? barcodeService = null;
                BarCode?         barCode        = null;
                BarCodeParams?   retParams      = null;

                try
                {
                    company = _connection.CreateNewCompany();
                    if (company.Connect() != 0)
                    {
                        company.GetLastError(out var code, out var msg);
                        throw new Exception($"SAP connect failed {code}: {msg}");
                    }

                    rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                    var safeItem = itemCode.Replace("'", "''");
                    rs.DoQuery(
                        $"SELECT COUNT(*) AS Cnt FROM OBCD WHERE ItemCode = '{safeItem}' AND UomEntry = {uomEntry}");
                    if (!rs.EoF && Convert.ToInt32(rs.Fields.Item("Cnt").Value) > 0)
                    {
                        result = SapBarcodeWriteResult.AlreadyExists();
                        return;
                    }

                    cs             = company.GetCompanyService();
                    barcodeService = (BarCodesService)cs.GetBusinessService(ServiceTypes.BarCodesService);

                    barCode          = (BarCode)barcodeService.GetDataInterface(BarCodesServiceDataInterfaces.bsBarCode);
                    barCode.ItemNo   = itemCode;
                    barCode.UoMEntry = uomEntry;
                    barCode.BarCode  = barcodeValue;

                    retParams = barcodeService.Add(barCode);

                    _logger.LogInformation(
                        "SapItemBarcodeWriteService: wrote barcode {Barcode} → {ItemCode} UoMEntry={UomEntry}",
                        barcodeValue, itemCode, uomEntry);

                    result = SapBarcodeWriteResult.Ok();
                }
                catch (Exception ex) { threadEx = ex; }
                finally
                {
                    if (retParams      != null) Marshal.FinalReleaseComObject(retParams);
                    if (barCode        != null) Marshal.FinalReleaseComObject(barCode);
                    if (rs             != null) Marshal.FinalReleaseComObject(rs);
                    if (barcodeService != null) Marshal.FinalReleaseComObject(barcodeService);
                    if (cs             != null) Marshal.FinalReleaseComObject(cs);
                    if (company != null && company.Connected) company.Disconnect();
                    if (company        != null) Marshal.FinalReleaseComObject(company);
                }
            });
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null) throw threadEx;
        return result!;
    }

    /// <summary>
    /// Writes a single barcode to OBCD for the given item + UoM.
    /// Idempotent: if that UoM already has a barcode the call returns AlreadyExists.
    /// </summary>
    public SapBarcodeWriteResult WriteBarcode(string itemCode, string barcodeValue, string uomCode)
    {
        SapBarcodeWriteResult? result    = null;
        Exception?             threadEx = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company?         company        = null;
                Recordset?       rs             = null;
                CompanyService?  cs             = null;
                BarCodesService? barcodeService = null;
                BarCode?         barCode        = null;
                BarCodeParams?   retParams      = null;

                try
                {
                    company = _connection.CreateNewCompany();
                    if (company.Connect() != 0)
                    {
                        company.GetLastError(out var code, out var msg);
                        throw new Exception($"SAP connect failed {code}: {msg}");
                    }

                    rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                    // Resolve UomEntry from OUOM
                    var safeUom  = uomCode.Replace("'", "''");
                    rs.DoQuery($"SELECT UomEntry FROM OUOM WHERE UomCode = '{safeUom}'");
                    if (rs.EoF)
                    {
                        result = SapBarcodeWriteResult.Fail($"UoM '{uomCode}' not found in SAP OUOM table.");
                        return;
                    }
                    var uomEntry = Convert.ToInt32(rs.Fields.Item("UomEntry").Value);

                    // Idempotency: skip if this UoM already has a barcode for the item
                    var safeItem = itemCode.Replace("'", "''");
                    rs.DoQuery(
                        $"SELECT COUNT(*) AS Cnt FROM OBCD WHERE ItemCode = '{safeItem}' AND UomEntry = {uomEntry}");
                    if (!rs.EoF && Convert.ToInt32(rs.Fields.Item("Cnt").Value) > 0)
                    {
                        result = SapBarcodeWriteResult.AlreadyExists();
                        return;
                    }

                    cs             = company.GetCompanyService();
                    barcodeService = (BarCodesService)cs.GetBusinessService(ServiceTypes.BarCodesService);

                    barCode          = (BarCode)barcodeService.GetDataInterface(BarCodesServiceDataInterfaces.bsBarCode);
                    barCode.ItemNo   = itemCode;
                    barCode.UoMEntry = uomEntry;
                    barCode.BarCode  = barcodeValue;

                    retParams = barcodeService.Add(barCode);

                    _logger.LogInformation(
                        "SapItemBarcodeWriteService: wrote barcode {Barcode} → {ItemCode} UoM={UomCode}",
                        barcodeValue, itemCode, uomCode);

                    result = SapBarcodeWriteResult.Ok();
                }
                catch (Exception ex) { threadEx = ex; }
                finally
                {
                    if (retParams      != null) Marshal.FinalReleaseComObject(retParams);
                    if (barCode        != null) Marshal.FinalReleaseComObject(barCode);
                    if (rs             != null) Marshal.FinalReleaseComObject(rs);
                    if (barcodeService != null) Marshal.FinalReleaseComObject(barcodeService);
                    if (cs             != null) Marshal.FinalReleaseComObject(cs);
                    if (company != null && company.Connected) company.Disconnect();
                    if (company        != null) Marshal.FinalReleaseComObject(company);
                }
            });
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null) throw threadEx;
        return result!;
    }
}

public record SapBarcodeWriteResult(bool Success, bool WasAlreadyPresent, string? Error)
{
    public static SapBarcodeWriteResult Ok()            => new(true,  false, null);
    public static SapBarcodeWriteResult AlreadyExists() => new(true,  true,  null);
    public static SapBarcodeWriteResult Fail(string e)  => new(false, false, e);
}
