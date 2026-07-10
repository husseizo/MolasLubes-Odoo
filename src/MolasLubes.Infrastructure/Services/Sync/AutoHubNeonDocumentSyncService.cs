using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;
using Npgsql;
using System.Net.Sockets;

namespace MolasLubes.Infrastructure.Services.Sync;

/// <summary>
/// Syncs AutoHub document types (deliveries, sales orders, invoices, etc.) from SAP B1
/// directly into dedicated Neon tables using UpdateDate delta filtering.
/// First run loads ~2 years; subsequent runs only pull changed/new documents.
/// </summary>
public class AutoHubNeonDocumentSyncService
{
    private static readonly TimeZoneInfo _tz = ResolveTimeZone();
    private const int LookbackYears = 2;

    private readonly SapAutoHubDocumentReader _reader;
    private readonly AutoHubDbContext _neon;
    private readonly ILogger<AutoHubNeonDocumentSyncService> _logger;

    public AutoHubNeonDocumentSyncService(
        SapAutoHubDocumentReader reader,
        AutoHubDbContext neon,
        ILogger<AutoHubNeonDocumentSyncService> logger)
    {
        _reader = reader;
        _neon   = neon;
        _logger = logger;
    }

    // ── Public sync entry points ──────────────────────────────────────────────

    public Task SyncDeliveriesAsync(CancellationToken ct = default)    => SyncDocumentAsync("Deliveries",        ct, SyncDeliveriesCoreAsync);
    public Task SyncSalesOrdersAsync(CancellationToken ct = default)   => SyncDocumentAsync("SalesOrders",       ct, SyncSalesOrdersCoreAsync);
    public Task SyncInvoicesAsync(CancellationToken ct = default)      => SyncDocumentAsync("Invoices",          ct, SyncInvoicesCoreAsync);
    public Task SyncGoodsReceiptsAsync(CancellationToken ct = default) => SyncDocumentAsync("GoodsReceipts",     ct, SyncGoodsReceiptsCoreAsync);
    public Task SyncStockTransfersAsync(CancellationToken ct = default)=> SyncDocumentAsync("StockTransfers",    ct, SyncStockTransfersCoreAsync);
    public Task SyncInventoryCountingsAsync(CancellationToken ct = default) => SyncDocumentAsync("InventoryCountings", ct, SyncInventoryCountingsCoreAsync);
    public Task SyncPurchaseOrdersAsync(CancellationToken ct = default)=> SyncDocumentAsync("PurchaseOrders",    ct, SyncPurchaseOrdersCoreAsync);
    public Task SyncUoMsAsync(CancellationToken ct = default)          => SyncDocumentAsync("UoMs",              ct, SyncUoMsCoreAsync);

    // ── Watermark helper ──────────────────────────────────────────────────────

    private async Task<string> GetSinceDateAsync(string docType, CancellationToken ct)
    {
        var state = await _neon.AutoHubSyncStates.FindAsync(new object[] { docType }, ct);
        var since = state?.LastSyncedAt ?? DateTime.UtcNow.AddYears(-LookbackYears);

        // Convert UTC to local, subtract 1 day safety buffer to catch same-day edits
        var local = TimeZoneInfo.ConvertTimeFromUtc(since, _tz).AddDays(-1);
        return local.ToString("yyyy-MM-dd");
    }

    private async Task UpdateWatermarkAsync(string docType, CancellationToken ct)
    {
        var state = await _neon.AutoHubSyncStates.FindAsync(new object[] { docType }, ct);
        if (state == null)
            _neon.AutoHubSyncStates.Add(new NeonAutoHubSyncState { DocType = docType, LastSyncedAt = DateTime.UtcNow });
        else
            state.LastSyncedAt = DateTime.UtcNow;
    }

    // ── Deliveries (ODLN/DLN1) ────────────────────────────────────────────────

