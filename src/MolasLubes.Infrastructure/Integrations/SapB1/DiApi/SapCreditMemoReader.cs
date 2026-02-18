using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapCreditMemoReader
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapCreditMemoReader> _logger;

    public SapCreditMemoReader(
        SapDiApiConnection connection,
        ILogger<SapCreditMemoReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public IEnumerable<SapCreditMemoDto> ReadCreditMemos(DateTime fromDate)
    {
        _logger.LogInformation(
            "📄 Reading Credit Memos (ORIN) from SAP since {FromDate}",
            fromDate);

        var company = _connection.GetConnectedCompany();
        var credit = (Documents)company.GetBusinessObject(BoObjectTypes.oCreditNotes);

        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        rs.DoQuery($@"
SELECT DocEntry
FROM ORIN
WHERE DocDate >= '{fromDate:yyyyMMdd}'
ORDER BY DocEntry
");

        while (!rs.EoF)
        {
            var docEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value);

            if (!credit.GetByKey(docEntry))
            {
                rs.MoveNext();
                continue;
            }

            var dto = new SapCreditMemoDto
            {
                DocEntry = credit.DocEntry,
                DocNum = credit.DocNum,
                CardCode = credit.CardCode,
                CardName = credit.CardName,
                DocDate = credit.DocDate,
                DocTotal = (decimal)credit.DocTotal,
                VatSum = (decimal)credit.VatSum,

                // 🔗 ODOO header UDFs (ORIN)
                OdooCreditId = credit.UserFields.Fields.Item(OdooUdfs.CreditNoteId).Value?.ToString(),
                OdooStatus = credit.UserFields.Fields.Item(OdooUdfs.Status).Value?.ToString(),
                OdooSyncDir = credit.UserFields.Fields.Item(OdooUdfs.SyncDir).Value?.ToString(),
                OdooErrorMsg = credit.UserFields.Fields.Item(OdooUdfs.ErrorMsg).Value?.ToString(),
                OdooLastSync = TryGetDate(credit.UserFields.Fields.Item(OdooUdfs.LastSync).Value)
            };

            for (int i = 0; i < credit.Lines.Count; i++)
            {
                credit.Lines.SetCurrentLine(i);

                dto.Lines.Add(new SapCreditMemoLineDto
                {
                    ItemCode = credit.Lines.ItemCode,
                    Quantity = (decimal)credit.Lines.Quantity,
                    LineTotal = (decimal)credit.Lines.LineTotal,

                    BaseType = credit.Lines.BaseType,
                    BaseEntry = credit.Lines.BaseEntry,
                    BaseLine = credit.Lines.BaseLine
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
}