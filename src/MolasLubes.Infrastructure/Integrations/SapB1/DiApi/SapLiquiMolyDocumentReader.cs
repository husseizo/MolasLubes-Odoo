#pragma warning disable CA1416

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Infrastructure.Integrations.SapB1.Profiles;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapLiquiMolyDocumentReader
{
    private readonly IntegrationProfilesOptions _profiles;
    private readonly ILogger<SapLiquiMolyDocumentReader> _logger;

    public SapLiquiMolyDocumentReader(
        IOptions<IntegrationProfilesOptions> profileOptions,
        ILogger<SapLiquiMolyDocumentReader> logger)
    {
        _profiles = profileOptions.Value;
        _logger = logger;
    }

    public LiquiMolyDocumentDetails? GetDocument(string docType, int docEntry, string? profileOverride = null)
    {
        if (docEntry <= 0)
            throw new ArgumentOutOfRangeException(nameof(docEntry), "docEntry must be greater than zero.");

        var normalized = NormalizeDocType(docType);
        var candidates = GetCandidates(normalized, profileOverride);

        foreach (var candidate in candidates)
        {
            var found = TryReadCandidate(candidate, docEntry);
            if (found != null)
                return found;
        }

        return null;
    }

    public LiquiMolyDocumentLine? GetLine(string docType, int docEntry, int lineNum, string? profileOverride = null)
    {
        if (docEntry <= 0)
            throw new ArgumentOutOfRangeException(nameof(docEntry), "docEntry must be greater than zero.");
        if (lineNum < 0)
            throw new ArgumentOutOfRangeException(nameof(lineNum), "lineNum must be >= 0.");

        var document = GetDocument(docType, docEntry, profileOverride);
        return document?.Lines.FirstOrDefault(l => l.LineNum == lineNum);
    }

    private LiquiMolyDocumentDetails? TryReadCandidate(DocumentCandidate candidate, int docEntry)
    {
        if (!_profiles.Profiles.TryGetValue(candidate.ProfileKey, out var profile))
            throw new InvalidOperationException($"Profile '{candidate.ProfileKey}' not configured.");

        LiquiMolyDocumentDetails? result = null;
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            SapDiApiCriticalSection.Run(() =>
            {
                Company? company = null;
                Recordset? headerRs = null;
                Recordset? lineRs = null;

                try
                {
                    company = CreateAndConnect(profile.Sap);

                    headerRs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                    headerRs.DoQuery(BuildHeaderSql(candidate, docEntry));

                    if (headerRs.EoF)
                        return;

                    var header = new LiquiMolyDocumentHeader
                    {
                        DocEntry = docEntry,
                        DocNum = ReadString(headerRs, "DocNum") ?? docEntry.ToString(),
                        DocType = candidate.DocType,
                        SapDocType = candidate.HeaderTable,
                        DocDate = ReadDate(headerRs, "DocDate"),
                        DueDate = ReadDate(headerRs, "DueDate"),
                        DocumentStatus = ReadString(headerRs, "DocumentStatus"),
                        Total = ReadDecimal(headerRs, "Total"),
                        CardCode = ReadString(headerRs, "CardCode"),
                        CardName = ReadString(headerRs, "CardName"),
                        PartnerCode = ReadString(headerRs, "PartnerCode"),
                        PartnerName = ReadString(headerRs, "PartnerName"),
                        CustomerCode = ReadString(headerRs, "CustomerCode"),
                        CustomerName = ReadString(headerRs, "CustomerName"),
                        VendorCode = ReadString(headerRs, "VendorCode"),
                        VendorName = ReadString(headerRs, "VendorName"),
                        FromWarehouse = ReadString(headerRs, "FromWarehouse"),
                        ToWarehouse = ReadString(headerRs, "ToWarehouse"),
                        CreatedBy = ReadString(headerRs, "CreatedBy"),
                        LastModifiedDate = ReadDate(headerRs, "LastModifiedDate"),
                        Remarks = ReadString(headerRs, "Comments"),
                        SalesPersonCode = ReadString(headerRs, "SalesPersonCode"),
                        SalesPersonName = ReadString(headerRs, "SalesPersonName")
                    };

                    lineRs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
                    lineRs.DoQuery(BuildLineSql(candidate, docEntry));

                    var lines = new List<LiquiMolyDocumentLine>();
                    while (!lineRs.EoF)
                    {
                        lines.Add(new LiquiMolyDocumentLine
                        {
                            LineNum = ReadInt(lineRs, "LineNum"),
                            ItemCode = ReadString(lineRs, "ItemCode") ?? string.Empty,
                            ItemName = ReadString(lineRs, "ItemName"),
                            ArticleNumber = ReadString(lineRs, "ArticleNumber"),
                            Quantity = ReadDecimal(lineRs, "Quantity"),
                            UnitOfMeasure = ReadString(lineRs, "UnitOfMeasure"),
                            UnitPrice = ReadNullableDecimal(lineRs, "UnitPrice"),
                            LineTotal = ReadNullableDecimal(lineRs, "LineTotal"),
                            Warehouse = ReadString(lineRs, "Warehouse"),
                            FromWarehouse = ReadString(lineRs, "FromWarehouse"),
                            ToWarehouse = ReadString(lineRs, "ToWarehouse"),
                            LineStatus = ReadString(lineRs, "LineStatus"),
                            Brand = ReadString(lineRs, "Brand"),
                            TanNumber = ReadString(lineRs, "TanNumber"),
                            ProductionPartNumber = ReadString(lineRs, "ProductionPartNumber"),
                            OpenQty = ReadNullableDecimal(lineRs, "OpenQty"),
                            Batch = null,
                            SerialNumber = null
                        });

                        lineRs.MoveNext();
                    }

                    header.LineCount = lines.Count;

                    result = new LiquiMolyDocumentDetails
                    {
                        Header = header,
                        Lines = lines
                    };

                    _logger.LogInformation(
                        "SapLiquiMolyDocumentReader: document loaded | DocType={DocType} | DocEntry={DocEntry} | Profile={Profile} | SourceTable={Table} | Lines={Lines}",
                        candidate.DocType, docEntry, candidate.ProfileKey, candidate.HeaderTable, lines.Count);
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
                finally
                {
                    if (lineRs != null) Marshal.ReleaseComObject(lineRs);
                    if (headerRs != null) Marshal.ReleaseComObject(headerRs);
                    DisconnectAndRelease(company);
                }
            });
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw threadException;

        return result;
    }

    private static string NormalizeDocType(string docType)
    {
        if (string.IsNullOrWhiteSpace(docType))
            throw new ArgumentException("docType is required.", nameof(docType));

        var normalized = docType.Trim().ToUpperInvariant();
        return normalized switch
        {
            "SO" or "ORDR" or
            "DLV" or "ODLN" or
            "TRQ" or "OWTQ" or
            "TRF" or "OWTR" or
            "INC" or "OINC" or
            "IP" or "OIQR" or
            "GR" or "OIGN" or "OPDN" or
            "GI" or "OIGE" or
            "PO" or "OPOR" => normalized,
            _ => throw new ArgumentException(
                $"Unsupported docType '{docType}'. Supported values: SO/ORDR, DLV/ODLN, TRQ/OWTQ, TRF/OWTR, INC/OINC, IP/OIQR, GR/OIGN, GI/OIGE, PO/OPOR.",
                nameof(docType))
        };
    }

    private static IReadOnlyList<DocumentCandidate> GetCandidates(string docType, string? profileOverride = null) => docType switch
    {
        "SO" or "ORDR" => new[]
        {
            new DocumentCandidate("SO", profileOverride ?? "MolasLubes", "ORDR", "RDR1", true, true, PartnerRole.Customer)
        },
        "DLV" or "ODLN" => new[]
        {
            new DocumentCandidate("DLV", profileOverride ?? "MolasLubes", "ODLN", "DLN1", true, true, PartnerRole.Customer)
        },
        "TRQ" or "OWTQ" => new[]
        {
            new DocumentCandidate("TRQ", profileOverride ?? "MolasLubes", "OWTQ", "WTQ1", true, false, PartnerRole.None)
        },
        "TRF" or "OWTR" => new[]
        {
            new DocumentCandidate("TRF", profileOverride ?? "MolasLubes", "OWTR", "WTR1", true, false, PartnerRole.None)
        },
        "INC" or "OINC" => new[]
        {
            new DocumentCandidate("INC", profileOverride ?? "MolasLubes", "OINC", "INC1", false, false, PartnerRole.None)
        },
        "IP" or "OIQR" => new[]
        {
            new DocumentCandidate("IP", profileOverride ?? "MolasLubes", "OIQR", "IQR1", false, false, PartnerRole.None)
        },
        "PO" or "OPOR" => new[]
        {
            new DocumentCandidate("PO", profileOverride ?? "AutoHub", "OPOR", "POR1", true, true, PartnerRole.Vendor)
        },
        "GI" or "OIGE" => new[]
        {
            new DocumentCandidate("GI", profileOverride ?? "MolasLubes", "OIGE", "IGE1", false, false, PartnerRole.None)
        },
        "GR" => new[]
        {
            new DocumentCandidate("GR", profileOverride ?? "AutoHub", "OPDN", "PDN1", true, true, PartnerRole.Vendor),
            new DocumentCandidate("GR", profileOverride ?? "AutoHub", "OIGN", "IGN1", false, false, PartnerRole.None)
        },
        "OPDN" => new[]
        {
            new DocumentCandidate("GR", profileOverride ?? "AutoHub", "OPDN", "PDN1", true, true, PartnerRole.Vendor)
        },
        "OIGN" => new[]
        {
            new DocumentCandidate("GR", profileOverride ?? "AutoHub", "OIGN", "IGN1", false, false, PartnerRole.None)
        },
        _ => Array.Empty<DocumentCandidate>()
    };

    private static string BuildHeaderSql(DocumentCandidate candidate, int docEntry)
    {
        var (salesPersonSelect, salesPersonJoin) = BuildHeaderSalesPersonSql(candidate);

        if (candidate.HeaderTable == "OINC")
        {
            return $@"
SELECT TOP 1
    CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
    COALESCE(h.CountDate, h.PostDate, h.CreateDate) AS DocDate,
    CAST(NULL AS DATETIME) AS DueDate,
    CASE
        WHEN h.Status = 'C' THEN 'CLOSED'
        WHEN h.Status = 'O' THEN 'OPEN'
        ELSE ISNULL(NULLIF(h.Status, ''), 'POSTED')
    END AS DocumentStatus,
    CONVERT(DECIMAL(19, 6), 0) AS Total,
    CAST(NULL AS NVARCHAR(50)) AS CardCode,
    CAST(NULL AS NVARCHAR(200)) AS CardName,
    CAST(NULL AS NVARCHAR(50)) AS PartnerCode,
    CAST(NULL AS NVARCHAR(200)) AS PartnerName,
    CAST(NULL AS NVARCHAR(50)) AS CustomerCode,
    CAST(NULL AS NVARCHAR(200)) AS CustomerName,
    CAST(NULL AS NVARCHAR(50)) AS VendorCode,
    CAST(NULL AS NVARCHAR(200)) AS VendorName,
    CAST(NULL AS NVARCHAR(8)) AS FromWarehouse,
    (
        SELECT TOP 1 NULLIF(x.WhsCode, '')
        FROM {candidate.LineTable} x
        WHERE x.DocEntry = h.DocEntry
        ORDER BY x.LineNum
    ) AS ToWarehouse,
    u.USER_CODE AS CreatedBy,
    h.UpdateDate AS LastModifiedDate,
    h.Remarks AS Comments,
    CAST(NULL AS NVARCHAR(10)) AS SalesPersonCode,
    CAST(NULL AS NVARCHAR(100)) AS SalesPersonName
FROM {candidate.HeaderTable} h
LEFT JOIN OUSR u ON u.USERID = h.UserSign
WHERE h.DocEntry = {docEntry}";
        }

        if (candidate.HeaderTable == "OIQR")
        {
            return $@"
SELECT TOP 1
    CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
    COALESCE(h.DocDate, h.CountDate, h.CreateDate) AS DocDate,
    h.DocDueDate AS DueDate,
    CASE
        WHEN h.Status = 'C' THEN 'CLOSED'
        WHEN h.Status = 'O' THEN 'OPEN'
        ELSE ISNULL(NULLIF(h.Status, ''), 'POSTED')
    END AS DocumentStatus,
    CONVERT(DECIMAL(19, 6), ISNULL(h.DocTotal, 0)) AS Total,
    CAST(NULL AS NVARCHAR(50)) AS CardCode,
    CAST(NULL AS NVARCHAR(200)) AS CardName,
    CAST(NULL AS NVARCHAR(50)) AS PartnerCode,
    CAST(NULL AS NVARCHAR(200)) AS PartnerName,
    CAST(NULL AS NVARCHAR(50)) AS CustomerCode,
    CAST(NULL AS NVARCHAR(200)) AS CustomerName,
    CAST(NULL AS NVARCHAR(50)) AS VendorCode,
    CAST(NULL AS NVARCHAR(200)) AS VendorName,
    CAST(NULL AS NVARCHAR(8)) AS FromWarehouse,
    CAST(NULL AS NVARCHAR(8)) AS ToWarehouse,
    u.USER_CODE AS CreatedBy,
    h.UpdateDate AS LastModifiedDate,
    h.Comments AS Comments,
    CAST(NULL AS NVARCHAR(10)) AS SalesPersonCode,
    CAST(NULL AS NVARCHAR(100)) AS SalesPersonName
FROM {candidate.HeaderTable} h
LEFT JOIN OUSR u ON u.USERID = h.UserSign
WHERE h.DocEntry = {docEntry}";
        }

        var partnerSql = BuildHeaderPartnerSql(candidate);
        var warehouseSql = BuildHeaderWarehouseSql(candidate);
        var totalSql = candidate.HasPricing
            ? "CONVERT(DECIMAL(19, 6), ISNULL(h.DocTotal, 0))"
            : "CONVERT(DECIMAL(19, 6), 0)";

        if (candidate.SupportsMarketingStatus)
        {
            return $@"
SELECT TOP 1
    CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
    h.DocDate AS DocDate,
    h.DocDueDate AS DueDate,
    CASE
        WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
        WHEN h.DocStatus = 'C' THEN 'CLOSED'
        ELSE 'OPEN'
    END AS DocumentStatus,
    {totalSql} AS Total,
    {partnerSql},
    {warehouseSql},
    u.USER_CODE AS CreatedBy,
    h.UpdateDate AS LastModifiedDate,
    h.Comments AS Comments,
    {salesPersonSelect}
FROM {candidate.HeaderTable} h
LEFT JOIN OUSR u ON u.USERID = h.UserSign
{salesPersonJoin}
WHERE h.DocEntry = {docEntry}";
        }

        return $@"
SELECT TOP 1
    CAST(h.DocNum AS NVARCHAR(50)) AS DocNum,
    h.DocDate AS DocDate,
    CAST(NULL AS DATETIME) AS DueDate,
    CASE
        WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
        ELSE 'POSTED'
    END AS DocumentStatus,
    {totalSql} AS Total,
    {partnerSql},
    {warehouseSql},
    u.USER_CODE AS CreatedBy,
    h.UpdateDate AS LastModifiedDate,
    h.Comments AS Comments,
    {salesPersonSelect}
FROM {candidate.HeaderTable} h
LEFT JOIN OUSR u ON u.USERID = h.UserSign
{salesPersonJoin}
WHERE h.DocEntry = {docEntry}";
    }

    private static string BuildLineSql(DocumentCandidate candidate, int docEntry)
    {
        var metaSql = BuildLineMetadataSql(candidate);
        var lineDescriptionSql = BuildLineItemNameSql(candidate, "l.Dscription", "i.ItemName");
        var countLineDescriptionSql = BuildLineItemNameSql(candidate, "l.ItemDesc", "i.ItemName");
        var postingLineDescriptionSql = BuildLineItemNameSql(candidate, "l.ItemName", "i.ItemName");
        var openQtySql = candidate.HeaderTable == "ORDR"
            ? "CONVERT(DECIMAL(19, 6), ISNULL(l.OpenQty, 0))"
            : "CAST(NULL AS DECIMAL(19, 6))";

        if (candidate.HeaderTable == "OINC")
        {
            return $@"
SELECT
    l.LineNum AS LineNum,
    l.ItemCode AS ItemCode,
    {countLineDescriptionSql} AS ItemName,
    {metaSql},
    CONVERT(DECIMAL(19, 6), ISNULL(l.CountQty, 0)) AS Quantity,
    NULLIF(l.UomCode, '') AS UnitOfMeasure,
    CAST(NULL AS DECIMAL(19, 6)) AS UnitPrice,
    CAST(NULL AS DECIMAL(19, 6)) AS LineTotal,
    NULLIF(l.WhsCode, '') AS Warehouse,
    CAST(NULL AS NVARCHAR(8)) AS FromWarehouse,
    NULLIF(l.WhsCode, '') AS ToWarehouse,
    CASE
        WHEN h.Status = 'C' OR l.LineStatus = 'C' THEN 'CLOSED'
        WHEN h.Status = 'O' OR l.LineStatus = 'O' THEN 'OPEN'
        ELSE ISNULL(NULLIF(l.LineStatus, ''), ISNULL(NULLIF(h.Status, ''), 'POSTED'))
    END AS LineStatus,
    CAST(NULL AS DECIMAL(19, 6)) AS OpenQty
FROM {candidate.LineTable} l
INNER JOIN {candidate.HeaderTable} h ON h.DocEntry = l.DocEntry
LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
WHERE l.DocEntry = {docEntry}
ORDER BY l.LineNum";
        }

        if (candidate.HeaderTable == "OIQR")
        {
            return $@"
SELECT
    l.DocLineNum AS LineNum,
    l.ItemCode AS ItemCode,
    {postingLineDescriptionSql} AS ItemName,
    {metaSql},
    CONVERT(DECIMAL(19, 6), ABS(ISNULL(l.Quantity, 0))) AS Quantity,
    NULLIF(l.UomCode, '') AS UnitOfMeasure,
    CONVERT(DECIMAL(19, 6), ISNULL(l.Price, 0)) AS UnitPrice,
    CONVERT(DECIMAL(19, 6), ISNULL(l.DocTotal, 0)) AS LineTotal,
    NULLIF(l.WhsCode, '') AS Warehouse,
    CASE WHEN ISNULL(l.Quantity, 0) < 0 THEN NULLIF(l.WhsCode, '') ELSE CAST(NULL AS NVARCHAR(8)) END AS FromWarehouse,
    CASE WHEN ISNULL(l.Quantity, 0) > 0 THEN NULLIF(l.WhsCode, '') ELSE CAST(NULL AS NVARCHAR(8)) END AS ToWarehouse,
    CASE
        WHEN h.Status = 'C' THEN 'CLOSED'
        WHEN h.Status = 'O' THEN 'OPEN'
        ELSE ISNULL(NULLIF(h.Status, ''), 'POSTED')
    END AS LineStatus,
    CAST(NULL AS DECIMAL(19, 6)) AS OpenQty
FROM {candidate.LineTable} l
INNER JOIN {candidate.HeaderTable} h ON h.DocEntry = l.DocEntry
LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
WHERE l.DocEntry = {docEntry}
ORDER BY l.DocLineNum";
        }

        var unitPriceSql = candidate.HasPricing
            ? "CONVERT(DECIMAL(19, 6), ISNULL(l.Price, 0))"
            : "CAST(NULL AS DECIMAL(19, 6))";
        var lineTotalSql = candidate.HasPricing
            ? "CONVERT(DECIMAL(19, 6), ISNULL(l.LineTotal, 0))"
            : "CAST(NULL AS DECIMAL(19, 6))";
        var fromWarehouseSql = candidate.HeaderTable is "OWTQ" or "OWTR"
            ? "NULLIF(l.FromWhsCod, '')"
            : candidate.DocType == "GI"
                ? "NULLIF(l.WhsCode, '')"
                : "CAST(NULL AS NVARCHAR(8))";
        var toWarehouseSql = candidate.HeaderTable is "OWTQ" or "OWTR"
            ? "NULLIF(l.WhsCode, '')"
            : candidate.DocType == "GR"
                ? "NULLIF(l.WhsCode, '')"
                : "CAST(NULL AS NVARCHAR(8))";
        var lineStatusSql = candidate.SupportsMarketingStatus
            ? @"CASE
        WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
        WHEN ISNULL(l.LineStatus, h.DocStatus) = 'C' THEN 'CLOSED'
        ELSE 'OPEN'
    END"
            : @"CASE
        WHEN ISNULL(h.CANCELED, 'N') = 'Y' THEN 'CANCELLED'
        ELSE 'POSTED'
    END";

        return $@"
SELECT
    l.LineNum AS LineNum,
    l.ItemCode AS ItemCode,
    {lineDescriptionSql} AS ItemName,
    {metaSql},
    CONVERT(DECIMAL(19, 6), ISNULL(l.Quantity, 0)) AS Quantity,
    NULLIF(l.unitMsr, '') AS UnitOfMeasure,
    {unitPriceSql} AS UnitPrice,
    {lineTotalSql} AS LineTotal,
    NULLIF(l.WhsCode, '') AS Warehouse,
    {fromWarehouseSql} AS FromWarehouse,
    {toWarehouseSql} AS ToWarehouse,
    {lineStatusSql} AS LineStatus,
    {openQtySql} AS OpenQty
FROM {candidate.LineTable} l
INNER JOIN {candidate.HeaderTable} h ON h.DocEntry = l.DocEntry
LEFT JOIN OITM i ON i.ItemCode = l.ItemCode
WHERE l.DocEntry = {docEntry}
ORDER BY l.LineNum";
    }

    private static string BuildHeaderPartnerSql(DocumentCandidate candidate) => candidate.PartnerRole switch
    {
        PartnerRole.Customer => @"
    h.CardCode AS CardCode,
    h.CardName AS CardName,
    h.CardCode AS PartnerCode,
    h.CardName AS PartnerName,
    h.CardCode AS CustomerCode,
    h.CardName AS CustomerName,
    CAST(NULL AS NVARCHAR(50)) AS VendorCode,
    CAST(NULL AS NVARCHAR(200)) AS VendorName",
        PartnerRole.Vendor => @"
    h.CardCode AS CardCode,
    h.CardName AS CardName,
    h.CardCode AS PartnerCode,
    h.CardName AS PartnerName,
    CAST(NULL AS NVARCHAR(50)) AS CustomerCode,
    CAST(NULL AS NVARCHAR(200)) AS CustomerName,
    h.CardCode AS VendorCode,
    h.CardName AS VendorName",
        _ => @"
    CAST(NULL AS NVARCHAR(50)) AS CardCode,
    CAST(NULL AS NVARCHAR(200)) AS CardName,
    CAST(NULL AS NVARCHAR(50)) AS PartnerCode,
    CAST(NULL AS NVARCHAR(200)) AS PartnerName,
    CAST(NULL AS NVARCHAR(50)) AS CustomerCode,
    CAST(NULL AS NVARCHAR(200)) AS CustomerName,
    CAST(NULL AS NVARCHAR(50)) AS VendorCode,
    CAST(NULL AS NVARCHAR(200)) AS VendorName"
    };

    private static string BuildHeaderWarehouseSql(DocumentCandidate candidate) => candidate.HeaderTable switch
    {
        "OWTQ" or "OWTR" => $@"
    (
        SELECT TOP 1 NULLIF(x.FromWhsCod, '')
        FROM {candidate.LineTable} x
        WHERE x.DocEntry = h.DocEntry
        ORDER BY x.LineNum
    ) AS FromWarehouse,
    (
        SELECT TOP 1 NULLIF(x.WhsCode, '')
        FROM {candidate.LineTable} x
        WHERE x.DocEntry = h.DocEntry
        ORDER BY x.LineNum
    ) AS ToWarehouse",
        "OIGN" => $@"
    CAST(NULL AS NVARCHAR(8)) AS FromWarehouse,
    (
        SELECT TOP 1 NULLIF(x.WhsCode, '')
        FROM {candidate.LineTable} x
        WHERE x.DocEntry = h.DocEntry
        ORDER BY x.LineNum
    ) AS ToWarehouse",
        "OIGE" => $@"
    (
        SELECT TOP 1 NULLIF(x.WhsCode, '')
        FROM {candidate.LineTable} x
        WHERE x.DocEntry = h.DocEntry
        ORDER BY x.LineNum
    ) AS FromWarehouse,
    CAST(NULL AS NVARCHAR(8)) AS ToWarehouse",
        _ => @"
    CAST(NULL AS NVARCHAR(8)) AS FromWarehouse,
    CAST(NULL AS NVARCHAR(8)) AS ToWarehouse"
    };

    private static (string SelectSql, string JoinSql) BuildHeaderSalesPersonSql(DocumentCandidate candidate)
    {
        if (candidate.PartnerRole == PartnerRole.None)
            return (
                @"CAST(NULL AS NVARCHAR(10)) AS SalesPersonCode,
    CAST(NULL AS NVARCHAR(100)) AS SalesPersonName",
                ""
            );

        return (
            @"CASE WHEN ISNULL(h.SlpCode, -1) = -1 THEN NULL ELSE CAST(h.SlpCode AS NVARCHAR(10)) END AS SalesPersonCode,
    COALESCE(NULLIF(sl.SlpName, ''), NULL) AS SalesPersonName",
            "LEFT JOIN OSLP sl ON sl.SlpCode = h.SlpCode"
        );
    }

    private static string BuildLineMetadataSql(DocumentCandidate candidate) =>
        candidate.ProfileKey == "AutoHub"
            ? @"COALESCE(NULLIF(i.U_Article_No, ''), NULLIF(l.ItemCode, '')) AS ArticleNumber,
    COALESCE(NULLIF(i.U_MdlTEST, ''), NULL) AS Brand,
    NULLIF(i.U_Engine_Code, '') AS TanNumber,
    NULLIF(i.U_PT_No_Inproduction, '') AS ProductionPartNumber"
            : @"NULLIF(l.ItemCode, '') AS ArticleNumber,
    CAST('Liqui Moly' AS NVARCHAR(100)) AS Brand,
    CAST(NULL AS NVARCHAR(100)) AS TanNumber,
    CAST(NULL AS NVARCHAR(100)) AS ProductionPartNumber";

    private static string BuildLineItemNameSql(DocumentCandidate candidate, string sourceExpression, string fallbackExpression) =>
        candidate.ProfileKey == "AutoHub"
            ? $"COALESCE(NULLIF(i.U_Item_Name, ''), NULLIF({sourceExpression}, ''), {fallbackExpression})"
            : $"COALESCE(NULLIF({sourceExpression}, ''), {fallbackExpression})";

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
        try
        {
            if (company.Connected)
                company.Disconnect();
        }
        catch
        {
        }

        Marshal.ReleaseComObject(company);
    }

    private static string? ReadString(Recordset rs, string fieldName)
    {
        var value = rs.Fields.Item(fieldName).Value;
        var text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int ReadInt(Recordset rs, string fieldName) =>
        Convert.ToInt32(rs.Fields.Item(fieldName).Value);

    private static decimal ReadDecimal(Recordset rs, string fieldName) =>
        Convert.ToDecimal(rs.Fields.Item(fieldName).Value ?? 0m);

    private static decimal? ReadNullableDecimal(Recordset rs, string fieldName)
    {
        var value = rs.Fields.Item(fieldName).Value;
        if (value == null || value is DBNull)
            return null;

        return Convert.ToDecimal(value);
    }

    private static DateTime? ReadDate(Recordset rs, string fieldName)
    {
        var value = rs.Fields.Item(fieldName).Value;
        if (value == null || value is DBNull)
            return null;

        if (value is DateTime dateTime)
            return dateTime;

        DateTime parsed;
        return DateTime.TryParse(value.ToString(), out parsed)
            ? parsed
            : null;
    }

    private sealed record DocumentCandidate(
        string DocType,
        string ProfileKey,
        string HeaderTable,
        string LineTable,
        bool SupportsMarketingStatus,
        bool HasPricing,
        PartnerRole PartnerRole);

    private enum PartnerRole
    {
        None,
        Customer,
        Vendor
    }
}

public class LiquiMolyDocumentDetails
{
    public LiquiMolyDocumentHeader Header { get; init; } = new();
    public List<LiquiMolyDocumentLine> Lines { get; init; } = new();
}

public class LiquiMolyDocumentHeader
{
    public int DocEntry { get; init; }
    public string DocNum { get; init; } = string.Empty;
    public string DocType { get; init; } = string.Empty;
    public string SapDocType { get; init; } = string.Empty;
    public DateTime? DocDate { get; init; }
    public DateTime? DueDate { get; init; }
    public string? DocumentStatus { get; init; }
    public decimal Total { get; init; }
    public int LineCount { get; set; }
    public string? CardCode { get; init; }
    public string? CardName { get; init; }
    public string? PartnerCode { get; init; }
    public string? PartnerName { get; init; }
    public string? CustomerCode { get; init; }
    public string? CustomerName { get; init; }
    public string? VendorCode { get; init; }
    public string? VendorName { get; init; }
    public string? FromWarehouse { get; init; }
    public string? ToWarehouse { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? LastModifiedDate { get; init; }
    public string? Remarks { get; init; }
    public string? Comments => Remarks;
    public string? SalesPersonCode { get; init; }
    public string? SalesPersonName { get; init; }
}

public class LiquiMolyDocumentLine
{
    public int LineNum { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string SapItemCode => ItemCode;
    public string? ItemName { get; init; }
    public string? ArticleNumber { get; init; }
    public decimal Quantity { get; init; }
    public string? UnitOfMeasure { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? Price => UnitPrice;
    public decimal? LineTotal { get; init; }
    public string? Warehouse { get; init; }
    public string? FromWarehouse { get; init; }
    public string? ToWarehouse { get; init; }
    public string? LineStatus { get; init; }
    public string? Brand { get; init; }
    public string? TanNumber { get; init; }
    public string? EngineCode => TanNumber;
    public string? ProductionPartNumber { get; init; }
    public string? PartNumberInProduction => ProductionPartNumber;
    public decimal? OpenQty { get; init; }
    public string? Batch { get; init; }
    public string? SerialNumber { get; init; }
}
