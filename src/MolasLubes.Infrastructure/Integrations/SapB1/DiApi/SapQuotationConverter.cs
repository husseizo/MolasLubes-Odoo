using SAPbobsCOM;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

/// <summary>
/// Reads open SAP Quotations (OQUT) and converts them to Sales Orders (ORDR)
/// via the DI API BaseType/BaseEntry/BaseLine line-copy mechanism.
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
    // CONVERT QUOTATION → SALES ORDER (BaseType/BaseEntry/BaseLine)
    // =====================================================
    public (int DocEntry, int DocNum) ConvertToSalesOrder(int quotationDocEntry)
    {
        _logger.LogInformation(
            "Converting Quotation DocEntry={DocEntry} to Sales Order",
            quotationDocEntry);

        var company   = _connection.GetConnectedCompany();
        var quotation = (Documents)company.GetBusinessObject(BoObjectTypes.oQuotations);

        if (!quotation.GetByKey(quotationDocEntry))
            throw new Exception($"Quotation {quotationDocEntry} not found in SAP");

        var order = (Documents)company.GetBusinessObject(BoObjectTypes.oOrders);

        // Copy header from quotation
        order.CardCode    = quotation.CardCode;
        order.DocDate     = DateTime.Today;
        order.TaxDate     = DateTime.Today;
        order.DocDueDate  = quotation.DocDueDate;
        order.DocCurrency = quotation.DocCurrency;

        if (!string.IsNullOrWhiteSpace(quotation.Comments))
            order.Comments = quotation.Comments;

        // Copy lines via BaseType/BaseEntry/BaseLine.
        // The Documents object starts with one empty line at index 0, so Add()
        // must be called *before* each subsequent line, not after every line.
        // Lines where OpenQuantity == 0 are already fully converted — skip them.
        bool firstLine = true;
        for (int i = 0; i < quotation.Lines.Count; i++)
        {
            quotation.Lines.SetCurrentLine(i);
            if (quotation.Lines.RemainingOpenQuantity == 0)
                continue;

            if (!firstLine)
                order.Lines.Add();

            order.Lines.BaseType  = (int)BoObjectTypes.oQuotations;
            order.Lines.BaseEntry = quotationDocEntry;
            order.Lines.BaseLine  = i;
            firstLine = false;
        }

        int rc = order.Add();

        if (rc != 0)
        {
            company.GetLastError(out int code, out string msg);
            throw new Exception(
                $"OQUT {quotationDocEntry} to ORDR failed ({code}): {msg}");
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
