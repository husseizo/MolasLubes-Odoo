using SAPbobsCOM;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapDeliveryReader
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapDeliveryReader> _logger;

    public SapDeliveryReader(
        SapDiApiConnection connection,
        ILogger<SapDeliveryReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // =====================================================
    // 🔄 DELTA (UpdateDate based)
    // =====================================================
    public IEnumerable<SapDeliveryDto> ReadRecentDeliveries(DateTime fromDate)
    {
        _logger.LogInformation(
            "📦 Reading SAP Deliveries | UpdateDate >= {FromDate}",
            fromDate);

        var company = _connection.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        var whereClause = fromDate <= new DateTime(2000, 1, 1)
            ? "1=1"
            : $"d.UpdateDate >= '{fromDate:yyyy-MM-dd}'";

        // One row per DLN1 line — grouped in memory by DocEntry.
        // Avoids the GROUP BY collapse bug that lost per-line data.
        rs.DoQuery($@"
SELECT
    d.DocEntry,
    d.DocNum,
    d.CardCode,
    d.DocDate,
    d.UpdateDate,
    d.CANCELED,
    d.{OdooUdfs.Status}      AS HdrStatus,
    d.{OdooUdfs.SyncDir}     AS HdrSyncDir,
    d.{OdooUdfs.LastSync}    AS HdrLastSync,
    d.{OdooUdfs.ErrorMsg}    AS HdrErrorMsg,
    d.{OdooUdfs.DeliveryId}  AS OdooDeliveryId,

    l.LineNum,
    l.ItemCode,
    l.Dscription             AS Description,
    l.Quantity,
    l.LineTotal,
    l.GrossBuyPr,
    l.BaseEntry,
    l.BaseLine,
    l.{OdooUdfs.DeliveryMoveId}   AS LineMoveId,
    l.{OdooUdfs.SalesOrderLineId}  AS LineSoLineId,
    l.{OdooUdfs.Status}            AS LineStatus,
    l.{OdooUdfs.SyncDir}           AS LineSyncDir,
    l.{OdooUdfs.LastSync}          AS LineLastSync,
    l.{OdooUdfs.ErrorMsg}          AS LineErrorMsg

FROM ODLN d
INNER JOIN DLN1 l ON d.DocEntry = l.DocEntry
WHERE {whereClause}
ORDER BY d.DocEntry, l.LineNum
");

        SapDeliveryDto? current = null;

        while (!rs.EoF)
        {
            var docEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value);

            // New document — flush previous and start fresh
            if (current == null || current.DocEntry != docEntry)
            {
                if (current != null)
                    yield return Finalise(current);

                current = new SapDeliveryDto
                {
                    DocEntry      = docEntry,
                    DocNum        = Convert.ToInt32(rs.Fields.Item("DocNum").Value),
                    CardCode      = rs.Fields.Item("CardCode").Value.ToString()!,
                    DocDate       = (DateTime)rs.Fields.Item("DocDate").Value,
                    SapUpdateDate = (DateTime)rs.Fields.Item("UpdateDate").Value,
                    IsCancelled   = rs.Fields.Item("CANCELED").Value?.ToString() == "Y",

                    OdooDeliveryId = rs.Fields.Item("OdooDeliveryId").Value?.ToString(),
                    OdooStatus     = rs.Fields.Item("HdrStatus").Value?.ToString(),
                    OdooSyncDir    = rs.Fields.Item("HdrSyncDir").Value?.ToString(),
                    OdooErrorMsg   = rs.Fields.Item("HdrErrorMsg").Value?.ToString(),
                    OdooLastSync   = TryGetDate(rs.Fields.Item("HdrLastSync").Value),
                };
            }

            var currentDelivery = current ?? throw new InvalidOperationException(
                "Delivery aggregation state was not initialized before reading line data.");

            currentDelivery.Lines.Add(new SapDeliveryLineDto
            {
                LineNum     = Convert.ToInt32(rs.Fields.Item("LineNum").Value),
                ItemCode    = rs.Fields.Item("ItemCode").Value?.ToString() ?? "",
                Description = rs.Fields.Item("Description").Value?.ToString() ?? "",
                Quantity    = Convert.ToDecimal(rs.Fields.Item("Quantity").Value),
                LineTotal   = Convert.ToDecimal(rs.Fields.Item("LineTotal").Value),
                GrossBuyPr  = Convert.ToDecimal(rs.Fields.Item("GrossBuyPr").Value),
                BaseEntry   = Convert.ToInt32(rs.Fields.Item("BaseEntry").Value),
                BaseLine    = Convert.ToInt32(rs.Fields.Item("BaseLine").Value),

                OdooMoveId           = rs.Fields.Item("LineMoveId").Value?.ToString(),
                OdooSalesOrderLineId = rs.Fields.Item("LineSoLineId").Value?.ToString(),
                OdooStatus           = rs.Fields.Item("LineStatus").Value?.ToString(),
                OdooSyncDir          = rs.Fields.Item("LineSyncDir").Value?.ToString(),
                OdooErrorMsg         = rs.Fields.Item("LineErrorMsg").Value?.ToString(),
                OdooLastSync         = TryGetDate(rs.Fields.Item("LineLastSync").Value),
            });

            rs.MoveNext();
        }

        // Flush the final delivery
        if (current != null)
            yield return Finalise(current);
    }

    // =====================================================
    // 🔥 FULL
    // =====================================================
    public IEnumerable<SapDeliveryDto> ReadAllDeliveries()
    {
        _logger.LogInformation("📦 Reading ALL SAP deliveries");
        return ReadRecentDeliveries(new DateTime(2000, 1, 1));
    }

    // =====================================================
    // HELPERS
    // =====================================================

    /// <summary>
    /// Computes convenience header fields from accumulated lines before yielding.
    /// </summary>
    private static SapDeliveryDto Finalise(SapDeliveryDto dto)
    {
        dto.BaseOrderEntry    = dto.Lines.FirstOrDefault()?.BaseEntry ?? 0;
        dto.DeliveredQuantity = dto.Lines.Sum(l => l.Quantity);
        return dto;
    }

    private static DateTime? TryGetDate(object? v)
    {
        if (v == null) return null;
        if (v is DateTime dt) return dt;
        if (DateTime.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }
}