    private async Task SyncDeliveriesCoreAsync(string sinceDate, CancellationToken ct)
    {
        var (headers, lines) = _reader.ReadDeliveries(sinceDate);
        if (headers.Count == 0) return;

        var keys = headers.Select(h => h.DocEntry).ToHashSet();
        var existing = await _neon.AutoHubDeliveries
            .Where(d => keys.Contains(d.DocEntry))
            .ToDictionaryAsync(d => d.DocEntry, ct);

        var now = DateTime.UtcNow;

        foreach (var h in headers)
        {
            if (existing.TryGetValue(h.DocEntry, out var entity))
            {
                entity.DocNum      = h.DocNum;
                entity.CardCode    = h.CardCode;
                entity.CardName    = h.CardName;
                entity.DocDate     = h.DocDate;
                entity.DocStatus   = h.DocStatus;
                entity.IsCancelled = h.IsCancelled;
                entity.Comments    = h.Comments;
                entity.UpdatedInSap = h.UpdatedInSap;
                entity.SyncedAt    = now;
            }
            else
            {
                _neon.AutoHubDeliveries.Add(new NeonAutoHubDelivery
                {
                    DocEntry    = h.DocEntry,
                    DocNum      = h.DocNum,
                    CardCode    = h.CardCode,
                    CardName    = h.CardName,
                    DocDate     = h.DocDate,
                    DocStatus   = h.DocStatus,
                    IsCancelled = h.IsCancelled,
                    Comments    = h.Comments,
                    UpdatedInSap = h.UpdatedInSap,
                    SyncedAt    = now
                });
            }
        }

        var existingLines = await _neon.AutoHubDeliveryLines
            .Where(l => keys.Contains(l.DocEntry))
            .ToListAsync(ct);
        _neon.AutoHubDeliveryLines.RemoveRange(existingLines);
        _neon.AutoHubDeliveryLines.AddRange(lines.Select(l => new NeonAutoHubDeliveryLine
        {
            DocEntry    = l.DocEntry,
            LineNum     = l.LineNum,
            ItemCode    = l.ItemCode,
            Description = l.Description,
            Quantity    = l.Quantity,
            Price       = l.Price,
            LineTotal   = l.LineTotal,
            WhsCode     = l.WhsCode
        }));

        _logger.LogInformation("AutoHub Deliveries upserted: Headers={H} Lines={L}", headers.Count, lines.Count);
    }

    // ── Sales Orders (ORDR/RDR1) ──────────────────────────────────────────────

    private async Task SyncSalesOrdersCoreAsync(string sinceDate, CancellationToken ct)
    {
        var (headers, lines) = _reader.ReadSalesOrders(sinceDate);
        if (headers.Count == 0) return;

        var keys = headers.Select(h => h.DocEntry).ToHashSet();
        var existing = await _neon.AutoHubSalesOrders
            .Where(d => keys.Contains(d.DocEntry))
            .ToDictionaryAsync(d => d.DocEntry, ct);

        var now = DateTime.UtcNow;

        foreach (var h in headers)
        {
            if (existing.TryGetValue(h.DocEntry, out var entity))
            {
                entity.DocNum      = h.DocNum;
                entity.CardCode    = h.CardCode;
                entity.CardName    = h.CardName;
                entity.DocDate     = h.DocDate;
                entity.DocDueDate  = h.DocDueDate;
                entity.DocStatus   = h.DocStatus;
                entity.DocTotal    = h.DocTotal;
                entity.Comments    = h.Comments;
                entity.UpdatedInSap = h.UpdatedInSap;
                entity.SyncedAt    = now;
            }
            else
            {
                _neon.AutoHubSalesOrders.Add(new NeonAutoHubSalesOrder
                {
                    DocEntry    = h.DocEntry,
                    DocNum      = h.DocNum,
                    CardCode    = h.CardCode,
                    CardName    = h.CardName,
                    DocDate     = h.DocDate,
                    DocDueDate  = h.DocDueDate,
                    DocStatus   = h.DocStatus,
                    DocTotal    = h.DocTotal,
                    Comments    = h.Comments,
                    UpdatedInSap = h.UpdatedInSap,
                    SyncedAt    = now
                });
            }
        }

        var existingLines = await _neon.AutoHubSalesOrderLines
            .Where(l => keys.Contains(l.DocEntry))
            .ToListAsync(ct);
        _neon.AutoHubSalesOrderLines.RemoveRange(existingLines);
        _neon.AutoHubSalesOrderLines.AddRange(lines.Select(l => new NeonAutoHubSalesOrderLine
        {
            DocEntry    = l.DocEntry,
            LineNum     = l.LineNum,
            ItemCode    = l.ItemCode,
            Description = l.Description,
            Quantity    = l.Quantity,
            OpenQty     = l.OpenQty,
            Price       = l.Price,
            LineTotal   = l.LineTotal,
            WhsCode     = l.WhsCode
        }));

        _logger.LogInformation("AutoHub SalesOrders upserted: Headers={H} Lines={L}", headers.Count, lines.Count);
    }

