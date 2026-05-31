using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

[SupportedOSPlatform("windows")]
public class SapProductBarcodeReader
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapProductBarcodeReader> _logger;

    private static readonly Dictionary<string, int> PreferredPackRank =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["3-PU"] = 1,
            ["4-PU"] = 2,
            ["6-PU"] = 3,
            ["10-PU"] = 4,
            ["12-PU"] = 5,
            ["20-PU"] = 6,
            ["24-PU"] = 7,
            ["25-PU"] = 8,
            ["50-Packing Units"] = 9,
            ["50-PU"] = 9,
        };

    public SapProductBarcodeReader(
        SapDiApiConnection connection,
        ILogger<SapProductBarcodeReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public Task EnrichAsync(IList<LiquiMolyProductDto> products, CancellationToken ct = default)
    {
        if (products.Count == 0)
            return Task.CompletedTask;

        var articleNumbers = products
            .Select(x => x.ArticleNumber)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (articleNumbers.Count == 0)
            return Task.CompletedTask;

        var snapshots = ReadByArticleNumbers(articleNumbers, ct);

        foreach (var product in products)
        {
            if (!snapshots.TryGetValue(product.ArticleNumber, out var snapshot))
                continue;

            product.HasUnitBarcode = snapshot.HasUnitBarcode;
            product.PrimaryBarcode = snapshot.PrimaryBarcode?.Code;
            product.PrimaryBarcodeUomCode = snapshot.PrimaryBarcode?.UomCode;
            product.PrimaryBarcodeUomName = snapshot.PrimaryBarcode?.UomName;
            product.PrimaryBarcodeUomEntry = snapshot.PrimaryBarcode?.UomEntry;
            product.PrimaryBarcodeBaseQtyInGroup = snapshot.PrimaryBarcode?.BaseQtyInGroup;
            product.BarcodeResolutionStatus = snapshot.ResolutionStatus;
            product.BarcodeResolutionNote = snapshot.ResolutionNote;
            product.AllBarcodes = snapshot.Barcodes;
            product.SapUomInfo = snapshot.SapUomInfo;
        }

        return Task.CompletedTask;
    }

    public Dictionary<string, SapProductBarcodeSnapshot> ReadByArticleNumbers(
        IReadOnlyCollection<string> articleNumbers,
        CancellationToken ct = default)
    {
        if (articleNumbers.Count == 0)
            return new Dictionary<string, SapProductBarcodeSnapshot>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, SapProductBarcodeSnapshot>(StringComparer.OrdinalIgnoreCase);
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company? company = null;
                Recordset? rs = null;

                try
                {
                    company = _connection.CreateNewCompany();
                    if (company.Connect() != 0)
                    {
                        company.GetLastError(out var code, out var message);
                        throw new Exception($"SAP connect failed {code}: {message}");
                    }

                    rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                    var inList = string.Join(", ", articleNumbers.Select(ToSqlLiteral));

                    rs.DoQuery($@"
SELECT
    I.ItemCode AS ArticleNumber,
    I.ItemName,
    I.UgpEntry AS UomGroupEntry,
    G.UgpName AS UomGroupName,
    I.CntUnitMsr AS DefaultCountingUomName,
    I.InvntryUom AS InventoryUomName,
    I.SalUnitMsr AS SalesUomName,
    I.BuyUnitMsr AS PurchaseUomName,
    B.BcdCode AS Barcode,
    B.UomEntry AS BarcodeUomEntry,
    U.UomCode AS BarcodeUomCode,
    U.UomName AS BarcodeUomName,
    G1.BaseQty AS BarcodeUomBaseQtyInGroup
FROM OITM I
LEFT JOIN OUGP G
    ON G.UgpEntry = I.UgpEntry
LEFT JOIN OBCD B
    ON B.ItemCode = I.ItemCode
LEFT JOIN OUOM U
    ON U.UomEntry = B.UomEntry
LEFT JOIN UGP1 G1
    ON G1.UgpEntry = I.UgpEntry
   AND G1.UomEntry = B.UomEntry
WHERE I.ItemCode IN ({inList})
ORDER BY
    I.ItemCode,
    CASE WHEN U.UomCode = 'Unit' THEN 0 ELSE 1 END,
    G1.BaseQty,
    U.UomCode,
    B.BcdCode");

                    var buckets = new Dictionary<string, SapProductBarcodeAccumulator>(StringComparer.OrdinalIgnoreCase);

                    while (!rs.EoF)
                    {
                        var article = ReadString(rs, "ArticleNumber");
                        if (string.IsNullOrWhiteSpace(article))
                        {
                            rs.MoveNext();
                            continue;
                        }

                        if (!buckets.TryGetValue(article, out var bucket))
                        {
                            bucket = new SapProductBarcodeAccumulator(
                                article,
                                ReadString(rs, "ItemName"),
                                new LiquiMolySapUomInfoDto
                                {
                                    UomGroupEntry = ReadInt(rs, "UomGroupEntry"),
                                    UomGroupName = ReadString(rs, "UomGroupName"),
                                    DefaultCountingUomName = ReadString(rs, "DefaultCountingUomName"),
                                    InventoryUomName = ReadString(rs, "InventoryUomName"),
                                    SalesUomName = ReadString(rs, "SalesUomName"),
                                    PurchaseUomName = ReadString(rs, "PurchaseUomName"),
                                });
                            buckets[article] = bucket;
                        }

                        var barcode = ReadString(rs, "Barcode");
                        if (!string.IsNullOrWhiteSpace(barcode))
                        {
                            bucket.Barcodes.Add(new LiquiMolyBarcodeRowDto
                            {
                                Code = barcode,
                                UomCode = ReadString(rs, "BarcodeUomCode"),
                                UomName = ReadString(rs, "BarcodeUomName"),
                                UomEntry = ReadInt(rs, "BarcodeUomEntry"),
                                BaseQtyInGroup = ReadDecimal(rs, "BarcodeUomBaseQtyInGroup"),
                            });
                        }

                        rs.MoveNext();
                    }

                    foreach (var article in articleNumbers)
                    {
                        if (!buckets.TryGetValue(article, out var bucket))
                        {
                            result[article] = BuildNoRowSnapshot(article);
                            continue;
                        }

                        result[article] = FinalizeSnapshot(bucket);
                    }
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
                finally
                {
                    ReleaseComSafely(rs, "Recordset");

                    if (company != null && company.Connected)
                    {
                        try { company.Disconnect(); }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "SapProductBarcodeReader: failed to disconnect SAP company cleanly.");
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
            "SapProductBarcodeReader: loaded SAP barcode snapshots | Requested={Requested} | Returned={Returned}",
            articleNumbers.Count, result.Count);

        return result;
    }

    private void ReleaseComSafely(object? comObject, string objectName)
    {
        if (comObject == null)
            return;

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SapProductBarcodeReader: failed to release COM object {ObjectName}.", objectName);
        }
    }

    private static SapProductBarcodeSnapshot BuildNoRowSnapshot(string articleNumber)
    {
        return new SapProductBarcodeSnapshot(
            articleNumber,
            null,
            false,
            "NO_BARCODE",
            "No SAP barcode rows were found for this item.",
            new List<LiquiMolyBarcodeRowDto>(),
            null);
    }

    private static SapProductBarcodeSnapshot FinalizeSnapshot(SapProductBarcodeAccumulator bucket)
    {
        var barcodes = bucket.Barcodes
            .OrderByDescending(x => string.Equals(x.UomCode, "Unit", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.BaseQtyInGroup ?? decimal.MaxValue)
            .ThenBy(x => GetPackRank(x.UomCode))
            .ThenBy(x => x.UomCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unitRows = barcodes
            .Where(x => string.Equals(x.UomCode, "Unit", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var hasUnit = unitRows.Count > 0;
        LiquiMolyBarcodeRowDto? primary = null;
        var status = "NO_BARCODE";
        string? note = null;

        if (barcodes.Count == 0)
        {
            note = "No SAP barcode rows were found for this item.";
        }
        else if (hasUnit)
        {
            primary = unitRows
                .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .First();
            status = "UNIT_MATCH";

            if (unitRows.Count > 1)
                note = "Multiple Unit barcodes found; selected the first barcode lexicographically.";
        }
        else if (barcodes.Count == 1)
        {
            primary = barcodes[0];
            status = string.IsNullOrWhiteSpace(primary.UomCode)
                ? "FALLBACK_SINGLE_BARCODE"
                : "FALLBACK_SINGLE_NON_UNIT";
            note = $"Unit barcode missing; selected the only available barcode ({primary.UomCode ?? "unknown UoM"}).";
        }
        else
        {
            var minBaseQty = barcodes.Min(x => x.BaseQtyInGroup ?? decimal.MaxValue);
            var smallestPackRows = barcodes
                .Where(x => (x.BaseQtyInGroup ?? decimal.MaxValue) == minBaseQty)
                .ToList();

            primary = smallestPackRows
                .OrderBy(x => GetPackRank(x.UomCode))
                .ThenBy(x => x.UomCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .First();

            if (smallestPackRows.Count == 1)
            {
                status = "FALLBACK_SMALLEST_PACK";
                note = $"Unit barcode missing; selected smallest available pack barcode ({primary.UomCode}, base qty {FormatBaseQty(primary.BaseQtyInGroup)}).";
            }
            else
            {
                status = "AMBIGUOUS_MULTIPLE_NON_UNIT";
                note = $"Unit barcode missing and multiple non-Unit barcodes share the smallest base qty {FormatBaseQty(primary.BaseQtyInGroup)}; selected a deterministic fallback.";
            }
        }

        foreach (var row in barcodes)
        {
            row.IsUnit = string.Equals(row.UomCode, "Unit", StringComparison.OrdinalIgnoreCase);
            row.IsPrimary = primary != null
                && string.Equals(row.Code, primary.Code, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.UomCode, primary.UomCode, StringComparison.OrdinalIgnoreCase);
        }

        return new SapProductBarcodeSnapshot(
            bucket.ArticleNumber,
            primary,
            hasUnit,
            status,
            note,
            barcodes,
            bucket.SapUomInfo);
    }

    private static string ToSqlLiteral(string value)
        => $"'{value.Replace("'", "''")}'";

    private static string? ReadString(Recordset rs, string fieldName)
        => rs.Fields.Item(fieldName).Value?.ToString();

    private static int? ReadInt(Recordset rs, string fieldName)
    {
        var value = rs.Fields.Item(fieldName).Value;
        if (value == null) return null;

        try { return Convert.ToInt32(value); }
        catch { return int.TryParse(value.ToString(), out int parsed) ? parsed : null; }
    }

    private static decimal? ReadDecimal(Recordset rs, string fieldName)
    {
        var value = rs.Fields.Item(fieldName).Value;
        if (value == null) return null;

        try { return Convert.ToDecimal(value); }
        catch { return decimal.TryParse(value.ToString(), out decimal parsed) ? parsed : null; }
    }

    private static int GetPackRank(string? uomCode)
    {
        if (string.IsNullOrWhiteSpace(uomCode))
            return int.MaxValue;

        return PreferredPackRank.TryGetValue(uomCode, out var rank)
            ? rank
            : int.MaxValue - 1;
    }

    private static string FormatBaseQty(decimal? value)
        => value?.ToString("0.###") ?? "unknown";

    private sealed record SapProductBarcodeAccumulator(
        string ArticleNumber,
        string? ItemName,
        LiquiMolySapUomInfoDto? SapUomInfo)
    {
        public List<LiquiMolyBarcodeRowDto> Barcodes { get; } = new();
    }
}

public sealed record SapProductBarcodeSnapshot(
    string ArticleNumber,
    LiquiMolyBarcodeRowDto? PrimaryBarcode,
    bool HasUnitBarcode,
    string ResolutionStatus,
    string? ResolutionNote,
    List<LiquiMolyBarcodeRowDto> Barcodes,
    LiquiMolySapUomInfoDto? SapUomInfo);
