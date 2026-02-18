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

                dto.Lines.Add(new SapInvoiceLineDto
                {
                    ItemCode = invoices.Lines.ItemCode,
                    Quantity = (decimal)invoices.Lines.Quantity,
                    LineTotal = (decimal)invoices.Lines.LineTotal,
                    BaseEntry = invoices.Lines.BaseEntry,
                    BaseLine = invoices.Lines.BaseLine,

                    // 🔗 ODOO LINE UDF
                    OdooInvoiceLineId = invoices.Lines.UserFields.Fields
                        .Item("U_Odoo_InvLine_ID").Value?.ToString()
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