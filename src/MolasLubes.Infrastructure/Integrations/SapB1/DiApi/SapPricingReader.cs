using Microsoft.Extensions.Logging;
using SAPbobsCOM;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapPricingReader
{
    private readonly SapDiApiConnection _conn;
    private readonly ILogger<SapPricingReader> _logger;

    public SapPricingReader(
        SapDiApiConnection conn,
        ILogger<SapPricingReader> logger)
    {
        _conn = conn;
        _logger = logger;
    }

    // ============================================
    // 📌 CUSTOMER PRICE LIST (OCRD → OPLN)
    // ============================================
    public SapCustomerPriceListDto? GetCustomerPriceList(string cardCode)
    {
        var company = _conn.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        rs.DoQuery($@"
SELECT
    c.ListNum,
    p.ListName,

    p.U_Odoo_Pricelist_ID,
    p.U_Odoo_Status,
    p.U_Odoo_LastSync,
    p.U_Odoo_SyncDir,
    p.U_Odoo_ErrorMsg

FROM OCRD c
LEFT JOIN OPLN p
    ON c.ListNum = p.ListNum
WHERE c.CardType = 'C'
  AND c.CardCode = '{cardCode.Replace("'", "''")}'
");

        if (rs.EoF) return null;

        return new SapCustomerPriceListDto
        {
            PriceListId = Convert.ToInt32(rs.Fields.Item("ListNum").Value),
            PriceListName = rs.Fields.Item("ListName").Value?.ToString(),

            // 🔗 ODOO (OPLN)
            OdooPricelistId = rs.Fields.Item("U_Odoo_Pricelist_ID").Value?.ToString(),
            OdooStatus = rs.Fields.Item("U_Odoo_Status").Value?.ToString(),
            OdooSyncDir = rs.Fields.Item("U_Odoo_SyncDir").Value?.ToString(),
            OdooErrorMsg = rs.Fields.Item("U_Odoo_ErrorMsg").Value?.ToString(),
            OdooLastSync = TryGetDate(rs.Fields.Item("U_Odoo_LastSync").Value)
        };
    }

    // ============================================
    // 💰 ITEM PRICE (ITM1)
    // ============================================
    public SapItemPriceDto? GetItemPrice(string itemCode, int priceList)
    {
        var company = _conn.GetConnectedCompany();
        var rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

        rs.DoQuery($@"
SELECT TOP 1
    Price,
    U_Odoo_PriceItem_ID
FROM ITM1
WHERE ItemCode = '{itemCode.Replace("'", "''")}'
  AND PriceList = {priceList}
");

        if (rs.EoF) return null;

        return new SapItemPriceDto
        {
            ItemCode = itemCode,
            PriceList = priceList,
            Price = Convert.ToDecimal(rs.Fields.Item("Price").Value),

            // 🔗 ODOO (ITM1)
            OdooPriceItemId =
                rs.Fields.Item("U_Odoo_PriceItem_ID").Value?.ToString()
        };
    }

    private static DateTime? TryGetDate(object? v)
    {
        if (v == null) return null;
        if (v is DateTime dt) return dt;
        if (DateTime.TryParse(v.ToString(), out var parsed)) return parsed;
        return null;
    }
}