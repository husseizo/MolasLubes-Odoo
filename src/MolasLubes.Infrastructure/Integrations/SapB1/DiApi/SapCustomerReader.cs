using Microsoft.Extensions.Logging;
using SAPbobsCOM;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapCustomerReader
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapCustomerReader> _logger;

    public SapCustomerReader(
        SapDiApiConnection connection,
        ILogger<SapCustomerReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // =====================================================
    // 🔄 DELTA READ
    // =====================================================
    public List<SapCustomerDto> ReadCustomersDelta(DateTime sinceUtc)
    {
        _logger.LogInformation("👥 Starting DELTA customer read | SinceUtc={SinceUtc}", sinceUtc);

        var sinceDate = sinceUtc.Date.ToString("yyyyMMdd");
        var customers = new List<SapCustomerDto>();

        var company = _connection.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        try
        {
            rs.DoQuery($@"
SELECT *
FROM OCRD
WHERE CardType = 'C'
AND UpdateDate >= '{sinceDate}'
ORDER BY CardCode
");

            while (!rs.EoF)
            {
                var dto = MapCustomer(rs);
                if (dto != null)
                    customers.Add(dto);

                rs.MoveNext();
            }

            _logger.LogInformation("✅ DELTA completed | Count={Count}", customers.Count);
            return customers;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
        }
    }

    // =====================================================
    // 🔁 BATCH READ
    // =====================================================
    public List<SapCustomerDto> ReadCustomerBatchAfter(string lastCardCode, int batchSize)
    {
        var customers = new List<SapCustomerDto>();
        var safeLast = lastCardCode?.Replace("'", "''");

        var company = _connection.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        try
        {
            string whereClause = string.IsNullOrWhiteSpace(safeLast)
                ? "WHERE CardType = 'C'"
                : $"WHERE CardType = 'C' AND CardCode > '{safeLast}'";

            rs.DoQuery($@"
SELECT TOP {batchSize} *
FROM OCRD
{whereClause}
ORDER BY CardCode
");

            while (!rs.EoF)
            {
                var dto = MapCustomer(rs);
                if (dto != null)
                    customers.Add(dto);

                rs.MoveNext();
            }

            _logger.LogInformation(
                "📦 SAP batch loaded | After={After} | Count={Count}",
                lastCardCode,
                customers.Count);

            return customers;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
        }
    }

    // =====================================================
    // 📦 FULL READ
    // =====================================================
    public List<SapCustomerDto> ReadAllCustomers(int batchSize = 500)
    {
        _logger.LogInformation("👥 Starting FULL customer read (batched)");

        var all = new List<SapCustomerDto>();
        string lastCardCode = "";

        while (true)
        {
            var batch = ReadCustomerBatchAfter(lastCardCode, batchSize);

            if (batch.Count == 0)
                break;

            all.AddRange(batch);
            lastCardCode = batch.Last().CardCode;

            if (batch.Count < batchSize)
                break;
        }

        _logger.LogInformation("✅ FULL customer read completed | Count={Count}", all.Count);
        return all;
    }

    // =====================================================
    // 🔍 SINGLE
    // =====================================================
    public SapCustomerDto? ReadCustomerByCardCode(string cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            throw new ArgumentException("CardCode required");

        var safe = cardCode.Replace("'", "''");

        var company = _connection.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        try
        {
            rs.DoQuery($@"
SELECT *
FROM OCRD
WHERE CardType = 'C'
AND CardCode = '{safe}'
");

            if (rs.EoF)
                return null;

            return MapCustomer(rs);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
        }
    }

    // =====================================================
    // 💳 CREDIT INFO
    // =====================================================
    public SapCustomerCreditDto? ReadCustomerCredit(string cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            return null;

        var safe = cardCode.Replace("'", "''");

        var company = _connection.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        try
        {
            rs.DoQuery($@"
SELECT CardCode, CreditLine, Balance
FROM OCRD
WHERE CardType = 'C'
AND CardCode = '{safe}'
");

            if (rs.EoF)
                return null;

            return new SapCustomerCreditDto
            {
                CardCode = SafeGetString(rs, "CardCode") ?? "",
                CreditLimit = SafeGetDecimal(rs, "CreditLine") ?? 0m,
                Balance = SafeGetDecimal(rs, "Balance") ?? 0m
            };
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
        }
    }

    // =====================================================
    // 🧩 MAP
    // =====================================================
    private SapCustomerDto? MapCustomer(Recordset rs)
    {
        var cardCode = SafeGetString(rs, "CardCode");

        if (string.IsNullOrWhiteSpace(cardCode))
        {
            _logger.LogWarning("⚠ Skipping SAP row with empty CardCode");
            return null;
        }

        var inactive = SafeGetString(rs, "Inactive");
        var frozen   = SafeGetString(rs, "Frozen");

        return new SapCustomerDto
        {
            CardCode = cardCode.Trim(),
            CardName = SafeGetString(rs, "CardName") ?? "",
            CardType = "C",

            IsActive = inactive != "Y" && frozen != "Y",

            Phone1 = SafeGetString(rs, "Phone1"),
            Phone2 = SafeGetString(rs, "Phone2"),
            Email = SafeGetString(rs, "E_Mail"),

            PriceList = SafeGetDecimal(rs, "ListNum") is decimal pl ? (int)pl : null,
            SlpCode = SafeGetDecimal(rs, "SlpCode") is decimal slp ? (int)slp : null,

            OdooPartnerId = SafeGetString(rs, "U_Odoo_Partner_ID"),

            UpdateDate = TryGetDate(SafeGet(rs, "UpdateDate")),
            UpdateTime = TryGetSapTime(SafeGet(rs, "UpdateTS"))
        };
    }

    // =====================================================
    // SAFE ACCESS (FIXED)
    // =====================================================
    private static object? SafeGet(Recordset rs, string fieldName)
    {
        try
        {
            for (int i = 0; i < rs.Fields.Count; i++)
            {
                var field = rs.Fields.Item(i);

                if (string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    var value = field.Value;

                    if (value == null)
                        return null;

                    if (value is DBNull)
                        return null;

                    return value;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeGetString(Recordset rs, string fieldName)
        => SafeGet(rs, fieldName)?.ToString();

    private static decimal? SafeGetDecimal(Recordset rs, string fieldName)
    {
        var value = SafeGet(rs, fieldName);
        if (value == null) return null;

        if (value is decimal d)
            return d;

        decimal parsed;
        if (decimal.TryParse(value.ToString(), out parsed))
            return parsed;

        return null;
    }

    private static DateTime? TryGetDate(object? v)
        => v is DateTime dt ? dt : DateTime.TryParse(v?.ToString(), out var d) ? d : null;

    private static DateTime? TryGetSapTime(object? v)
    {
        if (v == null) return null;

        int n;
        if (int.TryParse(v.ToString(), out n))
        {
            var s = n.ToString().PadLeft(6, '0');
            return DateTime.Today
                .AddHours(int.Parse(s.Substring(0, 2)))
                .AddMinutes(int.Parse(s.Substring(2, 2)))
                .AddSeconds(int.Parse(s.Substring(4, 2)));
        }

        return null;
    }

    // =====================================================
    // ACTIVE CHECK
    // =====================================================
    /// <summary>
    /// Throws ArgumentException if the CardCode does not exist or is marked Inactive in SAP.
    /// Call this before posting invoices, payments, or orders to SAP.
    /// </summary>
    public void ValidateCardCodeActive(string cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            throw new ArgumentException("CardCode is required");

        var safe = cardCode.Replace("'", "''");
        var company = _connection.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        try
        {
            rs.DoQuery($"SELECT * FROM OCRD WHERE CardType='C' AND CardCode='{safe}'");

            if (rs.EoF)
                throw new ArgumentException($"Customer '{cardCode}' not found in SAP.");

            var inactive = SafeGetString(rs, "Inactive");
            var frozen   = SafeGetString(rs, "Frozen");

            if (string.Equals(inactive, "Y", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Customer '{cardCode}' is marked Inactive in SAP and cannot be used for transactions.");

            if (string.Equals(frozen, "Y", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Customer '{cardCode}' is Frozen in SAP and cannot be used for transactions.");
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
        }
    }
}