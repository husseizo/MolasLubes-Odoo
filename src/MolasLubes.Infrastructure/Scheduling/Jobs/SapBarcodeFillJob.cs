using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MolasLubes.Infrastructure.Integrations.LiquiMoly;
using MolasLubes.Infrastructure.Integrations.Meguin;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Scheduling;
using Quartz;

namespace MolasLubes.Infrastructure.Scheduling.Jobs;

/// <summary>
/// Daily off-hours job that fills missing Unit barcodes for COCWHSE items.
///
/// COCWHSE workers scan individual bottles — the unit EAN from the LiquiMoly/Meguin
/// catalogue is the correct value to register against the "Unit" UoM in SAP OBCD.
///
/// MANWHSE is intentionally excluded: workers there scan cartons, whose barcode is a
/// different EAN from the unit barcode. The scraper only has unit EANs, so writing
/// them under the packaging UoM would register the wrong value. MANWHSE packaging
/// barcodes must be added via physical carton scan →
///   POST /api/admin/items/{itemCode}/barcodes  (single)
///   POST /api/admin/items/barcodes/fill-bulk   (many at once)
///
/// Only numeric ItemCodes are scraped. Auto-parts (BM/MB/VAG/VOL) have no catalogue
/// data and are skipped — use the manual scan endpoints for those too.
/// </summary>
[DisallowConcurrentExecution]
public class SapBarcodeFillJob : IJob
{
    private readonly IServiceScopeFactory       _scopeFactory;
    private readonly ILogger<SapBarcodeFillJob> _logger;

    private const int BatchSize = 30;

