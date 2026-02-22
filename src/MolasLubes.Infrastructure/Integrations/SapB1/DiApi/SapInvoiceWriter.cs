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
    private readonly SapCustomerReader _customerReader;
    private readonly ILogger<SapInvoiceWriter> _logger;

    public SapInvoiceWriter(
        SapDiApiConnection conn,
        IdempotencyService idempo,
        SapCustomerReader customerReader,
        ILogger<SapInvoiceWriter> logger)
    {
        _conn = conn;
        _idempo = idempo;
        _customerReader = customerReader;
        _logger = logger;
    }

    // =====================================================
    // CREATE INVOICE
    // ── Delivery-based: DeliveryDocEntry > 0
    // ── Standalone:     DeliveryDocEntry = 0, Lines provided
    // =====================================================
    public (int DocEntry, int DocNum, bool AlreadyExists) CreateInvoice(CreateInvoiceDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.CardCode)) throw new ArgumentException("CardCode is required");

        bool isDeliveryBased = dto.DeliveryDocEntry > 0;
        bool isStandalone = !isDeliveryBased;

        if (isStandalone && (dto.Lines == null || dto.Lines.Count == 0))
            throw new ArgumentException("Either DeliveryDocEntry or Lines must be provided");

        // Validate customer is active in SAP
        _customerReader.ValidateCardCodeActive(dto.CardCode);

        // Idempotency: only applicable for delivery-based invoices
        if (isDeliveryBased)
        {
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
        }

        var company = _conn.GetConnectedCompany();
        var invoice = (Documents)company.GetBusinessObject(BoObjectTypes.oInvoices);

        try
        {
            ApplyHeader(invoice, dto);

            if (isDeliveryBased)
                AddDeliveryLines(company, invoice, dto.DeliveryDocEntry);
            else
                AddStandaloneLines(invoice, dto.Lines!);

            var rc = invoice.Add();
            if (rc != 0)
            {
                company.GetLastError(out var code, out var msg);
                throw SapErrorTranslator.Translate(msg, code);
            }

            var docEntry = int.Parse(company.GetNewObjectKey());
            invoice.GetByKey(docEntry);

            _logger.LogInformation(
                "✅ Invoice created | {Mode} | DocEntry={DocEntry} | DocNum={DocNum}",
                isDeliveryBased ? $"Delivery={dto.DeliveryDocEntry}" : "Standalone",
                docEntry, invoice.DocNum);

            return (docEntry, invoice.DocNum, false);
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

    // =====================================================
    // CANCEL INVOICE
    // =====================================================
    public void CancelInvoice(int sapDocEntry)
    {
        if (sapDocEntry <= 0) throw new ArgumentException("SapDocEntry is required");

        var company = _conn.GetConnectedCompany();
        var invoice = (Documents)company.GetBusinessObject(BoObjectTypes.oInvoices);

        if (!invoice.GetByKey(sapDocEntry))
            throw new ArgumentException($"Invoice {sapDocEntry} not found in SAP");

        var rc = invoice.Cancel();
        if (rc != 0)
        {
            company.GetLastError(out var code, out var msg);
            throw SapErrorTranslator.Translate(msg, code);
        }

        _logger.LogInformation("✅ Invoice cancelled | DocEntry={DocEntry}", sapDocEntry);
    }

    // ── Private helpers ──────────────────────────────────

    private static void ApplyHeader(Documents invoice, CreateInvoiceDto dto)
    {
        invoice.CardCode = dto.CardCode.Trim();
        invoice.DocDate = dto.DocDate;
        invoice.TaxDate = dto.DocDate;
        invoice.DocDueDate = dto.DocDueDate ?? dto.DocDate;

        if (dto.PaymentGroupCode.HasValue)
            invoice.PaymentGroupCode = dto.PaymentGroupCode.Value;

        if (!string.IsNullOrWhiteSpace(dto.Comments))
            invoice.Comments = dto.Comments.Trim();

        if (!string.IsNullOrWhiteSpace(dto.NumAtCard))
            invoice.NumAtCard = dto.NumAtCard.Trim();

        // UDFs
        if (dto.DeliveryDocEntry > 0)
            OdooUdfMapper.ApplyInvoiceUdfs(invoice, dto.DeliveryDocEntry, dto.OdooInvoiceId);
        else if (!string.IsNullOrWhiteSpace(dto.OdooInvoiceId))
            invoice.UserFields.Fields.Item("U_Odoo_Invoice_ID").Value = dto.OdooInvoiceId.Trim();
    }

    private static void AddDeliveryLines(Company company, Documents invoice, int deliveryDocEntry)
    {
        var delivery = (Documents)company.GetBusinessObject(BoObjectTypes.oDeliveryNotes);
        if (!delivery.GetByKey(deliveryDocEntry))
            throw new ArgumentException($"Delivery {deliveryDocEntry} not found in SAP");

        for (int i = 0; i < delivery.Lines.Count; i++)
        {
            delivery.Lines.SetCurrentLine(i);

            invoice.Lines.BaseType = (int)BoObjectTypes.oDeliveryNotes;
            invoice.Lines.BaseEntry = deliveryDocEntry;
            invoice.Lines.BaseLine = i;
            invoice.Lines.Add();
        }
    }

    private static void AddStandaloneLines(Documents invoice, List<CreateInvoiceLineDto> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line.ItemCode))
                throw new ArgumentException($"Line {i}: ItemCode is required");
            if (line.Quantity <= 0)
                throw new ArgumentException($"Line {i}: Quantity must be > 0");
            if (line.UnitPrice < 0)
                throw new ArgumentException($"Line {i}: UnitPrice must be >= 0");

            invoice.Lines.ItemCode = line.ItemCode.Trim();
            invoice.Lines.Quantity = (double)line.Quantity;
            invoice.Lines.UnitPrice = (double)line.UnitPrice;

            if (!string.IsNullOrWhiteSpace(line.Description))
                invoice.Lines.ItemDescription = line.Description.Trim();

            if (!string.IsNullOrWhiteSpace(line.WarehouseCode))
                invoice.Lines.WarehouseCode = line.WarehouseCode.Trim();

            if (!string.IsNullOrWhiteSpace(line.TaxCode))
                invoice.Lines.TaxCode = line.TaxCode.Trim();

            if (i < lines.Count - 1)
                invoice.Lines.Add();
        }
    }
}
