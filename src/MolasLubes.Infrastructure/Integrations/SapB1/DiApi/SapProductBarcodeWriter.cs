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
    /// writes the EAN to OBCD via the DI API Items business object.
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
                Items? items = null;

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

                    var unitUomEntry = (int)rs.Fields.Item("UomEntry").Value;
                    ReleaseComSafely(rs, "Recordset (OUOM)");
                    rs = null;

                    items = (Items)company.GetBusinessObject(BoObjectTypes.oItems);

                    foreach (var product in toWrite)
                    {
                        if (ct.IsCancellationRequested) break;

                        try
                        {
                            if (!items.GetByKey(product.ArticleNumber))
                            {
                                _logger.LogWarning(
                                    "SapProductBarcodeWriter: item {ArticleNumber} not found in SAP — skipping",
                                    product.ArticleNumber);
                                skipped++;
                                continue;
                            }

                            // Idempotency: skip if a Unit barcode already exists
                            var alreadyHasUnit = false;
                            for (var j = 0; j < items.Barcodes.Count; j++)
                            {
                                items.Barcodes.SetCurrentLine(j);
                                if (items.Barcodes.UoMEntry == unitUomEntry
                                    && !string.IsNullOrEmpty(items.Barcodes.BcdCode))
                                {
                                    alreadyHasUnit = true;
                                    break;
                                }
                            }

                            if (alreadyHasUnit)
                            {
                                _logger.LogDebug(
                                    "SapProductBarcodeWriter: {ArticleNumber} already has Unit barcode — skipping",
                                    product.ArticleNumber);
                                skipped++;
                                continue;
                            }

                            // Position to the correct row: use the blank last row if empty, else add
                            var lastIdx = items.Barcodes.Count - 1;
                            if (lastIdx >= 0)
                            {
                                items.Barcodes.SetCurrentLine(lastIdx);
                                var lastCode = items.Barcodes.BcdCode;
                                if (!string.IsNullOrEmpty(lastCode))
                                    items.Barcodes.Add();
                            }

                            items.Barcodes.BcdCode   = product.EanCode;
                            items.Barcodes.UoMEntry  = unitUomEntry;

                            var retCode = items.Update();
                            if (retCode != 0)
                            {
                                company.GetLastError(out var errCode, out var errMsg);
                                _logger.LogError(
                                    "SapProductBarcodeWriter: failed for {ArticleNumber} | SAP [{Code}]: {Msg}",
                                    product.ArticleNumber, errCode, errMsg);
                                failed++;
                            }
                            else
                            {
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
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "SapProductBarcodeWriter: unexpected error for {ArticleNumber}",
                                product.ArticleNumber);
                            failed++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
                finally
                {
                    ReleaseComSafely(items, "Items");
                    ReleaseComSafely(rs, "Recordset");

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