    public SapBarcodeFillJob(
        IServiceScopeFactory       scopeFactory,
        ILogger<SapBarcodeFillJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[BarcodeFill] Job started");

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var productReader     = scope.ServiceProvider.GetRequiredService<SapProductReader>();
            var barcodeReader     = scope.ServiceProvider.GetRequiredService<SapProductBarcodeReader>();
            var barcodeWriter     = scope.ServiceProvider.GetRequiredService<SapProductBarcodeWriter>();
            var barcodeWriteSvc   = scope.ServiceProvider.GetRequiredService<SapItemBarcodeWriteService>();
            var lmScraper         = scope.ServiceProvider.GetRequiredService<LiquiMolyProductScraperService>();
            var meguinScraper     = scope.ServiceProvider.GetRequiredService<MeguinProductScraperService>();

            // ── Step 1: COCWHSE items missing a Unit barcode ──────────────────
            var missing  = productReader.ReadMissingBarcodeReport();
            var cocItems = missing.Where(r => r.WhsCode == "COCWHSE").ToList();
            var manCount = missing.Count(r => r.WhsCode == "MANWHSE");

            _logger.LogInformation(
                "[BarcodeFill] Missing | COCWHSE(unit)={Coc} | MANWHSE(packaging)={Man} — MANWHSE skipped (carton barcodes differ from unit EAN, use manual scan endpoints)",
                cocItems.Count, manCount);

            if (cocItems.Count == 0)
            {
                _logger.LogInformation("[BarcodeFill] Nothing to fill — all COCWHSE items have unit barcodes");
                sw.Stop();
                return;
            }

            // ── Step 2: Filter to scrape-able items (numeric ItemCode) ────────
            var scrapeable = cocItems.Where(r => r.ItemCode.All(char.IsDigit)).ToList();
            var skipped    = cocItems.Count - scrapeable.Count;

            _logger.LogInformation(
                "[BarcodeFill] Scrape-able={Count} | Skipped(auto-parts/no-catalog)={Skip}",
                scrapeable.Count, skipped);

            if (scrapeable.Count == 0)
            {
                _logger.LogInformation("[BarcodeFill] All remaining items are auto-parts — use manual scan endpoints");
                sw.Stop();
                return;
            }

            // ── Step 3: Split LiquiMoly vs Meguin, scrape, write Unit barcode ─
            var lmSkus = scrapeable
                .Where(r => !r.ItemName.Contains("meguin", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ItemCode).ToList();

            var meguinSkus = scrapeable
                .Where(r => r.ItemName.Contains("meguin", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ItemCode).ToList();

            _logger.LogInformation(
                "[BarcodeFill] SKU split | LiquiMoly={Lm} | Meguin={Meg}",
                lmSkus.Count, meguinSkus.Count);

            int written = 0;

            written += await FillInBatchesAsync(
                lmSkus, lmScraper, barcodeReader, barcodeWriter,
                "LiquiMoly", context.CancellationToken);

            written += await FillInBatchesAsync(
                meguinSkus, meguinScraper, barcodeReader, barcodeWriter,
                "Meguin", context.CancellationToken);

            // ── Step 4: Fallback — write OITM.CodeBars → OBCD where scraper had no EAN ──
            // Some items have a barcode already stored in OITM.CodeBars (the legacy
            // single-barcode field) but it was never copied to OBCD.  A Plus reads OBCD
            // during inventory counting, so without this step those items still fail to
            // count even though SAP already holds the barcode value.
            var legacyItems = productReader.ReadCocwhseItemsWithLegacyCodeBars();

            _logger.LogInformation(
                "[BarcodeFill/Legacy] OITM.CodeBars fallback — found {Count} COCWHSE item(s) with CodeBars not yet in OBCD",
                legacyItems.Count);

            int legacyWritten = 0;
            foreach (var item in legacyItems)
            {
                if (context.CancellationToken.IsCancellationRequested) break;
                try
                {
                    var r = barcodeWriteSvc.WriteBarcode(item.ItemCode, item.CodeBars, "Unit");
                    if (r.Success && !r.WasAlreadyPresent)
                    {
                        legacyWritten++;
                        _logger.LogInformation(
                            "[BarcodeFill/Legacy] Written {Code} → {Bcd}", item.ItemCode, item.CodeBars);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[BarcodeFill/Legacy] Failed to write CodeBars for {Code}", item.ItemCode);
                }
            }

            _logger.LogInformation(
                "[BarcodeFill/Legacy] Written from CodeBars={Count}", legacyWritten);

            written += legacyWritten;

            sw.Stop();
            _logger.LogInformation(
                "[BarcodeFill] Complete | Written={Written} (scraper={Scraper} + legacy={Legacy}) | Skipped(no-catalog)={Skip} | DurationMs={Ms}",
                written, written - legacyWritten, legacyWritten, skipped, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[BarcodeFill] Job FAILED | DurationMs={Ms}", sw.ElapsedMilliseconds);
            await QuartzRetryHelper.HandleRetryAsync(context, ex);
        }
    }

    private async Task<int> FillInBatchesAsync(
        List<string>                   skus,
        LiquiMolyProductScraperService scraper,
        SapProductBarcodeReader        barcodeReader,
        SapProductBarcodeWriter        barcodeWriter,
        string                         label,
        CancellationToken              ct)
    {
        if (skus.Count == 0) return 0;

        int written    = 0;
        int batchCount = (int)Math.Ceiling((double)skus.Count / BatchSize);

        for (int i = 0; i < batchCount; i++)
        {
            if (ct.IsCancellationRequested) break;

            var batch = skus.Skip(i * BatchSize).Take(BatchSize).ToList();

            _logger.LogInformation(
                "[BarcodeFill/{L}] Batch {B}/{T} — scraping {N} item(s)",
                label, i + 1, batchCount, batch.Count);

            List<LiquiMolyProductDto> products;
            try
            {
                products = await scraper.ScrapeByArticleNumbersAsync(batch, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[BarcodeFill/{L}] Batch {B}/{T} — scrape failed, continuing",
                    label, i + 1, batchCount);
                continue;
            }

            if (products.Count == 0)
            {
                _logger.LogWarning(
                    "[BarcodeFill/{L}] Batch {B}/{T} — scraper returned 0 products",
                    label, i + 1, batchCount);
                continue;
            }

            // Enrich: sets HasUnitBarcode so the writer skips already-fixed items
            await barcodeReader.EnrichAsync(products, ct);

            _logger.LogInformation(
                "[BarcodeFill/{L}] Batch {B}/{T} — scraped={S} | haveEAN={E}",
                label, i + 1, batchCount, products.Count,
                products.Count(p => !string.IsNullOrWhiteSpace(p.EanCode)));

            try
            {
                await barcodeWriter.WriteAsync(products, ct);
                written += products.Count(p => !string.IsNullOrWhiteSpace(p.EanCode) && !p.HasUnitBarcode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[BarcodeFill/{L}] Batch {B}/{T} — barcode write failed (non-fatal)",
                    label, i + 1, batchCount);
            }
        }

        return written;
    }
}
