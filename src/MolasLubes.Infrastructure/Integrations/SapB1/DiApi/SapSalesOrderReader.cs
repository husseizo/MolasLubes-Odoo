using SAPbobsCOM;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapSalesOrderReader
{
    private static readonly DateTime SqlMinDate = new DateTime(1753, 1, 1);

    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapSalesOrderReader> _logger;

    public SapSalesOrderReader(
        SapDiApiConnection connection,
        ILogger<SapSalesOrderReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // =====================================================
    // 🔄 DELTA (BASED ON UpdateDate)
    // =====================================================
    public IEnumerable<SapSalesOrderDto> ReadRecentSalesOrders(DateTime fromDate)
    {
        var safeFromDate = fromDate < SqlMinDate
            ? SqlMinDate
            : fromDate;

        _logger.LogInformation(
            "🛒 Reading SAP Sales Orders | FromDate={FromDate}",
            safeFromDate);

        var company = _connection.GetConnectedCompany();
        var orders = (Documents)company.GetBusinessObject(BoObjectTypes.oOrders);
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        // 🔥 IMPORTANT:
        // Use UpdateDate for delta sync (not DocDate)
        rs.DoQuery($@"
SELECT DocEntry
FROM ORDR
WHERE UpdateDate >= '{safeFromDate:yyyyMMdd}'
ORDER BY DocEntry
");

        while (!rs.EoF)
        {
            var docEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value);

            if (!orders.GetByKey(docEntry))
            {
                rs.MoveNext();
                continue;
            }

            var dto = new SapSalesOrderDto
            {
                // =====================
                // HEADER
                // =====================
                DocEntry = orders.DocEntry,
                DocNum = orders.DocNum,
                CardCode = orders.CardCode,
                CardName = orders.CardName,
                DocDate = orders.DocDate,
                DocTotal = (decimal)orders.DocTotal,

                // 🔥 Cancellation / Status Detection
                DocStatus =
                    orders.Cancelled == BoYesNoEnum.tYES ? "X" :
                    orders.DocumentStatus == BoStatus.bost_Close ? "C" :
                    "O",

                // =====================
                // ODOO HEADER UDFS
                // =====================
                OdooSalesOrderId =
                    orders.UserFields.Fields
                        .Item(OdooUdfs.SalesOrderId).Value?.ToString(),

                OdooStatus =
                    orders.UserFields.Fields
                        .Item(OdooUdfs.Status).Value?.ToString(),

                OdooSyncDir =
                    orders.UserFields.Fields
                        .Item(OdooUdfs.SyncDir).Value?.ToString(),

                OdooErrorMsg =
                    orders.UserFields.Fields
                        .Item(OdooUdfs.ErrorMsg).Value?.ToString(),

                OdooLastSync =
                    TryGetDate(
                        orders.UserFields.Fields
                            .Item(OdooUdfs.LastSync).Value)
            };

            // =====================
            // LINES (RDR1)
            // =====================
            for (int i = 0; i < orders.Lines.Count; i++)
            {
                orders.Lines.SetCurrentLine(i);

                dto.Lines.Add(new SapSalesOrderLineDto
                {
                    LineNum = orders.Lines.LineNum,
                    ItemCode = orders.Lines.ItemCode,
                    ItemName = orders.Lines.ItemDescription,
                    Quantity = (decimal)orders.Lines.Quantity,
                    Price = (decimal)orders.Lines.Price,
                    LineTotal = (decimal)orders.Lines.LineTotal,

                    OdooSalesOrderLineId =
                        orders.Lines.UserFields.Fields
                            .Item("U_Odoo_SOLine_ID").Value?.ToString()
                });
            }

            yield return dto;

            rs.MoveNext();
        }
    }

    // =====================================================
    // 🔥 FULL SYNC
    // =====================================================
    public IEnumerable<SapSalesOrderDto> ReadAllSalesOrders()
    {
        _logger.LogInformation("🛒 Reading ALL SAP Sales Orders");

        // Start from safe SQL minimum
        return ReadRecentSalesOrders(SqlMinDate);
    }

    // =====================================================
    // HELPER
    // =====================================================
    private static DateTime? TryGetDate(object? v)
    {
        if (v == null) return null;
        if (v is DateTime dt) return dt;
        if (DateTime.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }
}