using SAPbobsCOM;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.Invoices;
using MolasLubes.Infrastructure.Integrations.SapB1.Errors;
using MolasLubes.Infrastructure.Integrations.SapB1.Helpers;
using MolasLubes.Infrastructure.Integrations.SapB1.Idempotency;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapInvoiceWriter
{
    private readonly SapDiApiConnection _conn;
    private readonly IdempotencyService _idempo;
    private readonly ILogger<SapInvoiceWriter> _logger;

    public SapInvoiceWriter(
        SapDiApiConnection conn,
        IdempotencyService idempo,
        ILogger<SapInvoiceWriter> logger)
    {
        _conn = conn;
        _idempo = idempo;
        _logger = logger;
    }

    // =====================================================
    // CREATE INVOICE (Delivery → Invoice)
    // =====================================================
    public (int DocEntry, int DocNum, bool AlreadyExists) CreateInvoice(CreateInvoiceDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (dto.DeliveryDocEntry <= 0) throw new ArgumentException("DeliveryDocEntry is required");
        if (string.IsNullOrWhiteSpace(dto.CardCode)) throw new ArgumentException("CardCode is required");

        // ✅ Idempotency: invoice already created from this delivery?
        var existing = _idempo.FindInvoiceBySourceDelivery(dto.DeliveryDocEntry);
        if (existing.HasValue)
        {
            var company0 = _conn.GetConnectedCompany();
            var inv0 = (Documents)company0.GetBusinessObject(BoObjectTypes.oInvoices);
            inv0.GetByKey(existing.Value);

            _logger.LogInformation(
                "♻️ Invoice already exists for delivery | Delivery={Delivery} | Invoice={InvEntry}/{InvNum}",
                dto.DeliveryDocEntry, existing.Value, inv0.DocNum);

            return (existing.Value, inv0.DocNum, true);
        }

        var company = _conn.GetConnectedCompany();
        var invoice = (Documents)company.GetBusinessObject(BoObjectTypes.oInvoices);

        try
        {
            // Header
            invoice.CardCode = dto.CardCode.Trim();
            invoice.DocDate = dto.DocDate;
            invoice.TaxDate = dto.DocDate;
            invoice.DocDueDate = dto.DocDate;

            if (!string.IsNullOrWhiteSpace(dto.NumAtCard))
                invoice.NumAtCard = dto.NumAtCard.Trim();

            // UDFs (idempotency marker + odoo fields)
            OdooUdfMapper.ApplyInvoiceUdfs(invoice, dto.DeliveryDocEntry);

            // Lines must be copied strictly from delivery
            invoice.Lines.BaseType = (int)BoObjectTypes.oDeliveryNotes; // 15
            invoice.Lines.BaseEntry = dto.DeliveryDocEntry;
            invoice.Lines.BaseLine = 0;   // DI API will copy lines when you add multiple via loop
            // If you want full correctness for many lines, you usually loop delivery lines count.
            // But most implementations let SAP copy with BaseEntry and add for each line index.

            // safer: load delivery lines count then add each base line
            var delivery = (Documents)company.GetBusinessObject(BoObjectTypes.oDeliveryNotes);
            if (!delivery.GetByKey(dto.DeliveryDocEntry))
                throw new Exception($"Delivery not found in SAP: {dto.DeliveryDocEntry}");

            for (int i = 0; i < delivery.Lines.Count; i++)
            {
                delivery.Lines.SetCurrentLine(i);

                invoice.Lines.BaseType = (int)BoObjectTypes.oDeliveryNotes;
                invoice.Lines.BaseEntry = dto.DeliveryDocEntry;
                invoice.Lines.BaseLine = i;

                invoice.Lines.Add();
            }

            var rc = invoice.Add();
            if (rc != 0)
            {
                company.GetLastError(out var code, out var msg);
                throw SapErrorTranslator.Translate(msg, code);
            }

            var docEntry = int.Parse(company.GetNewObjectKey());
            invoice.GetByKey(docEntry);

            _logger.LogInformation(
                "✅ Invoice created | Delivery={Delivery} | DocEntry={DocEntry} | DocNum={DocNum}",
                dto.DeliveryDocEntry, docEntry, invoice.DocNum);

            return (docEntry, invoice.DocNum, false);
        }
        catch (SapIntegrationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // If SAP gave last error, convert it
            company.GetLastError(out var code, out var msg);
            if (code != 0)
                throw SapErrorTranslator.Translate(msg, code, ex);

            throw;
        }
    }
}