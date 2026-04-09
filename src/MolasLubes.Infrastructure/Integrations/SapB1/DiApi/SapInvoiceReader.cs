using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Integrations.SapB1.Helpers;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapInvoiceReader
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapInvoiceReader> _logger;

    public SapInvoiceReader(
        SapDiApiConnection connection,
        ILogger<SapInvoiceReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public IEnumerable<SapInvoiceDto> ReadInvoices(DateTime fromDate)
    {
        _logger.LogInformation(
            "🧾 Reading SAP invoices | FromDate={FromDate}",
            fromDate);

        var company = _connection.GetConnectedCompany();
        var invoices = (Documents)company.GetBusinessObject(BoObjectTypes.oInvoices);
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
        var sqlDate = SapDateHelper.ToSqlDate(fromDate);

        // Map BaseEntry to parent SO's Odoo ID (for fallback order lookup)
        var invoiceLineParentOrderMap = new Dictionary<(int docEntry, int lineNum), string?>(); 

        // Query to fetch parent SO's U_Odoo_SO_ID for each invoice line
        // Invoice lines can reference: 1) ORDR (sales order) directly, or 2) ODLN (delivery)
        // For ODLN reference, we hop through DLN1 to find the parent ORDR
        rs.DoQuery($@"
SELECT
    iv.DocEntry           AS InvoiceDocEntry,
    ivl.LineNum           AS LineNum,
    COALESCE(
        -- Case 1: BaseEntry is a sales order (ORDR)
        (SELECT U_Odoo_SO_ID FROM ORDR WHERE DocEntry = ivl.BaseEntry),
        -- Case 2: BaseEntry is a delivery (ODLN), get parent SO from first delivery line
        (SELECT TOP 1 o.U_Odoo_SO_ID 
         FROM DLN1 dl 
         JOIN ORDR o ON dl.BaseEntry = o.DocEntry 
         WHERE dl.DocEntry = ivl.BaseEntry)
    ) AS ParentOdooSoId
FROM OINV iv
INNER JOIN INV1 ivl ON iv.DocEntry = ivl.DocEntry
WHERE iv.DocDate >= '{sqlDate}'
");

        while (!rs.EoF)
        {
            var invoiceDocEntry = Convert.ToInt32(rs.Fields.Item("InvoiceDocEntry").Value);
            var lineNum = Convert.ToInt32(rs.Fields.Item("LineNum").Value);
            var parentOdooSoId = rs.Fields.Item("ParentOdooSoId").Value?.ToString();
            invoiceLineParentOrderMap[(invoiceDocEntry, lineNum)] = parentOdooSoId;
            rs.MoveNext();
        }

        // Now query actual invoice headers
        rs.DoQuery($@"
SELECT DocEntry
FROM OINV
WHERE DocDate >= '{sqlDate}'
ORDER BY DocEntry
");

        while (!rs.EoF)
        {
            var docEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value);

            if (!invoices.GetByKey(docEntry))
            {
                rs.MoveNext();
                continue;
            }

            var dto = new SapInvoiceDto
            {
                // SAP HEADER
                DocEntry = invoices.DocEntry,
                DocNum = invoices.DocNum,
                CardCode = invoices.CardCode,
                CardName = invoices.CardName,
                DocDate = invoices.DocDate,
                DocTotal = (decimal)invoices.DocTotal,
                VatSum = (decimal)invoices.VatSum,

                // 🔗 ODOO HEADER UDFS
                OdooInvoiceId = invoices.UserFields.Fields
                    .Item(OdooUdfs.InvoiceId).Value?.ToString(),

                OdooStatus = invoices.UserFields.Fields
                    .Item(OdooUdfs.Status).Value?.ToString(),

                OdooSyncDir = invoices.UserFields.Fields
                    .Item(OdooUdfs.SyncDir).Value?.ToString(),

                OdooErrorMsg = invoices.UserFields.Fields
                    .Item(OdooUdfs.ErrorMsg).Value?.ToString(),

                OdooLastSync = TryGetDate(
                    invoices.UserFields.Fields
                        .Item(OdooUdfs.LastSync).Value)
            };

            // =====================
            // LINES (INV1)
            // =====================
            for (int i = 0; i < invoices.Lines.Count; i++)
            {
                invoices.Lines.SetCurrentLine(i);
                var lineNum = invoices.Lines.LineNum;

                // Lookup parent SO's Odoo ID from pre-fetched map
                invoiceLineParentOrderMap.TryGetValue((docEntry, lineNum), out var parentOdooSoId);

                dto.Lines.Add(new SapInvoiceLineDto
                {
                    ItemCode = invoices.Lines.ItemCode,
                    Description = invoices.Lines.ItemDescription,
                    Quantity = (decimal)invoices.Lines.Quantity,
                    LineTotal = (decimal)invoices.Lines.LineTotal,
                    GrossBuyPr = (decimal)invoices.Lines.GrossBuyPrice,
                    BaseEntry = invoices.Lines.BaseEntry,
                    BaseLine = invoices.Lines.BaseLine,
                    OdooParentSalesOrderId = parentOdooSoId,

                    // 🔗 ODOO LINE UDFs
                    OdooInvoiceLineId = invoices.Lines.UserFields.Fields
                        .Item(OdooUdfs.InvoiceLineId).Value?.ToString(),

                    OdooStatus = invoices.Lines.UserFields.Fields
                        .Item(OdooUdfs.Status).Value?.ToString(),

                    OdooSyncDir = invoices.Lines.UserFields.Fields
                        .Item(OdooUdfs.SyncDir).Value?.ToString(),

                    OdooErrorMsg = invoices.Lines.UserFields.Fields
                        .Item(OdooUdfs.ErrorMsg).Value?.ToString(),

                    OdooLastSync = TryGetDate(
                        invoices.Lines.UserFields.Fields
                            .Item(OdooUdfs.LastSync).Value)
                });
            }

            yield return dto;
            rs.MoveNext();
        }
    }

    private static DateTime? TryGetDate(object? v)
    {
        if (v == null) return null;
        if (v is DateTime dt) return dt;
        if (DateTime.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }



    public IEnumerable<SapInvoiceDto> ReadAllInvoices()
    {
        _logger.LogInformation("🧾 Reading ALL SAP invoices");

        return ReadInvoices(new DateTime(2000, 1, 1));
    }
}