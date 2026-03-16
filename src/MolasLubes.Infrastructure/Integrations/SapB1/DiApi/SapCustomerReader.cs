using Microsoft.Extensions.Logging;
using SAPbobsCOM;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapCustomerReader
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapCustomerReader> _logger;

    // Base SELECT used by every customer query.
    // T0.*        → all OCRD columns (version-safe for Inactive/Frozen/frozenFor)
    // BillAddr.*  → default billing address from CRD1
    // ShipAddr.*  → default shipping address from CRD1
    private const string CustomerSelect = @"
    T0.*,
    BillAddr.Street  AS BillToStreet,
    BillAddr.City    AS BillToCity,
    BillAddr.Country AS BillToCountry,
    ShipAddr.Street  AS ShipToStreet,
    ShipAddr.City    AS ShipToCity,
    ShipAddr.Country AS ShipToCountry";

    // OUTER APPLY picks the first address row regardless of whether
    // OCRD.BillToDef / ShipToDef is populated (often empty in SAP B1).
    private const string CustomerJoins = @"
OUTER APPLY (
    SELECT TOP 1 Street, City, Country
    FROM CRD1
    WHERE CardCode  = T0.CardCode
      AND AdresType = 'B'
    ORDER BY LineNum
) BillAddr
OUTER APPLY (
    SELECT TOP 1 Street, City, Country
    FROM CRD1
    WHERE CardCode  = T0.CardCode
      AND AdresType = 'S'
    ORDER BY LineNum
) ShipAddr";

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
SELECT {CustomerSelect}
FROM OCRD T0
{CustomerJoins}
WHERE T0.CardType = 'C'
AND   T0.UpdateDate >= '{sinceDate}'
ORDER BY T0.CardCode
");

            if (!rs.EoF)
            {
                var idx = BuildFieldIndex(rs);
                while (!rs.EoF)
                {
                    var dto = MapCustomer(rs, idx);
                    if (dto != null)
                        customers.Add(dto);

                    rs.MoveNext();
                }
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
    // 🔁 BATCH READ  (used by full-sync streaming loop)
    // =====================================================
    public List<SapCustomerDto> ReadCustomerBatchAfter(string lastCardCode, int batchSize)
    {
        var customers = new List<SapCustomerDto>(batchSize);
        var safeLast  = lastCardCode?.Replace("'", "''");

        var company = _connection.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        try
        {
            string whereClause = string.IsNullOrWhiteSpace(safeLast)
                ? "WHERE T0.CardType = 'C'"
                : $"WHERE T0.CardType = 'C' AND T0.CardCode > '{safeLast}'";

            rs.DoQuery($@"
SELECT TOP {batchSize} {CustomerSelect}
FROM OCRD T0
{CustomerJoins}
{whereClause}
ORDER BY T0.CardCode
");

            if (!rs.EoF)
            {
                var idx = BuildFieldIndex(rs);
                while (!rs.EoF)
                {
                    var dto = MapCustomer(rs, idx);
                    if (dto != null)
                        customers.Add(dto);

                    rs.MoveNext();
                }
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
SELECT {CustomerSelect}
FROM OCRD T0
{CustomerJoins}
WHERE T0.CardType = 'C'
AND   T0.CardCode = '{safe}'
");

            if (rs.EoF)
                return null;

            var idx = BuildFieldIndex(rs);
            return MapCustomer(rs, idx);
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

            var idx = BuildFieldIndex(rs);
            return new SapCustomerCreditDto
            {
                CardCode    = GetString(rs, idx, "CardCode") ?? "",
                CreditLimit = GetDecimal(rs, idx, "CreditLine") ?? 0m,
                Balance     = GetDecimal(rs, idx, "Balance")     ?? 0m
            };
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
        }
    }

    // =====================================================
    // 🧩 MAP  (index-based O(1) field access)
    // =====================================================
    private SapCustomerDto? MapCustomer(Recordset rs, Dictionary<string, int> idx)
    {
        var cardCode = GetString(rs, idx, "CardCode");

        if (string.IsNullOrWhiteSpace(cardCode))
        {
            _logger.LogWarning("⚠ Skipping SAP row with empty CardCode");
            return null;
        }

        // SAP B1 8.81+: Inactive / Frozen columns.
        // Older versions: frozenFor covers both (Y = blocked/inactive).
        var inactive  = GetString(rs, idx, "Inactive");
        var frozen    = GetString(rs, idx, "Frozen");
        var frozenFor = GetString(rs, idx, "frozenFor");

        return new SapCustomerDto
        {
            CardCode = cardCode.Trim(),
            CardName = GetString(rs, idx, "CardName") ?? "",
            CardType = "C",

            IsActive = inactive != "Y" && frozen != "Y" && frozenFor != "Y",

            Phone1 = GetString(rs, idx, "Phone1"),
            Phone2 = GetString(rs, idx, "Phone2"),
            Email  = GetString(rs, idx, "E_Mail"),

            PriceList = GetDecimal(rs, idx, "ListNum") is decimal pl  ? (int)pl  : null,
            SlpCode   = GetDecimal(rs, idx, "SlpCode") is decimal slp ? (int)slp : null,

            BillToStreet  = GetString(rs, idx, "BillToStreet"),
            BillToCity    = GetString(rs, idx, "BillToCity"),
            BillToCountry = GetString(rs, idx, "BillToCountry"),

            ShipToStreet  = GetString(rs, idx, "ShipToStreet"),
            ShipToCity    = GetString(rs, idx, "ShipToCity"),
            ShipToCountry = GetString(rs, idx, "ShipToCountry"),

            OdooPartnerId = GetString(rs, idx, "U_Odoo_Partner_ID"),

            UpdateDate = TryGetDate(GetRaw(rs, idx, "UpdateDate")),
            UpdateTime = TryGetSapTime(GetRaw(rs, idx, "UpdateTS"))
        };
    }

    // =====================================================
    // FIELD-INDEX HELPERS
    // Built once per query execution — O(1) per field access
    // =====================================================
    private static Dictionary<string, int> BuildFieldIndex(Recordset rs)
    {
        var dict = new Dictionary<string, int>(rs.Fields.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rs.Fields.Count; i++)
        {
            var name = rs.Fields.Item(i).Name;
            if (!dict.ContainsKey(name))
                dict[name] = i;
        }
        return dict;
    }

    private static object? GetRaw(Recordset rs, Dictionary<string, int> idx, string name)
    {
        if (!idx.TryGetValue(name, out var i)) return null;
        try
        {
            var v = rs.Fields.Item(i).Value;
            return v is DBNull ? null : v;
        }
        catch { return null; }
    }

    private static string?  GetString (Recordset rs, Dictionary<string, int> idx, string name)
        => GetRaw(rs, idx, name)?.ToString();

    private static decimal? GetDecimal(Recordset rs, Dictionary<string, int> idx, string name)
    {
        var v = GetRaw(rs, idx, name);
        if (v == null) return null;
        if (v is decimal d) return d;
        return decimal.TryParse(v.ToString(), out var p) ? p : null;
    }

    private static DateTime? TryGetDate(object? v)
        => v is DateTime dt ? dt : DateTime.TryParse(v?.ToString(), out var d) ? d : null;

    private static DateTime? TryGetSapTime(object? v)
    {
        if (v == null) return null;
        if (!int.TryParse(v.ToString(), out var n)) return null;
        var s = n.ToString().PadLeft(6, '0');
        return DateTime.Today
            .AddHours(int.Parse(s.Substring(0, 2)))
            .AddMinutes(int.Parse(s.Substring(2, 2)))
            .AddSeconds(int.Parse(s.Substring(4, 2)));
    }

    // =====================================================
    // ACTIVE CHECK
    // =====================================================
    public void ValidateCardCodeActive(string cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            throw new ArgumentException("CardCode is required");

        var safe    = cardCode.Replace("'", "''");
        var company = _connection.GetConnectedCompany();
        var rs      = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        try
        {
            rs.DoQuery($"SELECT * FROM OCRD WHERE CardType='C' AND CardCode='{safe}'");

            if (rs.EoF)
                throw new ArgumentException($"Customer '{cardCode}' not found in SAP.");

            var idx      = BuildFieldIndex(rs);
            var inactive  = GetString(rs, idx, "Inactive");
            var frozen    = GetString(rs, idx, "Frozen");
            var frozenFor = GetString(rs, idx, "frozenFor");

            if (string.Equals(inactive,  "Y", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Customer '{cardCode}' is marked Inactive in SAP and cannot be used for transactions.");

            if (string.Equals(frozen,    "Y", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Customer '{cardCode}' is Frozen in SAP and cannot be used for transactions.");

            if (string.Equals(frozenFor, "Y", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Customer '{cardCode}' is blocked (frozenFor=Y) in SAP and cannot be used for transactions.");
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);
        }
    }
}
