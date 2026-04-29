#pragma warning disable CA1416 // COM interop — Windows only

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using MolasLubes.Infrastructure.Services.LiquiMolyReplenishment;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Creates inter-company Sales Orders (oOrders / ORDR) in MolasLubes company.
/// Used for Liqui Moly replenishment SO→PO→GR flow.
/// Each call opens a fresh STA-thread connection and releases it after the document is created.
/// </summary>
public class SapInterCompanySalesOrderWriter
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly PL05PricingCalculator _pricingCalculator;
    private readonly ILogger<SapInterCompanySalesOrderWriter> _logger;

    // Customer code representing AutoHub in MolasLubes company
    private const string AUTOHUB_CUSTOMER_CODE = "SHP00118";

    public SapInterCompanySalesOrderWriter(
        IOptions<IntegrationProfilesOptions> profileOptions,
        PL05PricingCalculator pricingCalculator,
        ILogger<SapInterCompanySalesOrderWriter> logger)
    {
        _profiles = profileOptions.Value;
        _pricingCalculator = pricingCalculator;
        _logger = logger;
    }

    /// <summary>
    /// Creates a Sales Order in MolasLubes company for inter-company replenishment.
    /// Uses customer SHP00118 (representing AutoHub) and Price List 5 for pricing.
    /// </summary>
    /// <param name="profileKey">Profile key for MolasLubes company (e.g., "MolasLubes")</param>
    /// <param name="transferRef">Unique reference for the replenishment request (e.g., "LM-REP-20250327-001")</param>
    /// <param name="comments">Additional comments to store in the document</param>
    /// <param name="lines">Line items with source item codes and quantities</param>
    /// <returns>SAP DocEntry and DocNum of the created Sales Order</returns>
    public SapDocumentRef CreateSalesOrder(
        string profileKey,
        string transferRef,
        string comments,
        IReadOnlyList<InterCompanySalesOrderLine> lines)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        if (lines == null || lines.Count == 0)
            throw new ArgumentException("At least one line item is required.", nameof(lines));

        _logger.LogInformation(
            "SapInterCompanySalesOrderWriter: Creating SO | Profile={Profile} | Customer={Customer} | Ref={Ref} | LineCount={Count}",
            profileKey, AUTOHUB_CUSTOMER_CODE, transferRef, lines.Count);

        // Calculate prices for all items in batch
        var itemCodes = lines.Select(l => l.SourceItemCode).Distinct().ToList();
        var prices = _pricingCalculator.CalculatePrices(itemCodes);

        if (prices.Count == 0)
        {
            throw new InvalidOperationException(
                $"Could not calculate prices for any items in replenishment request {transferRef}");
        }

        SapDocumentRef? result = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Documents? so = null;

            try
            {
                company = CreateAndConnect(profile.Sap);
                so = (Documents)company.GetBusinessObject(BoObjectTypes.oOrders);

                // Header
                so.CardCode = AUTOHUB_CUSTOMER_CODE;
                so.DocDate = DateTime.Today;
                so.TaxDate = DateTime.Today;
                so.DocDueDate = DateTime.Today;
                so.DocCurrency = "ILS";  // Israeli Shekel
                so.Comments = $"LM Replenishment {transferRef} | {comments}".Trim();

                // User-defined fields for tracking
                so.UserFields.Fields.Item("U_TransferRef").Value = transferRef;
                so.UserFields.Fields.Item("U_FromDb").Value = profileKey;
                so.UserFields.Fields.Item("U_ToDb").Value = "AutoHub";

                // Lines
                int lineIndex = 0;
                int skippedLines = 0;

                foreach (var line in lines)
                {
                    if (!prices.TryGetValue(line.SourceItemCode, out var price))
                    {
                        _logger.LogWarning(
                            "SapInterCompanySalesOrderWriter: Skipping line {Index} | Item={Item} | Reason=No price available",
                            lineIndex, line.SourceItemCode);
                        skippedLines++;
                        lineIndex++;
                        continue;
                    }

                    if (line.Quantity <= 0)
                    {
                        _logger.LogWarning(
                            "SapInterCompanySalesOrderWriter: Skipping line {Index} | Item={Item} | Reason=Invalid quantity {Qty}",
                            lineIndex, line.SourceItemCode, line.Quantity);
                        skippedLines++;
                        lineIndex++;
                        continue;
                    }

                    so.Lines.ItemCode = line.SourceItemCode;
                    so.Lines.Quantity = (double)line.Quantity;
                    so.Lines.Price = (double)price;
                    so.Lines.Currency = "ILS";

                    _logger.LogDebug(
                        "SapInterCompanySalesOrderWriter: Line {Index} | Item={Item} | Qty={Qty} | Price={Price:F2}",
                        lineIndex, line.SourceItemCode, line.Quantity, price);

                    so.Lines.Add();
                    lineIndex++;
                }

                if (lineIndex == skippedLines)
                {
                    throw new InvalidOperationException(
                        $"All {lines.Count} lines were skipped due to missing prices or invalid quantities");
                }

                // Commit
                int rc = so.Add();
                if (rc != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception(
                        $"Sales Order Add() failed [{code}]: {msg} | Ref={transferRef} | Customer={AUTOHUB_CUSTOMER_CODE}");
                }

                var docEntry = int.Parse(company.GetNewObjectKey());

                // Retrieve DocNum via Recordset
                var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                try
                {
                    rs.DoQuery($"SELECT DocNum FROM ORDR WHERE DocEntry = {docEntry}");
                    var docNum = rs.EoF ? docEntry.ToString()
                        : rs.Fields.Item("DocNum").Value?.ToString() ?? docEntry.ToString();
                    result = new SapDocumentRef(docEntry, docNum);
                }
                finally
                {
                    Marshal.ReleaseComObject(rs);
                }

                _logger.LogInformation(
                    "SapInterCompanySalesOrderWriter: created | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref} | ProcessedLines={Processed}/{Total}",
                    profileKey, docEntry, result.DocNum, transferRef, (lineIndex - skippedLines), lines.Count);
            }
            catch (Exception ex)
            {
                threadException = ex;
                _logger.LogError(ex,
                    "SapInterCompanySalesOrderWriter: failed | Profile={Profile} | Ref={Ref}",
                    profileKey, transferRef);
            }
            finally
            {
                if (so != null) Marshal.ReleaseComObject(so);
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
    /// Checks MolasLubes company for an ORDR whose U_TransferRef matches the given ref.
    /// Returns the existing document ref if found, or null if no match.
    /// Used to prevent duplicate SO creation when retrying after a partial failure.
    /// </summary>
    public SapDocumentRef? FindSalesOrderByTransferRef(string profileKey, string transferRef)
    {
        if (!_profiles.Profiles.TryGetValue(profileKey, out var profile))
            throw new InvalidOperationException($"Profile '{profileKey}' not configured.");

        SapDocumentRef? found = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Recordset? rs = null;

            try
            {
                company = CreateAndConnect(profile.Sap);
                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                var safeRef = transferRef.Replace("'", "''");
                rs.DoQuery($"SELECT DocEntry, DocNum FROM ORDR WHERE U_TransferRef = '{safeRef}'");

                if (!rs.EoF)
                {
                    var docEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value);
                    var docNum = rs.Fields.Item("DocNum").Value?.ToString() ?? docEntry.ToString();
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
                "SapInterCompanySalesOrderWriter: found existing SO | Profile={Profile} | DocEntry={Entry} | DocNum={Num} | Ref={Ref}",
                profileKey, found.DocEntry, found.DocNum, transferRef);

        return found;
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
        try { if (company.Connected) company.Disconnect(); } catch { /* best effort */ }
        Marshal.ReleaseComObject(company);
    }
}

// ── DTOs for inter-company sales order creation ──────────────────────

/// <summary>
/// Line item for inter-company sales order creation.
/// Uses SourceItemCode (MolasLubes item code) and quantity from replenishment request.
/// </summary>
public record InterCompanySalesOrderLine(
    string SourceItemCode,
    decimal Quantity);
