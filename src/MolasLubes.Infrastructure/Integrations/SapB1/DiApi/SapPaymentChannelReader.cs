using Microsoft.Extensions.Logging;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapPaymentChannelReader
{
    private readonly SapDiApiConnection _conn;
    private readonly ILogger<SapPaymentChannelReader> _logger;

    public SapPaymentChannelReader(
        SapDiApiConnection conn,
        ILogger<SapPaymentChannelReader> logger)
    {
        _conn = conn;
        _logger = logger;
    }

    /// <summary>
    /// Returns available payment channels:
    /// - BankAccounts: G/L accounts usable for bank transfers (AccType = 'C', account level 3)
    /// - CreditCards: credit card master records from OCRC
    /// </summary>
    public PaymentChannelsDto ReadPaymentChannels()
    {
        _logger.LogInformation("💳 Reading payment channels from SAP");

        var company = _conn.GetConnectedCompany();
        var result = new PaymentChannelsDto();

        // -------------------------------------------------
        // Bank/transfer accounts: query checking accounts
        // -------------------------------------------------
        var rsBank = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
        try
        {
            rsBank.DoQuery(@"
SELECT AcctCode, AcctName
FROM OACT
WHERE AccType = 'C'
  AND Postable = 'Y'
  AND Frozen = 'N'
ORDER BY AcctCode
");

            while (!rsBank.EoF)
            {
                result.BankAccounts.Add(new PaymentChannelItemDto
                {
                    Code = rsBank.Fields.Item("AcctCode").Value?.ToString() ?? "",
                    Name = rsBank.Fields.Item("AcctName").Value?.ToString() ?? ""
                });
                rsBank.MoveNext();
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(rsBank);
        }

        // -------------------------------------------------
        // Credit card types: OCRC
        // -------------------------------------------------
        var rsCard = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
        try
        {
            rsCard.DoQuery(@"
SELECT CardCode, CardName, GLAccount
FROM OCRC
ORDER BY CardCode
");

            while (!rsCard.EoF)
            {
                result.CreditCards.Add(new CreditCardChannelDto
                {
                    Code = rsCard.Fields.Item("CardCode").Value?.ToString() ?? "",
                    Name = rsCard.Fields.Item("CardName").Value?.ToString() ?? "",
                    GlAccount = rsCard.Fields.Item("GLAccount").Value?.ToString()
                });
                rsCard.MoveNext();
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(rsCard);
        }

        _logger.LogInformation(
            "✅ Payment channels loaded | BankAccounts={B} | CreditCards={C}",
            result.BankAccounts.Count, result.CreditCards.Count);

        return result;
    }
}

public class PaymentChannelsDto
{
    public List<PaymentChannelItemDto> BankAccounts { get; set; } = new();
    public List<CreditCardChannelDto> CreditCards { get; set; } = new();
}

public class PaymentChannelItemDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public class CreditCardChannelDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? GlAccount { get; set; }
}
