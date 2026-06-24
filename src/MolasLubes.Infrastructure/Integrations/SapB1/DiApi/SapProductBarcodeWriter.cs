using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

[SupportedOSPlatform("windows")]
public class SapProductBarcodeWriter
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapProductBarcodeWriter> _logger;

    public SapProductBarcodeWriter(
        SapDiApiConnection connection,
        ILogger<SapProductBarcodeWriter> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>
    /// For each product that has an EAN code but no Unit barcode in SAP yet,
    /// writes the EAN to OBCD via BarCodesService.
    /// Idempotent: skips items that already have a Unit barcode.
    /// </summary>
    public Task WriteAsync(IList<LiquiMolyProductDto> products, CancellationToken ct = default)
    {
        var toWrite = products
            .Where(p => !string.IsNullOrWhiteSpace(p.EanCode) && !p.HasUnitBarcode)
            .ToList();

        if (toWrite.Count == 0)
        {
            _logger.LogInformation("SapProductBarcodeWriter: no products need barcode writes");
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "SapProductBarcodeWriter: writing EAN barcodes for {Count} product(s)",
            toWrite.Count);

        Exception? threadException = null;
        var written = 0;
        var skipped = 0;
        var failed = 0;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company? company = null;
                Recordset? rs = null;
                CompanyService? cs = null;
                BarCodesService? barcodeService = null;

                try
                {
                    company = _connection.CreateNewCompany();
                    if (company.Connect() != 0)
                    {
                        company.GetLastError(out var code, out var message);
                        throw new Exception($"SAP connect failed {code}: {message}");
                    }

                    // Resolve the "Unit" UoM entry from OUOM
                    rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                    rs.DoQuery("SELECT UomEntry FROM OUOM WHERE UomCode = 'Unit'");
                    if (rs.EoF)
                    {
                        _logger.LogWarning(
                            "SapProductBarcodeWriter: 'Unit' UoM not found in SAP — skipping all barcode writes");
                        return;
                    }

                    var unitUomEntry = Convert.ToInt32(rs.Fields.Item("UomEntry").Value);
                    ReleaseComSafely(rs, "Recordset (OUOM)");
                    rs = null;

                    // Get BarCodesService via CompanyService
                    cs = company.GetCompanyService();
                    barcodeService = (BarCodesService)cs.GetBusinessService(ServiceTypes.BarCodesService);

                    // Reuse a single Recordset for idempotency checks
                    rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                    foreach (var product in toWrite)
                    {
                        if (ct.IsCancellationRequested) break;

                        BarCode? barCode = null;
                        BarCodeParams? retParams = null;

                        try
                        {
                            var safeCode = product.ArticleNumber.Replace("'", "''");

                            // Idempotency: skip if Unit barcode already in OBCD
                            rs.DoQuery(
                                $"SELECT COUNT(*) AS Cnt FROM OBCD WHERE ItemCode = '{safeCode}' AND UomEntry = {unitUomEntry}");
                            if (!rs.EoF)
                            {
                                var cnt = Convert.ToInt32(rs.Fields.Item("Cnt").Value);
                                if (cnt > 0)
                                {
                                    _logger.LogDebug(
                                        "SapProductBarcodeWriter: {ArticleNumber} already has Unit barcode — skipping",
                                        product.ArticleNumber);
                                    skipped++;
                                    continue;
                                }
                            }

                            barCode = (BarCode)barcodeService.GetDataInterface(
                                BarCodesServiceDataInterfaces.bsBarCode);

                            barCode.ItemNo = product.ArticleNumber;
                            barCode.UoMEntry = unitUomEntry;
                            barCode.BarCode = product.EanCode;

                            retParams = barcodeService.Add(barCode);

                            _logger.LogInformation(
                                "SapProductBarcodeWriter: wrote EAN {Ean} → {ArticleNumber}",
                                product.EanCode, product.ArticleNumber);

                            // Reflect the change on the DTO so downstream mappers see Unit barcode
                            product.HasUnitBarcode         = true;
                            product.PrimaryBarcode         = product.EanCode;
                            product.PrimaryBarcodeUomCode  = "Unit";
                            product.BarcodeResolutionStatus = "UNIT_MATCH";

                            written++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "SapProductBarcodeWriter: unexpected error for {ArticleNumber}",
                                product.ArticleNumber);
                            failed++;
                        }
                        finally
                        {
                            if (retParams != null) ReleaseComSafely(retParams, "BarCodeParams");
                            if (barCode != null) ReleaseComSafely(barCode, "BarCode");
                        }
                    }
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
                finally
                {
                    ReleaseComSafely(rs, "Recordset");
                    ReleaseComSafely(barcodeService, "BarCodesService");
                    ReleaseComSafely(cs, "CompanyService");

                    if (company != null && company.Connected)
                    {
                        try { company.Disconnect(); }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "SapProductBarcodeWriter: failed to disconnect SAP company cleanly.");
                        }
                    }

                    ReleaseComSafely(company, "Company");
                }
            });
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw threadException;

        _logger.LogInformation(
            "SapProductBarcodeWriter: complete | Written={Written} | Skipped={Skipped} | Failed={Failed}",
            written, skipped, failed);

        return Task.CompletedTask;
    }

    private void ReleaseComSafely(object? comObject, string objectName)
    {
        if (comObject == null) return;
        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SapProductBarcodeWriter: failed to release COM object {ObjectName}.", objectName);
        }
    }
}
