using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.LiquiMolyTransfers;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.LiquiMolyTransfers;

/// <summary>
/// Orchestrates the Liqui Moly internal stock transfer saga:
///   DryRun → classify each line (no writes)
///   Apply  → preflight → save audit header → Goods Issue → Goods Receipt
///
/// Status flow:
///   PENDING → ISSUED → COMPLETED
///   ISSUED  → RECEIPT_PENDING  (GR failed after GI succeeded)
///   PENDING → FAILED           (preflight blocked apply)
/// </summary>
public class LiquiMolyTransferService
{
    private readonly SapLiquiMolyItemMapper  _mapper;
    private readonly SapLiquiMolyStockReader _stockReader;
    private readonly SapGoodsIssueWriter     _giWriter;
    private readonly SapGoodsReceiptWriter   _grWriter;
    private readonly TransferRefGenerator    _refGen;
    private readonly MolasCacheDbContext     _db;
    private readonly ILogger<LiquiMolyTransferService> _logger;

    public LiquiMolyTransferService(
        SapLiquiMolyItemMapper  mapper,
        SapLiquiMolyStockReader stockReader,
        SapGoodsIssueWriter     giWriter,
        SapGoodsReceiptWriter   grWriter,
        TransferRefGenerator    refGen,
        MolasCacheDbContext     db,
        ILogger<LiquiMolyTransferService> logger)
    {
        _mapper      = mapper;
        _stockReader = stockReader;
        _giWriter    = giWriter;
        _grWriter    = grWriter;
        _refGen      = refGen;
        _db          = db;
        _logger      = logger;
    }

    // =====================================================
    // DRY-RUN — preflight only, no writes anywhere
    // =====================================================

    public LiquiMolyTransferDryRunResult DryRun(CreateLiquiMolyTransferRequest request)
    {
        var (error, rows) = Preflight(request);

        if (error != null)
            return new LiquiMolyTransferDryRunResult { Error = error };

        var totals = rows
            .GroupBy(r => r.Outcome)
            .ToDictionary(g => g.Key, g => g.Count());

        var canApply = rows.All(r =>
            r.Outcome == TransferLineOutcome.OK);

        return new LiquiMolyTransferDryRunResult
        {
            CanApply = canApply,
            Lines    = rows,
            Totals   = totals
        };
    }

    // =====================================================
    // APPLY — preflight → GI (source) → GR (target) → audit
    // =====================================================

    public async Task<LiquiMolyTransferApplyResult> ApplyAsync(
        CreateLiquiMolyTransferRequest request,
        CancellationToken ct = default)
    {
        // ── 1. Preflight ──────────────────────────────────────────────────
        var (error, rows) = Preflight(request);

        if (error != null)
            return new LiquiMolyTransferApplyResult
                { Status = "FAILED", ErrorMessage = error, Lines = rows };

        var blockingFailures = rows.Any(r => r.Outcome != TransferLineOutcome.OK);
        if (blockingFailures)
            return new LiquiMolyTransferApplyResult
            {
                Status       = "FAILED",
                ErrorMessage = "Preflight found blocking issues — see Lines for details.",
                Lines        = rows
            };

        // ── 2. Persist audit header ───────────────────────────────────────
        var transferRef = _refGen.Generate();

        var header = new CacheLiquiMolyTransfer
        {
            TransferRef     = transferRef,
            SourceProfile   = request.SourceProfile,
            TargetProfile   = request.TargetProfile,
            SourceWarehouse = request.SourceWarehouse,
            TargetWarehouse = request.TargetWarehouse,
            Comments        = request.Comments,
            Status          = "PENDING",
            CreatedAt       = DateTime.UtcNow
        };

        foreach (var row in rows)
        {
            header.Lines.Add(new CacheLiquiMolyTransferLine
            {
                SourceItemCode = row.SourceItemCode,
                TargetItemCode = row.TargetItemCode!,
                ArticleNumber  = row.ArticleNumber!,
                SourceItemName = row.SourceItemName,
                TargetItemName = row.TargetItemName,
                Quantity       = row.RequestedQty,
                Status         = "PENDING"
            });
        }

        _db.CacheLiquiMolyTransfers.Add(header);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "LiquiMolyTransfer: audit saved | Ref={Ref} | Lines={Count}",
            transferRef, rows.Count);

        // ── 3. Goods Issue (source DB) ────────────────────────────────────
        var giLines = rows
            .Select(r => new GoodsDocumentLine(r.SourceItemCode, r.RequestedQty))
            .ToList();

