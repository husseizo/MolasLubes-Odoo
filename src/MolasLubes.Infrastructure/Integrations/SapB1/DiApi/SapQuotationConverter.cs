using SAPbobsCOM;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Reads open SAP Quotations (OQUT) and converts them to Sales Orders (ORDR)
/// via the DI API CopyFrom mechanism.
/// </summary>
public class SapQuotationConverter
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapQuotationConverter> _logger;

    public SapQuotationConverter(
        SapDiApiConnection connection,
        ILogger<SapQuotationConverter> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // =====================================================
    // READ OPEN QUOTATIONS (OQUT DocStatus = 'O')
    // =====================================================
    public IEnumerable<SapQuotationDto> ReadOpenQuotations()
    {
        _logger.LogInformation("Reading SAP OPEN Quotations (OQUT)");

        var company    = _connection.GetConnectedCompany();
        var quotations = (Documents)company.GetBusinessObject(BoObjectTypes.oQuotations);
        var rs         = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        rs.DoQuery(@"
SELECT DocEntry
FROM OQUT
WHERE DocStatus = 'O'
  AND Cancelled = 'N'
ORDER BY DocEntry
");

        while (!rs.EoF)
        {
            var docEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value);

            if (!quotations.GetByKey(docEntry))
            {
                rs.MoveNext();
                continue;
            }

            yield return new SapQuotationDto
            {
                DocEntry   = quotations.DocEntry,
                DocNum     = quotations.DocNum,
                CardCode   = quotations.CardCode,
                CardName   = quotations.CardName,
                DocDate    = quotations.DocDate,
                DocTotal   = (decimal)quotations.DocTotal,
                DocStatus  = "O",
                UpdateDate = quotations.UpdateDate,
            };

            rs.MoveNext();
        }
    }

    // =====================================================
    // CONVERT QUOTATION → SALES ORDER (CopyFrom)
    // =====================================================
    public (int DocEntry, int DocNum) ConvertToSalesOrder(int quotationDocEntry)
    {
        _logger.LogInformation(
            "Converting Quotation DocEntry={DocEntry} to Sales Order via CopyFrom",
            quotationDocEntry);

        var company = _connection.GetConnectedCompany();
        var order   = (Documents)company.GetBusinessObject(BoObjectTypes.oOrders);

        order.CopyFrom(
            quotationDocEntry,
            BoObjectTypes.oQuotations,
            BoCopyDigitGroups.boCopyDigitGroups_No);

        int rc = order.Add();

        if (rc != 0)
        {
            company.GetLastError(out int code, out string msg);
            throw new Exception(
                $"CopyFrom OQUT {quotationDocEntry} to ORDR failed ({code}): {msg}");
        }

        int docEntry = int.Parse(company.GetNewObjectKey());

        order.GetByKey(docEntry);
        int docNum = order.DocNum;

        _logger.LogInformation(
            "Sales Order created from Quotation | OQUT DocEntry={QuotDocEntry} | ORDR DocEntry={OrderDocEntry} | DocNum={DocNum}",
            quotationDocEntry,
            docEntry,
            docNum);

        return (docEntry, docNum);
    }
}
