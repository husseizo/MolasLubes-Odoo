#pragma warning disable CA1416 // Possible null argument

using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;
using MolasLubes.Infrastructure.Integrations.SapB1.Udfs;
using SAPbobsCOM;
using System.Threading;

namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public class SapProductReader
{
    private readonly SapDiApiConnection _connection;
    private readonly ILogger<SapProductReader> _logger;

    public SapProductReader(
        SapDiApiConnection connection,
        ILogger<SapProductReader> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    // =====================================================
    // READ ALL PRODUCTS (READ-ONLY)
    // =====================================================
    public List<SapProductDto> ReadAllProducts()
    {
        _logger.LogInformation("📦 SAP Product read started (STA thread)");

        var results = new List<SapProductDto>();
        Exception? threadException = null;

        var thread = new Thread(() =>
        {
            Company? company = null;
            Recordset? rs = null;

            try
            {
                company = _connection.CreateNewCompany();

                if (company.Connect() != 0)
                {
                    company.GetLastError(out var code, out var msg);
                    throw new Exception($"SAP connect failed {code}: {msg}");
                }

                rs = (Recordset)company.GetBusinessObject(BoObjectTypes.BoRecordset);

                rs.DoQuery(@"
SELECT
    T0.ItemCode,
    T0.ItemName,
    T1.WhsCode,
    T1.OnHand,

    MAX(T3.BcdCode) AS Barcode,

    T0.U_Odoo_Product_ID,
    T0.U_Odoo_Status,
    T0.U_Odoo_LastSync,
    T0.U_Odoo_SyncDir,

    MAX(CASE WHEN T2.PriceList = 1 THEN T2.Price END) AS PriceList_1,
    MAX(CASE WHEN T2.PriceList = 2 THEN T2.Price END) AS PriceList_2,
    MAX(CASE WHEN T2.PriceList = 3 THEN T2.Price END) AS PriceList_3

FROM OITM T0
INNER JOIN OITW T1
    ON T0.ItemCode = T1.ItemCode

LEFT JOIN ITM1 T2
    ON T0.ItemCode = T2.ItemCode

LEFT JOIN OBCD T3
    ON T0.ItemCode = T3.ItemCode

WHERE T0.frozenFor = 'N'

GROUP BY
    T0.ItemCode,
    T0.ItemName,
    T1.WhsCode,
    T1.OnHand,
    T0.U_Odoo_Product_ID,
    T0.U_Odoo_Status,
    T0.U_Odoo_LastSync,
    T0.U_Odoo_SyncDir

ORDER BY T0.ItemCode, T1.WhsCode
");

                while (!rs.EoF)
                {
                    var dto = new SapProductDto
                    {
                        ItemCode = rs.Fields.Item("ItemCode").Value?.ToString() ?? "",
                        ItemName = rs.Fields.Item("ItemName").Value?.ToString() ?? "",
                        WarehouseCode = rs.Fields.Item("WhsCode").Value?.ToString() ?? "",
                        OnHand = Convert.ToDecimal(rs.Fields.Item("OnHand").Value ?? 0),

                        Barcode = rs.Fields.Item("Barcode").Value?.ToString(),

                        OdooProductId = rs.Fields.Item(OdooUdfs.ProductId).Value?.ToString(),
                        OdooStatus = rs.Fields.Item(OdooUdfs.Status).Value?.ToString(),
                        OdooSyncDir = rs.Fields.Item(OdooUdfs.SyncDir).Value?.ToString(),
                        OdooLastSync = rs.Fields.Item(OdooUdfs.LastSync).Value as DateTime?
                    };

                    dto.PriceLists[1] = Convert.ToDecimal(rs.Fields.Item("PriceList_1").Value ?? 0);
                    dto.PriceLists[2] = Convert.ToDecimal(rs.Fields.Item("PriceList_2").Value ?? 0);
                    dto.PriceLists[3] = Convert.ToDecimal(rs.Fields.Item("PriceList_3").Value ?? 0);

                    results.Add(dto);
                    rs.MoveNext();
                }
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                if (rs != null)
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(rs);

                if (company != null && company.Connected)
                    company.Disconnect();

                if (company != null)
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(company);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
            throw threadException;

        _logger.LogInformation(
            "📦 SAP Product read completed | Count={Count}",
            results.Count);

        return results;
    }
}