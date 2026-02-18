using Microsoft.EntityFrameworkCore;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Pricing;

public class CustomerPricingService
{
    private readonly MolasCacheDbContext _db;
    private readonly SapPricingReader _sap;

    public CustomerPricingService(
        MolasCacheDbContext db,
        SapPricingReader sap)
    {
        _db = db;
        _sap = sap;
    }

    public async Task<CustomerItemPriceDto?> GetCustomerItemPriceAsync(
        string cardCode,
        string itemCode)
    {
        // ============================
        // 1️⃣ CUSTOMER PRICE LIST (SAP)
        // ============================
        var priceListDto = _sap.GetCustomerPriceList(cardCode);
        if (priceListDto == null)
            return null;

        int listNum = priceListDto.ListNum;

        // ============================
        // 2️⃣ TRY CACHE (FAST PATH)
        // ============================
        var p = await _db.CacheProducts
            .AsNoTracking()
            .Where(x => x.ItemCode == itemCode)
            .Select(x => new
            {
                x.ItemCode,
                x.PriceList_1,
                x.PriceList_2,
                x.PriceList_3
            })
            .FirstOrDefaultAsync();

        if (p != null && listNum >= 1 && listNum <= 3)
        {
            decimal? cachedPrice =
                listNum == 1 ? p.PriceList_1 :
                listNum == 2 ? p.PriceList_2 :
                               p.PriceList_3;

            if (cachedPrice.HasValue)
            {
                return new CustomerItemPriceDto
                {
                    CardCode = cardCode,
                    ItemCode = itemCode,
                    PriceList = listNum,
                    Price = cachedPrice.Value,
                    Source = "CACHE"
                };
            }
        }

        // ============================
        // 3️⃣ FALLBACK → SAP ITM1
        // ============================
        var sapPriceDto = _sap.GetItemPrice(itemCode, listNum);
        if (sapPriceDto == null)
            return null;

        return new CustomerItemPriceDto
        {
            CardCode = cardCode,
            ItemCode = itemCode,
            PriceList = listNum,
            Price = sapPriceDto.Price,
            Source = "SAP"
        };
    }
}