        SapDocumentRef giRef;
        try
        {
            giRef = _giWriter.CreateGoodsIssue(
                profileKey:    request.SourceProfile,
                warehouseCode: request.SourceWarehouse,
                transferRef:   transferRef,
                targetProfile: request.TargetProfile,
                comments:      request.Comments ?? string.Empty,
                lines:         giLines);
        }
        catch (Exception ex)
        {
            header.Status       = "FAILED";
            header.ErrorMessage = $"Goods Issue failed: {ex.Message}";
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex, "LiquiMolyTransfer: Goods Issue failed | Ref={Ref}", transferRef);
            return new LiquiMolyTransferApplyResult
                { TransferRef = transferRef, Status = "FAILED", ErrorMessage = header.ErrorMessage, Lines = rows };
        }

        header.GoodsIssueDocEntry = giRef.DocEntry;
        header.GoodsIssueDocNum   = giRef.DocNum;
        header.Status             = "ISSUED";
        await _db.SaveChangesAsync(ct);

        // ── 4. Goods Receipt (target DB) ──────────────────────────────────
        var grLines = rows
            .Select(r => new GoodsDocumentLine(r.SourceItemCode, r.RequestedQty, r.TargetItemCode))
            .ToList();

        SapDocumentRef grRef;
        try
        {
            grRef = _grWriter.CreateGoodsReceipt(
                profileKey:    request.TargetProfile,
                warehouseCode: request.TargetWarehouse,
                transferRef:   transferRef,
                sourceProfile: request.SourceProfile,
                comments:      request.Comments ?? string.Empty,
                lines:         grLines);
        }
        catch (Exception ex)
        {
            header.Status       = "RECEIPT_PENDING";
            header.ErrorMessage = $"Goods Receipt failed (GI={giRef.DocNum} already posted): {ex.Message}";
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex,
                "LiquiMolyTransfer: Goods Receipt failed | Ref={Ref} | GI DocNum={Num}",
                transferRef, giRef.DocNum);
            return new LiquiMolyTransferApplyResult
            {
                TransferRef          = transferRef,
                Status               = "RECEIPT_PENDING",
                GoodsIssueDocEntry   = giRef.DocEntry,
                GoodsIssueDocNum     = giRef.DocNum,
                ErrorMessage         = header.ErrorMessage,
                Lines                = rows
            };
        }

        header.GoodsReceiptDocEntry = grRef.DocEntry;
        header.GoodsReceiptDocNum   = grRef.DocNum;
        header.Status               = "COMPLETED";
        header.CompletedAt          = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "LiquiMolyTransfer: completed | Ref={Ref} | GI={GiNum} | GR={GrNum}",
            transferRef, giRef.DocNum, grRef.DocNum);

        return new LiquiMolyTransferApplyResult
        {
            TransferRef          = transferRef,
            Status               = "COMPLETED",
            GoodsIssueDocEntry   = giRef.DocEntry,
            GoodsIssueDocNum     = giRef.DocNum,
            GoodsReceiptDocEntry = grRef.DocEntry,
            GoodsReceiptDocNum   = grRef.DocNum,
            Lines                = rows
        };
    }

    // =====================================================
    // GET TRANSFER — read audit header + lines
    // =====================================================

    public async Task<CacheLiquiMolyTransfer?> GetTransferAsync(
        string transferRef,
        CancellationToken ct = default)
    {
        return await _db.CacheLiquiMolyTransfers
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.TransferRef == transferRef, ct);
    }

    // =====================================================
    // RETRY RECEIPT — re-attempt GR on RECEIPT_PENDING transfers
    // =====================================================

    public async Task<LiquiMolyTransferApplyResult> RetryReceiptAsync(
        string transferRef,
        CancellationToken ct = default)
    {
        var header = await _db.CacheLiquiMolyTransfers
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.TransferRef == transferRef, ct);

        if (header == null)
            return new LiquiMolyTransferApplyResult
                { TransferRef = transferRef, Status = "FAILED", ErrorMessage = "Transfer not found." };

        if (header.Status != "RECEIPT_PENDING")
            return new LiquiMolyTransferApplyResult
            {
                TransferRef = transferRef,
                Status      = header.Status,
                ErrorMessage = $"Transfer is in status '{header.Status}', only RECEIPT_PENDING can be retried."
            };

        var grLines = header.Lines
            .Select(l => new GoodsDocumentLine(l.SourceItemCode, l.Quantity, l.TargetItemCode))
            .ToList();

        SapDocumentRef grRef;
        try
        {
            grRef = _grWriter.CreateGoodsReceipt(
                profileKey:    header.TargetProfile,
                warehouseCode: header.TargetWarehouse,
                transferRef:   transferRef,
                sourceProfile: header.SourceProfile,
                comments:      header.Comments ?? string.Empty,
                lines:         grLines);
        }
        catch (Exception ex)
        {
            header.ErrorMessage = $"Retry failed: {ex.Message}";
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex, "LiquiMolyTransfer: retry receipt failed | Ref={Ref}", transferRef);
            return new LiquiMolyTransferApplyResult
            {
                TransferRef        = transferRef,
                Status             = "RECEIPT_PENDING",
                GoodsIssueDocEntry = header.GoodsIssueDocEntry,
                GoodsIssueDocNum   = header.GoodsIssueDocNum,
                ErrorMessage       = header.ErrorMessage
            };
        }

        header.GoodsReceiptDocEntry = grRef.DocEntry;
        header.GoodsReceiptDocNum   = grRef.DocNum;
        header.Status               = "COMPLETED";
        header.CompletedAt          = DateTime.UtcNow;
        header.ErrorMessage         = null;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "LiquiMolyTransfer: receipt retry succeeded | Ref={Ref} | GR={GrNum}",
            transferRef, grRef.DocNum);

        return new LiquiMolyTransferApplyResult
        {
            TransferRef          = transferRef,
            Status               = "COMPLETED",
            GoodsIssueDocEntry   = header.GoodsIssueDocEntry,
            GoodsIssueDocNum     = header.GoodsIssueDocNum,
            GoodsReceiptDocEntry = grRef.DocEntry,
            GoodsReceiptDocNum   = grRef.DocNum
        };
    }

    // =====================================================
    // PRIVATE — shared preflight logic
    // =====================================================

    private (string? error, List<TransferLinePreflightRow> rows) Preflight(
        CreateLiquiMolyTransferRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceWarehouse))
            return ("sourceWarehouse is required.", new());

        if (string.IsNullOrWhiteSpace(request.TargetWarehouse))
            return ("targetWarehouse is required.", new());

        if (request.Lines == null || request.Lines.Count == 0)
            return ("At least one line is required.", new());

        var rows = new List<TransferLinePreflightRow>();

        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0)
            {
                rows.Add(new TransferLinePreflightRow
                {
                    SourceItemCode = line.SourceItemCode,
                    RequestedQty   = line.Quantity,
                    Outcome        = TransferLineOutcome.INVALID_QTY,
                    Message        = "Quantity must be greater than zero."
                });
                continue;
            }

            // Item mapping (validates brand, frozen status, article number, target item)
            LiquiMolyMappedLine mapped;
            try
            {
                mapped = _mapper.MapLine(
                    request.SourceProfile, request.TargetProfile, line.SourceItemCode);
            }
            catch (Exception ex)
            {
                rows.Add(new TransferLinePreflightRow
                {
                    SourceItemCode = line.SourceItemCode,
                    RequestedQty   = line.Quantity,
                    Outcome        = "ERROR",
                    Message        = ex.Message
                });
                continue;
            }

            if (!mapped.IsOk)
            {
                rows.Add(new TransferLinePreflightRow
                {
                    SourceItemCode = line.SourceItemCode,
                    ArticleNumber  = mapped.ArticleNumber,
                    RequestedQty   = line.Quantity,
                    Outcome        = mapped.Outcome,
                    Message        = mapped.FailReason
                });
                continue;
            }

            // Stock check
            decimal available;
            try
            {
                available = _stockReader.GetAvailableQuantity(
                    request.SourceProfile, line.SourceItemCode, request.SourceWarehouse);
            }
            catch (Exception ex)
            {
                rows.Add(new TransferLinePreflightRow
                {
                    SourceItemCode = line.SourceItemCode,
                    ArticleNumber  = mapped.ArticleNumber,
                    SourceItemName = mapped.SourceItemName,
                    TargetItemCode = mapped.TargetItemCode,
                    TargetItemName = mapped.TargetItemName,
                    RequestedQty   = line.Quantity,
                    Outcome        = "ERROR",
                    Message        = $"Stock read failed: {ex.Message}"
                });
                continue;
            }

            if (available < line.Quantity)
            {
                rows.Add(new TransferLinePreflightRow
                {
                    SourceItemCode = line.SourceItemCode,
                    ArticleNumber  = mapped.ArticleNumber,
                    SourceItemName = mapped.SourceItemName,
                    TargetItemCode = mapped.TargetItemCode,
                    TargetItemName = mapped.TargetItemName,
                    RequestedQty   = line.Quantity,
                    AvailableQty   = available,
                    Outcome        = TransferLineOutcome.INSUFFICIENT_STOCK,
                    Message        = $"Only {available} available in {request.SourceWarehouse}."
                });
                continue;
            }

            rows.Add(new TransferLinePreflightRow
            {
                SourceItemCode = line.SourceItemCode,
                ArticleNumber  = mapped.ArticleNumber,
                SourceItemName = mapped.SourceItemName,
                TargetItemCode = mapped.TargetItemCode,
                TargetItemName = mapped.TargetItemName,
                RequestedQty   = line.Quantity,
                AvailableQty   = available,
                Outcome        = TransferLineOutcome.OK
            });
        }

        return (null, rows);
    }
}
