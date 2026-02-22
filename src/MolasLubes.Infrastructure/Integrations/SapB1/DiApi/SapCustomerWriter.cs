using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Application.Customers;
using MolasLubes.Infrastructure.Integrations.SapB1.Errors;
using MolasLubes.Infrastructure.Integrations.SapB1.Helpers;
using SAPbobsCOM;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapCustomerWriter
{
    private readonly SapDiApiConnection _connection;
    private readonly SapSettings _settings;
    private readonly ILogger<SapCustomerWriter> _logger;

    public SapCustomerWriter(
        SapDiApiConnection connection,
        IOptions<SapSettings> settings,
        ILogger<SapCustomerWriter> logger)
    {
        _connection = connection;
        _settings = settings.Value;
        _logger = logger;
    }

    // =====================================================
    // CREATE CUSTOMER  (idempotent by OdooCustomerId)
    // =====================================================
    public string CreateCustomer(UpsertCustomerDto dto)
    {
        Validate(dto);

        if (string.IsNullOrWhiteSpace(dto.OdooCustomerId))
            throw new ArgumentException("OdooCustomerId is required for enterprise sync.");

        var company = _connection.GetConnectedCompany();

        var existingCardCode = FindCustomerByOdooId(company, dto.OdooCustomerId);
        if (!string.IsNullOrWhiteSpace(existingCardCode))
        {
            _logger.LogInformation(
                "Customer exists → Updating | OdooId={OdooId} | CardCode={CardCode}",
                dto.OdooCustomerId, existingCardCode);

            UpdateCustomer(existingCardCode, dto);
            return existingCardCode;
        }

        var bp = (BusinessPartners)company.GetBusinessObject(BoObjectTypes.oBusinessPartners);

        try
        {
            bp.CardType = BoCardTypes.cCustomer;
            bp.Series = GetCustomerSeries(company);
            bp.CardName = dto.CardName.Trim();
            bp.Phone1 = dto.Phone1;
            bp.Phone2 = dto.Phone2;
            bp.EmailAddress = dto.Email;

            if (dto.PriceList.HasValue)
                bp.PriceListNum = dto.PriceList.Value;

            if (dto.SlpCode.HasValue)
                bp.SalesPersonCode = dto.SlpCode.Value;

            if (dto.GroupCode.HasValue)
                bp.GroupCode = dto.GroupCode.Value;

            if (dto.CreditLine.HasValue)
                bp.CreditLimit = (double)dto.CreditLine.Value;

            if (!string.IsNullOrWhiteSpace(dto.Remarks))
                bp.Notes = dto.Remarks.Trim();

            if (dto.IsActive.HasValue)
                bp.Frozen = dto.IsActive.Value ? BoYesNoEnum.tNO : BoYesNoEnum.tYES;

            OdooUdfMapper.ApplyCustomerUdfs(bp, dto.OdooCustomerId);
            ApplyAddresses(bp, dto);

            int rc = bp.Add();
            if (rc != 0)
            {
                company.GetLastError(out int code, out string msg);
                throw SapErrorTranslator.Translate(msg, code);
            }

            var newCardCode = company.GetNewObjectKey();

            _logger.LogInformation(
                "✅ SAP Customer created | CardCode={CardCode} | OdooId={OdooId}",
                newCardCode, dto.OdooCustomerId);

            return newCardCode;
        }
        catch (SapIntegrationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            company.GetLastError(out int code, out string msg);
            if (code != 0)
                throw SapErrorTranslator.Translate(msg, code, ex);
            throw;
        }
    }

    // =====================================================
    // UPDATE CUSTOMER
    // =====================================================
    public void UpdateCustomer(string cardCode, UpsertCustomerDto dto)
    {
        Validate(dto);

        if (string.IsNullOrWhiteSpace(cardCode))
            throw new ArgumentException("cardCode is required");

        var company = _connection.GetConnectedCompany();
        var bp = (BusinessPartners)company.GetBusinessObject(BoObjectTypes.oBusinessPartners);

        if (!bp.GetByKey(cardCode))
            throw new ArgumentException($"SAP customer not found: {cardCode}");

        try
        {
            bp.CardName = dto.CardName.Trim();
            bp.Phone1 = dto.Phone1;
            bp.Phone2 = dto.Phone2;
            bp.EmailAddress = dto.Email;

            if (dto.PriceList.HasValue)
                bp.PriceListNum = dto.PriceList.Value;

            if (dto.SlpCode.HasValue)
                bp.SalesPersonCode = dto.SlpCode.Value;

            if (dto.GroupCode.HasValue)
                bp.GroupCode = dto.GroupCode.Value;

            if (dto.CreditLine.HasValue)
                bp.CreditLimit = (double)dto.CreditLine.Value;

            if (!string.IsNullOrWhiteSpace(dto.Remarks))
                bp.Notes = dto.Remarks.Trim();

            if (dto.IsActive.HasValue)
                bp.Frozen = dto.IsActive.Value ? BoYesNoEnum.tNO : BoYesNoEnum.tYES;

            OdooUdfMapper.ApplyCustomerUdfs(bp, dto.OdooCustomerId);
            ApplyAddresses(bp, dto);

            int rc = bp.Update();
            if (rc != 0)
            {
                company.GetLastError(out int code, out string msg);
                throw SapErrorTranslator.Translate(msg, code);
            }

            _logger.LogInformation("✅ SAP Customer updated | CardCode={CardCode}", cardCode);
        }
        catch (SapIntegrationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            company.GetLastError(out int code, out string msg);
            if (code != 0)
                throw SapErrorTranslator.Translate(msg, code, ex);
            throw;
        }
    }

    // =====================================================
    // DEACTIVATE  (Frozen=Y — blocks new transactions)
    // =====================================================
    public void DeactivateCustomer(string cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            throw new ArgumentException("cardCode is required");

        var company = _connection.GetConnectedCompany();
        var bp = (BusinessPartners)company.GetBusinessObject(BoObjectTypes.oBusinessPartners);

        if (!bp.GetByKey(cardCode))
            throw new ArgumentException($"SAP customer not found: {cardCode}");

        bp.Frozen = BoYesNoEnum.tYES;

        int rc = bp.Update();
        if (rc != 0)
        {
            company.GetLastError(out int code, out string msg);
            throw SapErrorTranslator.Translate(msg, code);
        }

        _logger.LogInformation("🔒 SAP Customer deactivated (Frozen) | CardCode={CardCode}", cardCode);
    }

    // =====================================================
    // REACTIVATE  (Frozen=N)
    // =====================================================
    public void ReactivateCustomer(string cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            throw new ArgumentException("cardCode is required");

        var company = _connection.GetConnectedCompany();
        var bp = (BusinessPartners)company.GetBusinessObject(BoObjectTypes.oBusinessPartners);

        if (!bp.GetByKey(cardCode))
            throw new ArgumentException($"SAP customer not found: {cardCode}");

        bp.Frozen = BoYesNoEnum.tNO;

        int rc = bp.Update();
        if (rc != 0)
        {
            company.GetLastError(out int code, out string msg);
            throw SapErrorTranslator.Translate(msg, code);
        }

        _logger.LogInformation("🔓 SAP Customer reactivated | CardCode={CardCode}", cardCode);
    }

    // ── Private helpers ──────────────────────────────────

    private string? FindCustomerByOdooId(Company company, string odooId)
    {
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
        var safeId = odooId.Replace("'", "''");

        rs.DoQuery($@"
SELECT TOP 1 CardCode
FROM OCRD
WHERE CardType = 'C'
AND U_Odoo_Partner_ID = '{safeId}'
");

        if (rs.EoF) return null;
        return rs.Fields.Item("CardCode").Value.ToString();
    }

    private int GetCustomerSeries(Company company)
    {
        var seriesName = _settings.CustomerSeries;
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);
        var safeName = seriesName.Replace("'", "''");

        rs.DoQuery($@"
SELECT TOP 1 Series
FROM NNM1
WHERE ObjectCode = '2'
AND SeriesName = '{safeName}'
AND Locked = 'N'
");

        if (rs.EoF)
        {
            _logger.LogCritical(
                "Customer series '{SeriesName}' not found or locked.", seriesName);
            throw new InvalidOperationException(
                $"Customer numbering series '{seriesName}' not found or locked. " +
                $"Check SAP settings or update SAP:CustomerSeries in appsettings.");
        }

        var series = Convert.ToInt32((object)rs.Fields.Item("Series").Value);
        _logger.LogInformation("Using Customer Series {Series} ({Name})", series, seriesName);
        return series;
    }

    private static void Validate(UpsertCustomerDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.CardName)) throw new ArgumentException("CardName is required");
    }

    private static void ApplyAddresses(BusinessPartners bp, UpsertCustomerDto dto)
    {
        UpsertAddress(bp, "BILL_TO", BoAddressType.bo_BillTo, dto.BillTo);
        UpsertAddress(bp, "SHIP_TO", BoAddressType.bo_ShipTo, dto.ShipTo);
    }

    private static void UpsertAddress(
        BusinessPartners bp,
        string addressName,
        BoAddressType type,
        CustomerAddressDto? a)
    {
        if (a == null) return;

        int foundIndex = -1;

        for (int i = 0; i < bp.Addresses.Count; i++)
        {
            bp.Addresses.SetCurrentLine(i);
            if (bp.Addresses.AddressName == addressName && bp.Addresses.AddressType == type)
            {
                foundIndex = i;
                break;
            }
        }

        if (foundIndex >= 0)
            bp.Addresses.SetCurrentLine(foundIndex);
        else
        {
            bp.Addresses.AddressName = addressName;
            bp.Addresses.AddressType = type;
        }

        bp.Addresses.Street = a.Street;
        bp.Addresses.City = a.City;
        bp.Addresses.Country = SapCountryMapper.ToSapCode(a.Country);

        if (foundIndex < 0)
            bp.Addresses.Add();
    }
}