    // ── AR Invoices (OINV/INV1) ───────────────────────────────────────────────

    private async Task SyncInvoicesCoreAsync(string sinceDate, CancellationToken ct)
    {
        var (headers, lines) = _reader.ReadInvoices(sinceDate);
        if (headers.Count == 0) return;

        var keys = headers.Select(h => h.DocEntry).ToHashSet();
        var existing = await _neon.AutoHubInvoices
            .Where(d => keys.Contains(d.DocEntry))
            .ToDictionaryAsync(d => d.DocEntry, ct);

        var now = DateTime.UtcNow;

        foreach (var h in headers)
        {
            if (existing.TryGetValue(h.DocEntry, out var entity))
            {
                entity.DocNum      = h.DocNum;
                entity.CardCode    = h.CardCode;
                entity.CardName    = h.CardName;
                entity.DocDate     = h.DocDate;
                entity.DocStatus   = h.DocStatus;
                entity.DocTotal    = h.DocTotal;
                entity.VatSum      = h.VatSum;
                entity.PaidToDate  = h.PaidToDate;
                entity.Comments    = h.Comments;
                entity.UpdatedInSap = h.UpdatedInSap;
                entity.SyncedAt    = now;
            }
            else
            {
                _neon.AutoHubInvoices.Add(new NeonAutoHubInvoice
                {
                    DocEntry    = h.DocEntry,
                    DocNum      = h.DocNum,
                    CardCode    = h.CardCode,
                    CardName    = h.CardName,
                    DocDate     = h.DocDate,
                    DocStatus   = h.DocStatus,
                    DocTotal    = h.DocTotal,
                    VatSum      = h.VatSum,
                    PaidToDate  = h.PaidToDate,
                    Comments    = h.Comments,
                    UpdatedInSap = h.UpdatedInSap,
                    SyncedAt    = now
                });
            }
        }

        var existingLines = await _neon.AutoHubInvoiceLines
            .Where(l => keys.Contains(l.DocEntry))
            .ToListAsync(ct);
        _neon.AutoHubInvoiceLines.RemoveRange(existingLines);
        _neon.AutoHubInvoiceLines.AddRange(lines.Select(l => new NeonAutoHubInvoiceLine
        {
            DocEntry    = l.DocEntry,
            LineNum     = l.LineNum,
            ItemCode    = l.ItemCode,
            Description = l.Description,
            Quantity    = l.Quantity,
            Price       = l.Price,
            LineTotal   = l.LineTotal,
            WhsCode     = l.WhsCode
        }));

        _logger.LogInformation("AutoHub Invoices upserted: Headers={H} Lines={L}", headers.Count, lines.Count);
    }

    // ── Goods Receipts (OIGN/IGN1) ────────────────────────────────────────────

