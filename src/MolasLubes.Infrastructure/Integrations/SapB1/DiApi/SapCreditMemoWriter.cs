using SAPbobsCOM;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.Invoices;
using MolasLubes.Infrastructure.Integrations.SapB1.Errors;
using MolasLubes.Infrastructure.Integrations.SapB1.Helpers;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapCreditMemoWriter
{
    private readonly SapDiApiConnection _conn;
    private readonly ILogger<SapCreditMemoWriter> _logger;

    public SapCreditMemoWriter(
        SapDiApiConnection conn,
        ILogger<SapCreditMemoWriter> logger)
    {
        _conn = conn;
        _logger = logger;
    }

    /// <summary>
    /// Creates a credit note (ORIN) based on an existing invoice (OINV).
    /// If dto.Lines is null/empty, all lines from the source invoice are credited.
    /// If dto.Lines is provided, only those BaseLine indexes are credited.
    /// </summary>
    public (int DocEntry, int DocNum) CreateCreditMemo(CreateCreditMemoDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (dto.InvoiceDocEntry <= 0) throw new ArgumentException("InvoiceDocEntry is required");
        if (string.IsNullOrWhiteSpace(dto.CardCode)) throw new ArgumentException("CardCode is required");

        var company = _conn.GetConnectedCompany();

        // Load source invoice to determine line count when crediting all
        var sourceInvoice = (Documents)company.GetBusinessObject(BoObjectTypes.oInvoices);
        if (!sourceInvoice.GetByKey(dto.InvoiceDocEntry))
            throw new ArgumentException($"Invoice {dto.InvoiceDocEntry} not found in SAP");

        var credit = (Documents)company.GetBusinessObject(BoObjectTypes.oCreditNotes);

        try
        {
            credit.CardCode = dto.CardCode.Trim();
            credit.DocDate = dto.DocDate;
            credit.TaxDate = dto.DocDate;
            credit.DocDueDate = dto.DocDate;

            if (!string.IsNullOrWhiteSpace(dto.Comments))
                credit.Comments = dto.Comments.Trim();

            // Determine which lines to credit
            var linesToCredit = BuildLineIndexes(dto, sourceInvoice.Lines.Count);

            foreach (var lineIndex in linesToCredit)
            {
                sourceInvoice.Lines.SetCurrentLine(lineIndex);

                credit.Lines.BaseType = (int)BoObjectTypes.oInvoices; // 13
                credit.Lines.BaseEntry = dto.InvoiceDocEntry;
                credit.Lines.BaseLine = lineIndex;

                // If caller specified a quantity override for this line, apply it
                if (dto.Lines != null)
                {
                    var lineOverride = dto.Lines.FirstOrDefault(l => l.BaseLine == lineIndex);
                    if (lineOverride != null && lineOverride.Quantity > 0)
                        credit.Lines.Quantity = (double)lineOverride.Quantity;
                }

                credit.Lines.Add();
            }

            // UDFs
            OdooUdfMapper.ApplyCreditMemoUdfs(credit, dto.OdooCreditMemoId, "FROM_SAP");

            var rc = credit.Add();
            if (rc != 0)
            {
                company.GetLastError(out var code, out var msg);
                throw SapErrorTranslator.Translate(msg, code);
            }

            var docEntry = int.Parse(company.GetNewObjectKey());
            credit.GetByKey(docEntry);

            _logger.LogInformation(
                "✅ Credit memo created | Invoice={Inv} | CreditEntry={Entry} | CreditNum={Num}",
                dto.InvoiceDocEntry, docEntry, credit.DocNum);

            return (docEntry, credit.DocNum);
        }
        catch (SapIntegrationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            company.GetLastError(out var code, out var msg);
            if (code != 0)
                throw SapErrorTranslator.Translate(msg, code, ex);
            throw;
        }
    }

    private static IEnumerable<int> BuildLineIndexes(CreateCreditMemoDto dto, int totalLines)
    {
        if (dto.Lines == null || dto.Lines.Count == 0)
        {
            // Credit all lines
            return Enumerable.Range(0, totalLines);
        }

        // Validate and deduplicate caller-specified line indexes
        return dto.Lines
            .Select(l => l.BaseLine)
            .Distinct()
            .OrderBy(i => i);
    }
}
