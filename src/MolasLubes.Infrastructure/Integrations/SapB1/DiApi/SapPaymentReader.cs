using SAPbobsCOM;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapPaymentReader
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapPaymentReader> _logger;

    private static readonly DateTime SqlMinDate = new(1753, 1, 1);
    private static readonly DateTime DefaultBaseline = new(2000, 1, 1);

    public SapPaymentReader(
        SapDiApiConnection connection,
        ILogger<SapPaymentReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // =====================================================
    // READ PAYMENTS (DELTA SAFE)
    // =====================================================
    public IEnumerable<SapPaymentDto> ReadPayments(DateTime fromDate)
    {
        var safeDate = NormalizeDate(fromDate);
        var sqlDate = safeDate.ToString("yyyy-MM-dd");

        _logger.LogInformation(
            "💰 Reading SAP payments | FromDate={FromDate}",
            safeDate);

        var company = _connection.GetConnectedCompany();
        var payment = (Payments)company.GetBusinessObject(BoObjectTypes.oIncomingPayments);
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        rs.DoQuery($@"
            SELECT DocEntry
            FROM ORCT
            WHERE DocDate >= '{sqlDate}'
            ORDER BY DocEntry
        ");

        while (!rs.EoF)
        {
            var docEntry = Convert.ToInt32(rs.Fields.Item("DocEntry").Value);

            if (!payment.GetByKey(docEntry))
            {
                rs.MoveNext();
                continue;
            }

            // =====================
            // HEADER
            // =====================
            decimal checkSum = 0m;

            for (int i = 0; i < payment.Checks.Count; i++)
            {
                payment.Checks.SetCurrentLine(i);
                checkSum += Convert.ToDecimal(payment.Checks.CheckSum);
            }

            var dto = new SapPaymentDto
            {
                DocEntry = payment.DocEntry,
                DocNum = payment.DocNum,
                CardCode = payment.CardCode,
                DocDate = payment.DocDate,
                CashSum = Convert.ToDecimal(payment.CashSum),
                TransferSum = Convert.ToDecimal(payment.TransferSum),
                CheckSum = checkSum,

                // 🔗 Odoo UDFs
                OdooPaymentId = GetUdfString(payment.UserFields, OdooUdfs.PaymentId),
                OdooStatus = GetUdfString(payment.UserFields, OdooUdfs.Status),
                OdooSyncDir = GetUdfString(payment.UserFields, OdooUdfs.SyncDir),
                OdooErrorMsg = GetUdfString(payment.UserFields, OdooUdfs.ErrorMsg),
                OdooLastSync = TryGetDate(
                    payment.UserFields.Fields.Item(OdooUdfs.LastSync).Value)
            };

            // =====================
            // INVOICE LINES (RCT2)
            // =====================
            for (int i = 0; i < payment.Invoices.Count; i++)
            {
                payment.Invoices.SetCurrentLine(i);

                dto.Invoices.Add(new SapPaymentInvoiceDto
                {
                    InvoiceDocEntry = payment.Invoices.DocEntry,
                    SumApplied = Convert.ToDecimal(payment.Invoices.SumApplied),
                    OdooPaymentLineId =
    GetLineUdfString(payment.Invoices.UserFields, OdooUdfs.PaymentLineId)
                });
            }

            yield return dto;
            rs.MoveNext();
        }
    }



    private static string? GetLineUdfString(UserFields ufs, string field)
    {
        try
        {
            return ufs.Fields.Item(field).Value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    // =====================================================
    // READ ALL (SAFE BASELINE)
    // =====================================================
    public IEnumerable<SapPaymentDto> ReadAllPayments()
    {
        _logger.LogInformation("💰 Reading ALL SAP payments");

        return ReadPayments(DefaultBaseline);
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private static DateTime NormalizeDate(DateTime input)
    {
        if (input < SqlMinDate)
            return DefaultBaseline;

        return input;
    }

    private static string? GetUdfString(UserFields ufs, string field)
    {
        try
        {
            return ufs.Fields.Item(field).Value?.ToString();
        }
        catch
        {
            return null;
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