    private async Task SyncGoodsReceiptsCoreAsync(string sinceDate, CancellationToken ct)
    {
        var (headers, lines) = _reader.ReadGoodsReceipts(sinceDate);
        if (headers.Count == 0) return;

        var keys = headers.Select(h => h.DocEntry).ToHashSet();
        var existing = await _neon.AutoHubGoodsReceipts
            .Where(d => keys.Contains(d.DocEntry))
            .ToDictionaryAsync(d => d.DocEntry, ct);

        var now = DateTime.UtcNow;

        foreach (var h in headers)
        {
            if (existing.TryGetValue(h.DocEntry, out var entity))
            {
                entity.DocNum      = h.DocNum;
                entity.DocDate     = h.DocDate;
                entity.Comments    = h.Comments;
                entity.UpdatedInSap = h.UpdatedInSap;
                entity.SyncedAt    = now;
            }
            else
            {
                _neon.AutoHubGoodsReceipts.Add(new NeonAutoHubGoodsReceipt
                {
                    DocEntry    = h.DocEntry,
                    DocNum      = h.DocNum,
                    DocDate     = h.DocDate,
                    Comments    = h.Comments,
                    UpdatedInSap = h.UpdatedInSap,
                    SyncedAt    = now
                });
            }
        }

        var existingLines = await _neon.AutoHubGoodsReceiptLines
            .Where(l => keys.Contains(l.DocEntry))
            .ToListAsync(ct);
        _neon.AutoHubGoodsReceiptLines.RemoveRange(existingLines);
        _neon.AutoHubGoodsReceiptLines.AddRange(lines.Select(l => new NeonAutoHubGoodsReceiptLine
        {
            DocEntry    = l.DocEntry,
            LineNum     = l.LineNum,
            ItemCode    = l.ItemCode,
            Description = l.Description,
            Quantity    = l.Quantity,
            WhsCode     = l.WhsCode
        }));

        _logger.LogInformation("AutoHub GoodsReceipts upserted: Headers={H} Lines={L}", headers.Count, lines.Count);
    }

    // ── Stock Transfers (OWTR/WTR1) ───────────────────────────────────────────

    private async Task SyncStockTransfersCoreAsync(string sinceDate, CancellationToken ct)
    {
        var (headers, lines) = _reader.ReadStockTransfers(sinceDate);
        if (headers.Count == 0) return;

        var keys = headers.Select(h => h.DocEntry).ToHashSet();
        var existing = await _neon.AutoHubStockTransfers
            .Where(d => keys.Contains(d.DocEntry))
            .ToDictionaryAsync(d => d.DocEntry, ct);

        var now = DateTime.UtcNow;

        foreach (var h in headers)
        {
            if (existing.TryGetValue(h.DocEntry, out var entity))
            {
                entity.DocNum      = h.DocNum;
                entity.DocDate     = h.DocDate;
                entity.Comments    = h.Comments;
                entity.UpdatedInSap = h.UpdatedInSap;
                entity.SyncedAt    = now;
            }
            else
            {
                _neon.AutoHubStockTransfers.Add(new NeonAutoHubStockTransfer
                {
                    DocEntry    = h.DocEntry,
                    DocNum      = h.DocNum,
                    DocDate     = h.DocDate,
                    Comments    = h.Comments,
                    UpdatedInSap = h.UpdatedInSap,
                    SyncedAt    = now
                });
            }
        }

        var existingLines = await _neon.AutoHubStockTransferLines
            .Where(l => keys.Contains(l.DocEntry))
            .ToListAsync(ct);
        _neon.AutoHubStockTransferLines.RemoveRange(existingLines);
        _neon.AutoHubStockTransferLines.AddRange(lines.Select(l => new NeonAutoHubStockTransferLine
        {
            DocEntry    = l.DocEntry,
            LineNum     = l.LineNum,
            ItemCode    = l.ItemCode,
            Description = l.Description,
            Quantity    = l.Quantity,
            FromWhsCode = l.FromWhsCode,
            ToWhsCode   = l.ToWhsCode
        }));

        _logger.LogInformation("AutoHub StockTransfers upserted: Headers={H} Lines={L}", headers.Count, lines.Count);
    }

    // ── Inventory Counting (OINC/INC1) ───────────────────────────────────────

