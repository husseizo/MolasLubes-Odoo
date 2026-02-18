using Microsoft.Extensions.Logging;
using MolasLubes.Application.Customers;
using MolasLubes.Infrastructure.Integrations.SapB1.Helpers;
using SAPbobsCOM;
using System;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapCustomerWriter
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapCustomerWriter> _logger;

    public SapCustomerWriter(
        SapDiApiConnection connection,
        ILogger<SapCustomerWriter> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // =====================================================
    // UPSERT CUSTOMER (ODOO → SAP)
    // =====================================================
    public string CreateCustomer(UpsertCustomerDto dto)
    {
        Validate(dto);

        if (string.IsNullOrWhiteSpace(dto.OdooCustomerId))
            throw new ArgumentException("OdooCustomerId is required for enterprise sync.");

        Company company = _connection.GetConnectedCompany();

        var existingCardCode = FindCustomerByOdooId(company, dto.OdooCustomerId);

        if (!string.IsNullOrWhiteSpace(existingCardCode))
        {
            _logger.LogInformation(
                "Customer exists → Updating | OdooId={OdooId} | CardCode={CardCode}",
                dto.OdooCustomerId,
                existingCardCode);

            UpdateCustomer(existingCardCode, dto);
            return existingCardCode;
        }

        BusinessPartners bp =
            (BusinessPartners)company.GetBusinessObject(BoObjectTypes.oBusinessPartners);

        try
        {
            bp.CardType = BoCardTypes.cCustomer;

            int series = GetCustomerSeries(company);
            bp.Series = series;

            bp.CardName = dto.CardName.Trim();
            bp.Phone1 = dto.Phone1;
            bp.Phone2 = dto.Phone2;
            bp.EmailAddress = dto.Email;

            // =====================================================
            // ✅ NEW: PRICE LIST + SALESPERSON
            // =====================================================
            if (dto.PriceList.HasValue)
            {
                bp.PriceListNum = dto.PriceList.Value;

                _logger.LogInformation(
                    "Applying PriceList {PriceList} to customer {Name}",
                    dto.PriceList.Value,
                    dto.CardName);
            }

            if (dto.SlpCode.HasValue)
            {
                bp.SalesPersonCode = dto.SlpCode.Value;

                _logger.LogInformation(
                    "Assigning SalesPerson {SlpCode} to customer {Name}",
                    dto.SlpCode.Value,
                    dto.CardName);
            }

            // =====================================================

            OdooUdfMapper.ApplyCustomerUdfs(bp, dto.OdooCustomerId);
            ApplyAddresses(bp, dto);

            _logger.LogInformation(
                "Creating SAP Customer | Name={Name} | OdooId={OdooId} | Series={Series}",
                dto.CardName,
                dto.OdooCustomerId,
                series);

            int rc = bp.Add();

            if (rc != 0)
            {
                company.GetLastError(out int code, out string msg);

                _logger.LogError(
                    "SAP Customer create failed | Code={Code} | Message={Message}",
                    code,
                    msg);

                throw new Exception($"SAP Customer create failed ({code}): {msg}");
            }

            string newCardCode = company.GetNewObjectKey();

            _logger.LogInformation(
                "SAP Customer created | CardCode={CardCode} | OdooId={OdooId}",
                newCardCode,
                dto.OdooCustomerId);

            return newCardCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SAP Customer UPSERT failed | OdooId={OdooId}",
                dto.OdooCustomerId);

            throw;
        }
    }

    // =====================================================
    // FIND CUSTOMER BY ODOO UDF
    // =====================================================
    private string? FindCustomerByOdooId(Company company, string odooId)
    {
        Recordset rs =
            (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        string safeId = odooId.Replace("'", "''");

        rs.DoQuery($@"
SELECT TOP 1 CardCode
FROM OCRD
WHERE CardType = 'C'
AND U_Odoo_Partner_ID = '{safeId}'
");

        if (rs.EoF)
            return null;

        return rs.Fields.Item("CardCode").Value.ToString();
    }

    // =====================================================
    // UPDATE CUSTOMER
    // =====================================================
    public void UpdateCustomer(string cardCode, UpsertCustomerDto dto)
    {
        Validate(dto);

        if (string.IsNullOrWhiteSpace(cardCode))
            throw new ArgumentException("cardCode is required");

        Company company = _connection.GetConnectedCompany();

        BusinessPartners bp =
            (BusinessPartners)company.GetBusinessObject(BoObjectTypes.oBusinessPartners);

        if (!bp.GetByKey(cardCode))
            throw new Exception($"SAP customer not found: {cardCode}");

        try
        {
            bp.CardName = dto.CardName.Trim();
            bp.Phone1 = dto.Phone1;
            bp.Phone2 = dto.Phone2;
            bp.EmailAddress = dto.Email;

            OdooUdfMapper.ApplyCustomerUdfs(bp, dto.OdooCustomerId);
            ApplyAddresses(bp, dto);

            int rc = bp.Update();

            if (rc != 0)
            {
                company.GetLastError(out int code, out string msg);

                _logger.LogError(
                    "SAP Customer update failed | Code={Code} | Message={Message}",
                    code,
                    msg);

                throw new Exception($"SAP Customer update failed ({code}): {msg}");
            }

            _logger.LogInformation(
                "SAP Customer updated | CardCode={CardCode}",
                cardCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SAP Customer update failed | CardCode={CardCode}",
                cardCode);

            throw;
        }
    }

    // =====================================================
    // GET CUSTOMER SERIES
    // =====================================================
    private int GetCustomerSeries(Company company)
    {
        Recordset rs =
            (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        rs.DoQuery(@"
SELECT TOP 1 Series
FROM NNM1
WHERE ObjectCode = '2'
AND SeriesName = 'CSR'
AND Locked = 'N'
");

        if (rs.EoF)
        {
            _logger.LogCritical("Customer series 'CSR' not found or locked.");
            throw new Exception("Customer numbering series 'CSR' not found or locked.");
        }

        int series = Convert.ToInt32(rs.Fields.Item("Series").Value);

        _logger.LogInformation("Using Customer Series {Series}", series);

        return series;
    }

    // =====================================================
    // VALIDATION
    // =====================================================
    private static void Validate(UpsertCustomerDto dto)
    {
        if (dto == null)
            throw new ArgumentNullException(nameof(dto));

        if (string.IsNullOrWhiteSpace(dto.CardName))
            throw new ArgumentException("CardName is required");
    }

    // =====================================================
    // ADDRESS HANDLING
    // =====================================================
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

            if (bp.Addresses.AddressName == addressName &&
                bp.Addresses.AddressType == type)
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