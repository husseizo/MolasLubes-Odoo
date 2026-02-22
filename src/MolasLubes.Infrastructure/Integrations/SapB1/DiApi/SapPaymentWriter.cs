using SAPbobsCOM;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.Payments;
using MolasLubes.Infrastructure.Integrations.SapB1.Errors;
using MolasLubes.Infrastructure.Integrations.SapB1.Helpers;
using MolasLubes.Infrastructure.Integrations.SapB1.Idempotency;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapPaymentWriter
{
    private readonly SapDiApiConnection _conn;
    private readonly IdempotencyService _idempo;
    private readonly SapCustomerReader _customerReader;
    private readonly ILogger<SapPaymentWriter> _logger;

    public SapPaymentWriter(
        SapDiApiConnection conn,
        IdempotencyService idempo,
        SapCustomerReader customerReader,
        ILogger<SapPaymentWriter> logger)
    {
        _conn = conn;
        _idempo = idempo;
        _customerReader = customerReader;
        _logger = logger;
    }

    // =====================================================
    // MULTI-INVOICE PAYMENT (new — replaces simple Create)
    // Validates: CashSum + TransferSum + CardSum == sum of invoice SumApplied
    // =====================================================
    public (int DocEntry, int DocNum, bool AlreadyExists) CreateIncomingPaymentMulti(CreateIncomingPaymentDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.CardCode)) throw new ArgumentException("CardCode is required");
        if (dto.Invoices == null || dto.Invoices.Count == 0) throw new ArgumentException("At least one invoice is required");
        if (dto.Invoices.Any(i => i.InvoiceDocEntry <= 0)) throw new ArgumentException("All InvoiceDocEntry values must be > 0");
        if (dto.Invoices.Any(i => i.SumApplied <= 0)) throw new ArgumentException("All SumApplied values must be > 0");

        var totalApplied = dto.Invoices.Sum(i => i.SumApplied);
        var totalMeans = dto.CashSum + dto.TransferSum + dto.CardSum;
        if (Math.Abs(totalMeans - totalApplied) > 0.01m)
            throw new ArgumentException(
                $"Payment means total ({totalMeans:0.00}) must equal sum of applied invoice amounts ({totalApplied:0.00})");

        if (dto.TransferSum > 0 && string.IsNullOrWhiteSpace(dto.TransferAccount))
            throw new ArgumentException("TransferAccount is required when TransferSum > 0");
        if (dto.CardSum > 0 && string.IsNullOrWhiteSpace(dto.CardName))
            throw new ArgumentException("CardName is required when CardSum > 0");

        // Validate customer is active in SAP
        _customerReader.ValidateCardCodeActive(dto.CardCode);

        var docDate = dto.DocDate ?? DateTime.Today;
        var counterRef = IdempotencyKeys.MultiPaymentCounterRef(
            dto.Invoices.Select(i => i.InvoiceDocEntry), totalApplied);

        var existing = _idempo.FindPaymentByCounterReference(counterRef);
        if (existing.HasValue)
        {
            var company0 = _conn.GetConnectedCompany();
            var pay0 = (Payments)company0.GetBusinessObject(BoObjectTypes.oIncomingPayments);
            pay0.GetByKey(existing.Value);

            _logger.LogInformation(
                "♻️ Multi-payment already exists | Ref={Ref} | PayEntry={Entry}/{Num}",
                counterRef, existing.Value, pay0.DocNum);

            return (existing.Value, pay0.DocNum, true);
        }

        var company = _conn.GetConnectedCompany();
        var pay = (Payments)company.GetBusinessObject(BoObjectTypes.oIncomingPayments);

        try
        {
            pay.CardCode = dto.CardCode.Trim();
            pay.DocDate = docDate;
            pay.DueDate = docDate;
            pay.TaxDate = docDate;
            pay.DocCurrency = string.IsNullOrWhiteSpace(dto.Currency) ? "TZS" : dto.Currency.Trim();
            pay.CounterReference = counterRef;

            if (!string.IsNullOrWhiteSpace(dto.ExternalPaymentId))
                pay.Reference1 = dto.ExternalPaymentId.Trim();

            // Apply each invoice
            foreach (var inv in dto.Invoices)
            {
                pay.Invoices.InvoiceType = BoRcptInvTypes.it_Invoice;
                pay.Invoices.DocEntry = inv.InvoiceDocEntry;
                pay.Invoices.SumApplied = (double)inv.SumApplied;
                pay.Invoices.Add();
            }

            // Payment means
            if (dto.CashSum > 0)
                pay.CashSum = (double)dto.CashSum;

            if (dto.TransferSum > 0)
            {
                pay.TransferSum = (double)dto.TransferSum;
                pay.TransferAccount = dto.TransferAccount!.Trim();
                pay.TransferDate = docDate;
                if (!string.IsNullOrWhiteSpace(dto.TransferReference))
                    pay.TransferReference = dto.TransferReference.Trim();
            }

            if (dto.CardSum > 0)
            {
                pay.CreditCards.CreditCardCode = dto.CardName!.Trim();
                pay.CreditCards.CreditSum = (double)dto.CardSum;
                pay.CreditCards.CardValidUntil = docDate;
                pay.CreditCards.Add();
            }

            // UDF: link to first invoice + Odoo payment ID
            OdooUdfMapper.ApplyPaymentUdfs(pay, dto.Invoices[0].InvoiceDocEntry, dto.ExternalPaymentId, dto.SyncDir);

            var rc = pay.Add();
            if (rc != 0)
            {
                company.GetLastError(out var code, out var msg);
                throw SapErrorTranslator.Translate(msg, code);
            }

            var docEntry = int.Parse(company.GetNewObjectKey());
            pay.GetByKey(docEntry);

            _logger.LogInformation(
                "✅ Multi-payment posted | Invoices={InvCount} | PayEntry={Entry} | PayNum={Num} | Ref={Ref}",
                dto.Invoices.Count, docEntry, pay.DocNum, counterRef);

            return (docEntry, pay.DocNum, false);
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

    public (int DocEntry, int DocNum, bool AlreadyExists) CreateIncomingPayment(CreatePaymentDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (dto.InvoiceDocEntry <= 0) throw new ArgumentException("InvoiceDocEntry is required");
        if (string.IsNullOrWhiteSpace(dto.CardCode)) throw new ArgumentException("CardCode is required");
        if (dto.Amount <= 0) throw new ArgumentException("Amount must be > 0");

        // idempotency: CounterReference
        var counterRef = IdempotencyKeys.PaymentCounterRef(dto.InvoiceDocEntry, dto.Amount);
        var existing = _idempo.FindPaymentByCounterReference(counterRef);
        if (existing.HasValue)
        {
            var company0 = _conn.GetConnectedCompany();
            var pay0 = (Payments)company0.GetBusinessObject(BoObjectTypes.oIncomingPayments);
            pay0.GetByKey(existing.Value);

            _logger.LogInformation(
                "♻️ Payment already exists | Invoice={Inv} | Payment={PayEntry}/{PayNum} | Ref={Ref}",
                dto.InvoiceDocEntry, existing.Value, pay0.DocNum, counterRef);

            return (existing.Value, pay0.DocNum, true);
        }

        var company = _conn.GetConnectedCompany();
        var pay = (Payments)company.GetBusinessObject(BoObjectTypes.oIncomingPayments);

        try
        {
            pay.CardCode = dto.CardCode.Trim();
            pay.DocDate = dto.DocDate;
            pay.DueDate = dto.DocDate;
            pay.TaxDate = dto.DocDate;
            pay.DocCurrency = string.IsNullOrWhiteSpace(dto.Currency) ? "TZS" : dto.Currency.Trim();

            // idempotency anchor in SAP
            pay.CounterReference = counterRef;

            if (!string.IsNullOrWhiteSpace(dto.ExternalRef))
                pay.Reference1 = dto.ExternalRef.Trim();

            // Apply to invoice
            pay.Invoices.InvoiceType = BoRcptInvTypes.it_Invoice;
            pay.Invoices.DocEntry = dto.InvoiceDocEntry;
            pay.Invoices.SumApplied = (double)dto.Amount;
            pay.Invoices.Add();

            // Means
            if (dto.CashAmount > 0)
                pay.CashSum = (double)dto.CashAmount;

            if (dto.TransferAmount > 0)
            {
                if (string.IsNullOrWhiteSpace(dto.TransferAccount))
                    throw new ArgumentException("TransferAccount is required when TransferAmount > 0");

                pay.TransferSum = (double)dto.TransferAmount;
                pay.TransferAccount = dto.TransferAccount.Trim();
                pay.TransferDate = dto.DocDate;
            }

            if (dto.CardAmount > 0)
            {
                pay.CreditCards.CreditSum = (double)dto.CardAmount;
                pay.CreditCards.CardValidUntil = dto.DocDate;
                pay.CreditCards.Add();
            }

            // UDFs
            OdooUdfMapper.ApplyPaymentUdfs(pay, dto.InvoiceDocEntry);

            var rc = pay.Add();
            if (rc != 0)
            {
                company.GetLastError(out var code, out var msg);
                throw SapErrorTranslator.Translate(msg, code);
            }

            var docEntry = int.Parse(company.GetNewObjectKey());
            pay.GetByKey(docEntry);

            _logger.LogInformation(
                "✅ Payment posted | Invoice={Inv} | PayEntry={Entry} | PayNum={Num} | Ref={Ref}",
                dto.InvoiceDocEntry, docEntry, pay.DocNum, counterRef);

            return (docEntry, pay.DocNum, false);
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
}