    private async Task SyncInventoryCountingsCoreAsync(string sinceDate, CancellationToken ct)
    {
        var (headers, lines) = _reader.ReadInventoryCountings(sinceDate);
        if (headers.Count == 0) return;

        var keys = headers.Select(h => h.DocEntry).ToHashSet();
        var existing = await _neon.AutoHubInventoryCountings
            .Where(d => keys.Contains(d.DocEntry))
            .ToDictionaryAsync(d => d.DocEntry, ct);

        var now = DateTime.UtcNow;

        foreach (var h in headers)
        {
            if (existing.TryGetValue(h.DocEntry, out var entity))
            {
                entity.DocNum      = h.DocNum;
                entity.CountDate   = h.CountDate;
                entity.Remarks     = h.Remarks;
                entity.UpdatedInSap = h.UpdatedInSap;
                entity.SyncedAt    = now;
            }
            else
            {
                _neon.AutoHubInventoryCountings.Add(new NeonAutoHubInventoryCounting
                {
                    DocEntry    = h.DocEntry,
                    DocNum      = h.DocNum,
                    CountDate   = h.CountDate,
                    Remarks     = h.Remarks,
                    UpdatedInSap = h.UpdatedInSap,
                    SyncedAt    = now
                });
            }
        }

        var existingLines = await _neon.AutoHubInventoryCountingLines
            .Where(l => keys.Contains(l.DocEntry))
            .ToListAsync(ct);
        _neon.AutoHubInventoryCountingLines.RemoveRange(existingLines);
        _neon.AutoHubInventoryCountingLines.AddRange(lines.Select(l => new NeonAutoHubInventoryCountingLine
        {
            DocEntry   = l.DocEntry,
            LineNum    = l.LineNum,
            ItemCode   = l.ItemCode,
            WhsCode    = l.WhsCode,
            CountedQty = l.CountedQty
        }));

        _logger.LogInformation("AutoHub InventoryCountings upserted: Headers={H} Lines={L}", headers.Count, lines.Count);
    }

    // ── Purchase Orders (OPOR/POR1) ───────────────────────────────────────────

    private async Task SyncPurchaseOrdersCoreAsync(string sinceDate, CancellationToken ct)
    {
        var (headers, lines) = _reader.ReadPurchaseOrders(sinceDate);
        if (headers.Count == 0) return;

        var keys = headers.Select(h => h.DocEntry).ToHashSet();
        var existing = await _neon.AutoHubPurchaseOrders
            .Where(d => keys.Contains(d.DocEntry))
            .ToDictionaryAsync(d => d.DocEntry, ct);

        var now = DateTime.UtcNow;

        foreach (var h in headers)
        {
            if (existing.TryGetValue(h.DocEntry, out var entity))
            {
                entity.DocNum      = h.DocNum;
                entity.CardCode    = h.CardCode;
                entity.CardName    = h.CardName;
                entity.DocDate     = h.DocDate;
                entity.DocDueDate  = h.DocDueDate;
                entity.DocStatus   = h.DocStatus;
                entity.DocTotal    = h.DocTotal;
                entity.Comments    = h.Comments;
                entity.UpdatedInSap = h.UpdatedInSap;
                entity.SyncedAt    = now;
            }
            else
            {
                _neon.AutoHubPurchaseOrders.Add(new NeonAutoHubPurchaseOrder
                {
                    DocEntry    = h.DocEntry,
                    DocNum      = h.DocNum,
                    CardCode    = h.CardCode,
                    CardName    = h.CardName,
                    DocDate     = h.DocDate,
                    DocDueDate  = h.DocDueDate,
                    DocStatus   = h.DocStatus,
                    DocTotal    = h.DocTotal,
                    Comments    = h.Comments,
                    UpdatedInSap = h.UpdatedInSap,
                    SyncedAt    = now
                });
            }
        }

        var existingLines = await _neon.AutoHubPurchaseOrderLines
            .Where(l => keys.Contains(l.DocEntry))
            .ToListAsync(ct);
        _neon.AutoHubPurchaseOrderLines.RemoveRange(existingLines);
        _neon.AutoHubPurchaseOrderLines.AddRange(lines.Select(l => new NeonAutoHubPurchaseOrderLine
        {
            DocEntry    = l.DocEntry,
            LineNum     = l.LineNum,
            ItemCode    = l.ItemCode,
            Description = l.Description,
            Quantity    = l.Quantity,
            OpenQty     = l.OpenQty,
            Price       = l.Price,
            LineTotal   = l.LineTotal,
            WhsCode     = l.WhsCode
        }));

        _logger.LogInformation("AutoHub PurchaseOrders upserted: Headers={H} Lines={L}", headers.Count, lines.Count);
    }

