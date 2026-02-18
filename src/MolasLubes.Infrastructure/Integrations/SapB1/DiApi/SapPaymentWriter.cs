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
    private readonly ILogger<SapPaymentWriter> _logger;

    public SapPaymentWriter(
        SapDiApiConnection conn,
        IdempotencyService idempo,
        ILogger<SapPaymentWriter> logger)
    {
        _conn = conn;
        _idempo = idempo;
        _logger = logger;
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