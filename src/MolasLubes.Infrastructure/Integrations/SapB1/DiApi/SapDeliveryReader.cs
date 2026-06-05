using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapDeliveryReader
{
    private const int DeadlockRetryCount = 3;
    private static readonly TimeSpan DeadlockBaseDelay = TimeSpan.FromSeconds(2);

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
    // DELTA (UpdateDate based)
    // =====================================================
    public IEnumerable<SapDeliveryDto> ReadRecentDeliveries(DateTime fromDate)
    {
        _logger.LogInformation(
            "Reading SAP deliveries | UpdateDate >= {FromDate}",
            fromDate);

        var company = _connection.GetConnectedCompany();
        Recordset? rs = null;

        var whereClause = fromDate <= new DateTime(2000, 1, 1)
            ? "1=1"
            : $"d.UpdateDate >= '{fromDate:yyyy-MM-dd}'";

        var sql = $@"
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
    o.U_Odoo_SO_ID          AS OdooParentSalesOrderId,
    l.LineNum,
    l.ItemCode,
    l.Dscription            AS Description,
    l.Quantity,
    l.LineTotal,
    l.GrossBuyPr,
    l.BaseEntry,
    l.BaseLine,
    l.{OdooUdfs.DeliveryMoveId}   AS LineMoveId,
    l.{OdooUdfs.SalesOrderLineId} AS LineSoLineId,
    l.{OdooUdfs.Status}           AS LineStatus,
    l.{OdooUdfs.SyncDir}          AS LineSyncDir,
    l.{OdooUdfs.LastSync}         AS LineLastSync,
    l.{OdooUdfs.ErrorMsg}         AS LineErrorMsg
FROM ODLN d
INNER JOIN DLN1 l ON d.DocEntry = l.DocEntry
LEFT JOIN ORDR o ON l.BaseEntry = o.DocEntry
WHERE {whereClause}
ORDER BY d.DocEntry, l.LineNum";

        try
        {
            rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
            ExecuteWithDeadlockRetry(rs, sql, fromDate);

            SapDeliveryDto? current = null;

            while (!rs.EoF)
            {
                var docEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value);

                if (current == null || current.DocEntry != docEntry)
                {
                    if (current != null)
                        yield return Finalise(current);

                    current = new SapDeliveryDto
                    {
                        DocEntry = docEntry,
                        DocNum = Convert.ToInt32(rs.Fields.Item("DocNum").Value),
                        CardCode = rs.Fields.Item("CardCode").Value.ToString() ?? string.Empty,
                        DocDate = (DateTime)rs.Fields.Item("DocDate").Value,
                        SapUpdateDate = (DateTime)rs.Fields.Item("UpdateDate").Value,
                        IsCancelled = rs.Fields.Item("CANCELED").Value?.ToString() == "Y",
                        OdooDeliveryId = rs.Fields.Item("OdooDeliveryId").Value?.ToString(),
                        OdooParentSalesOrderId = rs.Fields.Item("OdooParentSalesOrderId").Value?.ToString(),
                        OdooStatus = rs.Fields.Item("HdrStatus").Value?.ToString(),
                        OdooSyncDir = rs.Fields.Item("HdrSyncDir").Value?.ToString(),
                        OdooErrorMsg = rs.Fields.Item("HdrErrorMsg").Value?.ToString(),
                        OdooLastSync = TryGetDate(rs.Fields.Item("HdrLastSync").Value),
                    };
                }

                var currentDelivery = current ?? throw new InvalidOperationException(
                    "Delivery aggregation state was not initialized before reading line data.");

                currentDelivery.Lines.Add(new SapDeliveryLineDto
                {
                    LineNum = Convert.ToInt32(rs.Fields.Item("LineNum").Value),
                    ItemCode = rs.Fields.Item("ItemCode").Value?.ToString() ?? string.Empty,
                    Description = rs.Fields.Item("Description").Value?.ToString() ?? string.Empty,
                    Quantity = Convert.ToDecimal(rs.Fields.Item("Quantity").Value),
                    LineTotal = Convert.ToDecimal(rs.Fields.Item("LineTotal").Value),
                    GrossBuyPr = Convert.ToDecimal(rs.Fields.Item("GrossBuyPr").Value),
                    BaseEntry = Convert.ToInt32(rs.Fields.Item("BaseEntry").Value),
                    BaseLine = Convert.ToInt32(rs.Fields.Item("BaseLine").Value),
                    OdooMoveId = rs.Fields.Item("LineMoveId").Value?.ToString(),
                    OdooSalesOrderLineId = rs.Fields.Item("LineSoLineId").Value?.ToString(),
                    OdooStatus = rs.Fields.Item("LineStatus").Value?.ToString(),
                    OdooSyncDir = rs.Fields.Item("LineSyncDir").Value?.ToString(),
                    OdooErrorMsg = rs.Fields.Item("LineErrorMsg").Value?.ToString(),
                    OdooLastSync = TryGetDate(rs.Fields.Item("LineLastSync").Value),
                });

                rs.MoveNext();
            }

            if (current != null)
                yield return Finalise(current);
        }
        finally
        {
            ReleaseComSafely(rs, "Recordset");
        }
    }

    // =====================================================
    // FULL
    // =====================================================
    public IEnumerable<SapDeliveryDto> ReadAllDeliveries()
    {
        _logger.LogInformation("Reading all SAP deliveries");
        return ReadRecentDeliveries(new DateTime(2000, 1, 1));
    }

    // =====================================================
    // HELPERS
    // =====================================================
    private SapDeliveryDto Finalise(SapDeliveryDto dto)
    {
        dto.BaseOrderEntry = dto.Lines.FirstOrDefault()?.BaseEntry ?? 0;
        dto.DeliveredQuantity = dto.Lines.Sum(l => l.Quantity);

        if (string.IsNullOrEmpty(dto.OdooParentSalesOrderId))
        {
            _logger.LogDebug(
                "Delivery has no parent SO Odoo ID | DocEntry={DocEntry} BaseOrderEntry={BaseOrderEntry}",
                dto.DocEntry,
                dto.BaseOrderEntry);
        }

        return dto;
    }

    private void ExecuteWithDeadlockRetry(Recordset rs, string sql, DateTime fromDate)
    {
        COMException? lastDeadlock = null;

        for (var attempt = 1; attempt <= DeadlockRetryCount; attempt++)
        {
            try
            {
                rs.DoQuery(sql);
                return;
            }
            catch (COMException ex) when (IsDeadlockVictim(ex))
            {
                lastDeadlock = ex;
                if (attempt == DeadlockRetryCount)
                    break;

                var delay = TimeSpan.FromMilliseconds(
                    DeadlockBaseDelay.TotalMilliseconds * attempt
                    + Random.Shared.Next(150, 600));

                _logger.LogWarning(
                    ex,
                    "SapDeliveryReader deadlock while reading deliveries, retrying | Attempt={Attempt}/{MaxAttempts} | DelayMs={DelayMs} | FromDate={FromDate}",
                    attempt,
                    DeadlockRetryCount,
                    delay.TotalMilliseconds,
                    fromDate);

                Thread.Sleep(delay);
            }
        }

        throw lastDeadlock is not null
            ? lastDeadlock
            : new InvalidOperationException("Delivery query retry failed without a captured deadlock exception.");
    }

    private static bool IsDeadlockVictim(COMException ex) =>
        ex.Message.Contains("deadlocked on lock resources", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("deadlock victim", StringComparison.OrdinalIgnoreCase);

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
            _logger.LogWarning(ex, "SapDeliveryReader: failed to release COM object {ObjectName}.", objectName);
        }
    }

    private static DateTime? TryGetDate(object? value)
    {
        if (value == null)
            return null;

        if (value is DateTime dt)
            return dt;

        return DateTime.TryParse(value.ToString(), out var parsed)
            ? parsed
            : null;
    }
}