    // ── UoMs (OUOM — full sync) ───────────────────────────────────────────────

    private async Task SyncUoMsCoreAsync(string sinceDate, CancellationToken ct)
    {
        var rows = _reader.ReadUoMs();
        if (rows.Count == 0) return;

        var keys = rows.Select(r => r.UomEntry).ToHashSet();
        var existing = await _neon.AutoHubUoMs
            .ToDictionaryAsync(u => u.UomEntry, ct);

        foreach (var r in rows)
        {
            if (existing.TryGetValue(r.UomEntry, out var entity))
            {
                entity.UomCode   = r.UomCode;
                entity.UomName   = r.UomName;
                entity.GroupEntry = r.GroupEntry;
            }
            else
            {
                _neon.AutoHubUoMs.Add(new NeonAutoHubUoM
                {
                    UomEntry  = r.UomEntry,
                    UomCode   = r.UomCode,
                    UomName   = r.UomName,
                    GroupEntry = r.GroupEntry
                });
            }
        }

        // Deactivate removed UoMs
        foreach (var old in existing.Values.Where(e => !keys.Contains(e.UomEntry)))
            _neon.AutoHubUoMs.Remove(old);

        _logger.LogInformation("AutoHub UoMs synced: {Count}", rows.Count);
    }

    // ── Orchestration wrapper (watermark + transaction + transient handling) ──

    private async Task SyncDocumentAsync(
        string docType,
        CancellationToken ct,
        Func<string, CancellationToken, Task> core)
    {
        _logger.LogInformation("AutoHubDocumentSync [{Type}]: started", docType);

        try
        {
            var sinceDate = await GetSinceDateAsync(docType, ct);

            var strategy = _neon.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _neon.Database.BeginTransactionAsync(ct);

                await core(sinceDate, ct);
                await UpdateWatermarkAsync(docType, ct);

                await _neon.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });

            _logger.LogInformation("AutoHubDocumentSync [{Type}]: completed", docType);
        }
        catch (Exception ex) when (IsTransientNeonFailure(ex))
        {
            _logger.LogWarning(ex,
                "AutoHubDocumentSync [{Type}]: transient Neon failure — skipping, will retry next run", docType);
            await ResetNeonPoolAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoHubDocumentSync [{Type}]: failed", docType);
            throw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task ResetNeonPoolAsync()
    {
        try
        {
            await _neon.Database.CloseConnectionAsync();
            if (_neon.Database.GetDbConnection() is NpgsqlConnection c)
                NpgsqlConnection.ClearPool(c);
            else
                NpgsqlConnection.ClearAllPools();
        }
        catch { /* best-effort */ }
    }

    private static bool IsTransientNeonFailure(Exception ex)
    {
        if (ex is EndOfStreamException or IOException or TimeoutException or SocketException)
            return true;
        if (ex is NpgsqlException n && (
            n.Message.Contains("Exception while reading from stream", StringComparison.OrdinalIgnoreCase) ||
            n.Message.Contains("Failed to connect to",               StringComparison.OrdinalIgnoreCase) ||
            n.Message.Contains("Timeout during connection attempt",  StringComparison.OrdinalIgnoreCase)))
            return true;
        return ex.InnerException != null && IsTransientNeonFailure(ex.InnerException);
    }

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in new[] { "Africa/Dar_es_Salaam", "E. Africa Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { }
        }
        return TimeZoneInfo.Utc;
    }
}
