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

        string whereClause;

        // FULL sync safe guard
        if (fromDate <= new DateTime(2000, 1, 1))
        {
            whereClause = "1=1";
        }
        else
        {
            whereClause = $"d.UpdateDate >= '{fromDate:yyyy-MM-dd}'";
        }

        var query = $@"
SELECT
    d.DocEntry,
    d.DocNum,
    d.CardCode,
    d.DocDate,
    d.UpdateDate,
    d.CANCELED,

    d.{OdooUdfs.Status}     AS OdooStatus,
    d.{OdooUdfs.SyncDir}    AS OdooSyncDir,
    d.{OdooUdfs.LastSync}   AS OdooLastSync,
    d.{OdooUdfs.ErrorMsg}   AS OdooErrorMsg,
    d.U_Odoo_Delivery_ID    AS OdooDeliveryId,

    l.BaseEntry,
    l.{OdooUdfs.SalesOrderLineId} AS OdooSalesOrderLineId,
    l.U_Odoo_Move_ID        AS OdooMoveId,
    SUM(l.Quantity)         AS Qty

FROM ODLN d
INNER JOIN DLN1 l ON d.DocEntry = l.DocEntry
WHERE {whereClause}
GROUP BY
    d.DocEntry,
    d.DocNum,
    d.CardCode,
    d.DocDate,
    d.UpdateDate,
    d.CANCELED,
    d.{OdooUdfs.Status},
    d.{OdooUdfs.SyncDir},
    d.{OdooUdfs.LastSync},
    d.{OdooUdfs.ErrorMsg},
    d.U_Odoo_Delivery_ID,
    l.BaseEntry,
    l.{OdooUdfs.SalesOrderLineId},
    l.U_Odoo_Move_ID
ORDER BY d.DocEntry
";

        rs.DoQuery(query);

        while (!rs.EoF)
        {
            yield return new SapDeliveryDto
            {
                DocEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value),
                DocNum = Convert.ToInt32(rs.Fields.Item("DocNum").Value),
                CardCode = rs.Fields.Item("CardCode").Value.ToString()!,
                DocDate = (DateTime)rs.Fields.Item("DocDate").Value,

                SapUpdateDate = (DateTime)rs.Fields.Item("UpdateDate").Value,

                IsCancelled =
                    rs.Fields.Item("CANCELED").Value?.ToString() == "Y",

                BaseOrderEntry =
                    Convert.ToInt32(rs.Fields.Item("BaseEntry").Value),

                DeliveredQuantity =
                    Convert.ToDecimal(rs.Fields.Item("Qty").Value),

                // 🔗 ODOO HEADER
                OdooDeliveryId =
                    rs.Fields.Item("OdooDeliveryId").Value?.ToString(),

                OdooStatus =
                    rs.Fields.Item("OdooStatus").Value?.ToString(),

                OdooSyncDir =
                    rs.Fields.Item("OdooSyncDir").Value?.ToString(),

                OdooErrorMsg =
                    rs.Fields.Item("OdooErrorMsg").Value?.ToString(),

                OdooLastSync =
                    TryGetDate(rs.Fields.Item("OdooLastSync").Value),

                // 🔗 ODOO LINE
                OdooMoveId =
                    rs.Fields.Item("OdooMoveId").Value?.ToString(),

                OdooSalesOrderLineId =
                    rs.Fields.Item("OdooSalesOrderLineId").Value?.ToString()
            };

            rs.MoveNext();
        }
    }

    // =====================================================
    // 🔥 FULL
    // =====================================================
    public IEnumerable<SapDeliveryDto> ReadAllDeliveries()
    {
        _logger.LogInformation("📦 Reading ALL SAP deliveries");
        return ReadRecentDeliveries(new DateTime(2000, 1, 1));
    }

    private static DateTime? TryGetDate(object? v)
    {
        if (v == null) return null;
        if (v is DateTime dt) return dt;
        if (DateTime.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }
}