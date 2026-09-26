using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.LiquiMolyTransfers;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Services.Sync;

namespace MolasLubes.Infrastructure.Services.LiquiMolyTransfers;

/// <summary>
/// Orchestrates the Liqui Moly internal stock transfer saga.
///
/// Same-company flow (SourceProfile == TargetProfile):
///   Preflight → create OWTR in SAP (linked to OWTQ if provided) → audit
///   Status: PENDING → COMPLETED
///
/// Cross-company flow (SourceProfile != TargetProfile):
///   Preflight → GI (source) → GR (target) → close OWTQ if provided → audit
///   Status: PENDING → ISSUED → COMPLETED  |  ISSUED → RECEIPT_PENDING
/// </summary>
public class LiquiMolyTransferService
{
    private readonly SapLiquiMolyItemMapper        _mapper;
    private readonly SapLiquiMolyStockReader       _stockReader;
    private readonly SapGoodsIssueWriter           _giWriter;
    private readonly SapGoodsReceiptWriter         _grWriter;
    private readonly SapInventoryTransferWriter    _itWriter;
    private readonly SapLiquiMolyDocumentReader    _docReader;
    private readonly LiquiMolyTransferSyncService  _syncService;
    private readonly TransferRefGenerator          _refGen;
    private readonly MolasCacheDbContext           _db;
    private readonly ILogger<LiquiMolyTransferService> _logger;

    public LiquiMolyTransferService(
        SapLiquiMolyItemMapper        mapper,
        SapLiquiMolyStockReader       stockReader,
        SapGoodsIssueWriter           giWriter,
        SapGoodsReceiptWriter         grWriter,
        SapInventoryTransferWriter    itWriter,
        SapLiquiMolyDocumentReader    docReader,
        LiquiMolyTransferSyncService  syncService,
        TransferRefGenerator          refGen,
        MolasCacheDbContext           db,
        ILogger<LiquiMolyTransferService> logger)
    {
        _mapper      = mapper;
        _stockReader = stockReader;
        _giWriter    = giWriter;
        _grWriter    = grWriter;
        _itWriter    = itWriter;
        _docReader   = docReader;
        _syncService = syncService;
        _refGen      = refGen;
        _db          = db;
        _logger      = logger;
    }

    // =====================================================
    // DRY-RUN — preflight only, no writes anywhere
    // =====================================================

    public LiquiMolyTransferDryRunResult DryRun(CreateLiquiMolyTransferRequest request)
    {
        // Base-request header resolution
        string? baseRequestDocNum = null;
        if (request.BaseRequestDocEntry.HasValue)
        {
            var owtqDoc = TryReadOwtq(request.SourceProfile, request.TargetProfile,
                                      request.BaseRequestDocEntry.Value, out var owtqError);
            if (owtqError != null)
                return new LiquiMolyTransferDryRunResult { Error = owtqError };
            baseRequestDocNum = owtqDoc?.Header.DocNum;
        }

        var (error, rows) = Preflight(request);

        if (error != null)
            return new LiquiMolyTransferDryRunResult { Error = error };

        var totals = rows
            .GroupBy(r => r.Outcome)
            .ToDictionary(g => g.Key, g => g.Count());

        var canApply = rows.All(r => r.Outcome == TransferLineOutcome.OK);

        return new LiquiMolyTransferDryRunResult
        {
            CanApply           = canApply,
            BaseRequestDocEntry = request.BaseRequestDocEntry,
            BaseRequestDocNum   = baseRequestDocNum,
            Lines              = rows,
            Totals             = totals
        };
    }

    // =====================================================
    // APPLY — execute the transfer
    // =====================================================

    public async Task<LiquiMolyTransferApplyResult> ApplyAsync(
        CreateLiquiMolyTransferRequest request,
        CancellationToken ct = default)
    {
        // ── 0. Idempotency check ──────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(request.ClientReference))
        {
            var existing = await _db.CacheLiquiMolyTransfers
                .FirstOrDefaultAsync(t => t.ClientReference == request.ClientReference, ct);

            if (existing != null)
            {
                _logger.LogInformation(
                    "LiquiMolyTransfer: idempotent return for ClientReference={Ref} → TransferRef={TRef}",
                    request.ClientReference, existing.TransferRef);

                return BuildResultFromAudit(existing);
            }
        }

        // ── 1. Preflight ──────────────────────────────────────────────────────
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

        // ── 2. Resolve base-request doc num ───────────────────────────────────
        string? baseRequestDocNum = null;
        if (request.BaseRequestDocEntry.HasValue)
        {
            var owtqDoc = TryReadOwtq(request.SourceProfile, request.TargetProfile,
                                      request.BaseRequestDocEntry.Value, out var owtqError);
            if (owtqError != null)
                return new LiquiMolyTransferApplyResult
                    { Status = "FAILED", ErrorMessage = owtqError, Lines = rows };
            baseRequestDocNum = owtqDoc?.Header.DocNum;
        }

        // ── 3. Persist audit header ───────────────────────────────────────────
        var transferRef = _refGen.Generate();

        var header = new CacheLiquiMolyTransfer
        {
            TransferRef             = transferRef,
            ClientReference         = request.ClientReference,
            ActorSapUserCode        = request.ActorSapUserCode,
            SourceProfile           = request.SourceProfile,
            TargetProfile           = request.TargetProfile,
            SourceWarehouse         = request.SourceWarehouse,
            TargetWarehouse         = request.TargetWarehouse,
            Comments                = request.Comments,
            BaseRequestDocEntry     = request.BaseRequestDocEntry,
            BaseRequestDocNum       = baseRequestDocNum,
            Status                  = "PENDING",
            CreatedAt               = DateTime.UtcNow
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
            "LiquiMolyTransfer: audit saved | Ref={Ref} | Lines={Count} | Mode={Mode}",
            transferRef, rows.Count,
            IsSameProfileTransfer(request.SourceProfile, request.TargetProfile) ? "OWTR" : "GI+GR");

        // ── 4. Execute SAP document(s) ────────────────────────────────────────
        if (IsSameProfileTransfer(request.SourceProfile, request.TargetProfile))
            return await ExecuteSameCompanyAsync(request, header, rows, ct);

        return await ExecuteCrossCompanyAsync(request, header, rows, ct);
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
                TransferRef  = transferRef,
                Status       = header.Status,
                ErrorMessage = $"Transfer is in status '{header.Status}', only RECEIPT_PENDING can be retried."
            };

        // Guard: check if GR already exists in SAP via U_TransferRef
        SapDocumentRef? existingGr = null;
        try
        {
            existingGr = _grWriter.FindExistingReceipt(header.TargetProfile, transferRef);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LiquiMolyTransfer: could not check for existing GR in SAP | Ref={Ref}", transferRef);
        }

        if (existingGr != null)
        {
            _logger.LogWarning(
                "LiquiMolyTransfer: GR already exists in SAP, recovering audit | Ref={Ref} | DocNum={Num}",
                transferRef, existingGr.DocNum);

            header.GoodsReceiptDocEntry = existingGr.DocEntry;
            header.GoodsReceiptDocNum   = existingGr.DocNum;
            header.Status               = "COMPLETED";
            header.CompletedAt          = DateTime.UtcNow;
            header.ErrorMessage         = null;
            await _db.SaveChangesAsync(ct);

            return new LiquiMolyTransferApplyResult
            {
                TransferRef          = transferRef,
                Status               = "COMPLETED",
                GoodsIssueDocEntry   = header.GoodsIssueDocEntry,
                GoodsIssueDocNum     = header.GoodsIssueDocNum,
                GoodsReceiptDocEntry = existingGr.DocEntry,
                GoodsReceiptDocNum   = existingGr.DocNum
            };
        }

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
    // PRIVATE — same-company OWTR path
    // =====================================================

    private async Task<LiquiMolyTransferApplyResult> ExecuteSameCompanyAsync(
        CreateLiquiMolyTransferRequest request,
        CacheLiquiMolyTransfer header,
        List<TransferLinePreflightRow> rows,
        CancellationToken ct)
    {
        var transferRef = header.TransferRef;

        var itLines = rows.Select((r, idx) => new InventoryTransferLine(
            r.SourceItemCode,
            r.RequestedQty,
            r.BaseRequestLineNum)).ToList();

        SapDocumentRef itRef;
        try
        {
            itRef = _itWriter.CreateInventoryTransfer(
                profileKey:           request.SourceProfile,
                fromWarehouseCode:    request.SourceWarehouse,
                toWarehouseCode:      request.TargetWarehouse,
                transferRef:          transferRef,
                comments:             request.Comments ?? string.Empty,
                lines:                itLines,
                baseRequestDocEntry:  request.BaseRequestDocEntry);
        }
        catch (Exception ex)
        {
            header.Status       = "FAILED";
            header.ErrorMessage = $"Inventory Transfer failed: {ex.Message}";
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex, "LiquiMolyTransfer: OWTR failed | Ref={Ref}", transferRef);
            return new LiquiMolyTransferApplyResult
                { TransferRef = transferRef, Status = "FAILED", ErrorMessage = header.ErrorMessage, Lines = rows };
        }

        header.InventoryTransferDocEntry = itRef.DocEntry;
        header.InventoryTransferDocNum   = itRef.DocNum;
        header.Status                    = "COMPLETED";
        header.CompletedAt               = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "LiquiMolyTransfer: same-company completed | Ref={Ref} | OWTR={ItNum}",
            transferRef, itRef.DocNum);

        // Neon refresh — OWTQ (if linked) + new OWTR
        await RefreshNeonAsync(request, itRef.DocEntry, ct);

        return new LiquiMolyTransferApplyResult
        {
            TransferRef              = transferRef,
            ClientReference          = header.ClientReference,
            Status                   = "COMPLETED",
            ExecutionMode            = "OWTR",
            BaseRequestDocEntry      = header.BaseRequestDocEntry,
            BaseRequestDocNum        = header.BaseRequestDocNum,
            InventoryTransferDocEntry = itRef.DocEntry,
            InventoryTransferDocNum  = itRef.DocNum,
            Lines                    = rows
        };
    }

    // =====================================================
    // PRIVATE — cross-company GI+GR path
    // =====================================================

    private async Task<LiquiMolyTransferApplyResult> ExecuteCrossCompanyAsync(
        CreateLiquiMolyTransferRequest request,
        CacheLiquiMolyTransfer header,
        List<TransferLinePreflightRow> rows,
        CancellationToken ct)
    {
        var transferRef = header.TransferRef;

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
                TransferRef        = transferRef,
                Status             = "RECEIPT_PENDING",
                GoodsIssueDocEntry = giRef.DocEntry,
                GoodsIssueDocNum   = giRef.DocNum,
                ErrorMessage       = header.ErrorMessage,
                Lines              = rows
            };
        }

        header.GoodsReceiptDocEntry = grRef.DocEntry;
        header.GoodsReceiptDocNum   = grRef.DocNum;
        header.Status               = "COMPLETED";
        header.CompletedAt          = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Close the source OWTQ if provided (cross-company: SAP doesn't auto-close it)
        if (request.BaseRequestDocEntry.HasValue)
        {
            // Determine which profile owns the OWTQ — it is in the source or target profile.
            // For cross-company, the OWTQ lives in the same-profile company as the request was raised.
            // We default to TargetProfile (MolasLubes) as the typical OWTQ owner.
            var owtqProfile = request.TargetProfile;
            try
            {
                _itWriter.CloseTransferRequest(owtqProfile, request.BaseRequestDocEntry.Value);
            }
            catch (Exception ex)
            {
                // Non-fatal: log and continue. The transfer itself completed.
                _logger.LogWarning(ex,
                    "LiquiMolyTransfer: could not close OWTQ {DocEntry} after cross-company transfer | Ref={Ref}",
                    request.BaseRequestDocEntry.Value, transferRef);
            }
        }

        _logger.LogInformation(
            "LiquiMolyTransfer: cross-company completed | Ref={Ref} | GI={GiNum} | GR={GrNum}",
            transferRef, giRef.DocNum, grRef.DocNum);

        // Neon refresh
        await RefreshNeonAsync(request, owtrDocEntry: null, ct);

        return new LiquiMolyTransferApplyResult
        {
            TransferRef         = transferRef,
            ClientReference     = header.ClientReference,
            Status              = "COMPLETED",
            ExecutionMode       = "GI_GR",
            BaseRequestDocEntry = header.BaseRequestDocEntry,
            BaseRequestDocNum   = header.BaseRequestDocNum,
            GoodsIssueDocEntry  = giRef.DocEntry,
            GoodsIssueDocNum    = giRef.DocNum,
            GoodsReceiptDocEntry = grRef.DocEntry,
            GoodsReceiptDocNum  = grRef.DocNum,
            Lines               = rows
        };
    }

    // =====================================================
    // PRIVATE — Neon refresh after a successful apply
    // =====================================================

    private async Task RefreshNeonAsync(
        CreateLiquiMolyTransferRequest request,
        int? owtrDocEntry,
        CancellationToken ct)
    {
        var targets = new List<(int DocEntry, string DocType)>();

        if (request.BaseRequestDocEntry.HasValue)
            targets.Add((request.BaseRequestDocEntry.Value, "OWTQ"));

        if (owtrDocEntry.HasValue)
            targets.Add((owtrDocEntry.Value, "OWTR"));

        if (targets.Count == 0) return;

        try
        {
            await _syncService.RefreshDocEntriesAsync(targets, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LiquiMolyTransfer: post-apply Neon refresh failed (non-fatal)");
        }
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

        if (string.Equals(
                request.SourceWarehouse?.Trim(),
                request.TargetWarehouse?.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return ("sourceWarehouse and targetWarehouse cannot be the same.", new());
        }

        if (request.Lines == null || request.Lines.Count == 0)
            return ("At least one line is required.", new());

        if (request.ClientReference?.Length > 64)
            return ("clientReference must be 64 characters or fewer.", new());

        // Read OWTQ for base-request validation
        LiquiMolyDocumentDetails? owtqDoc = null;
        if (request.BaseRequestDocEntry.HasValue)
        {
            owtqDoc = TryReadOwtq(request.SourceProfile, request.TargetProfile,
                                  request.BaseRequestDocEntry.Value, out var owtqErr);
            if (owtqErr != null)
                return (owtqErr, new());
        }

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

            // Validate against OWTQ line if provided
            decimal? openQtyOnRequest = null;
            if (owtqDoc != null && line.BaseRequestLineNum.HasValue)
            {
                var owtqLine = owtqDoc.Lines.FirstOrDefault(l => l.LineNum == line.BaseRequestLineNum.Value);
                if (owtqLine == null)
                {
                    rows.Add(new TransferLinePreflightRow
                    {
                        SourceItemCode    = line.SourceItemCode,
                        BaseRequestLineNum = line.BaseRequestLineNum,
                        RequestedQty      = line.Quantity,
                        Outcome           = "OWTQ_LINE_NOT_FOUND",
                        Message           = $"OWTQ line {line.BaseRequestLineNum} not found on DocEntry {request.BaseRequestDocEntry}."
                    });
                    continue;
                }

                openQtyOnRequest = owtqLine.OpenQty ?? 0m;
                if (line.Quantity > openQtyOnRequest)
                {
                    rows.Add(new TransferLinePreflightRow
                    {
                        SourceItemCode    = line.SourceItemCode,
                        BaseRequestLineNum = line.BaseRequestLineNum,
                        RequestedQty      = line.Quantity,
                        OpenQtyOnRequest  = openQtyOnRequest,
                        Outcome           = "EXCEEDS_OPEN_QTY",
                        Message           = $"Requested {line.Quantity} exceeds open qty {openQtyOnRequest} on OWTQ line {line.BaseRequestLineNum}."
                    });
                    continue;
                }
            }

            // Item mapping
            LiquiMolyMappedLine mapped;
            if (IsSameProfileTransfer(request.SourceProfile, request.TargetProfile))
            {
                mapped = new LiquiMolyMappedLine
                {
                    SourceItemCode = line.SourceItemCode,
                    TargetItemCode = line.SourceItemCode,
                    ArticleNumber  = line.SourceItemCode,
                    Outcome        = "OK"
                };
            }
            else
            {
                try
                {
                    mapped = _mapper.MapLine(
                        request.SourceProfile, request.TargetProfile, line.SourceItemCode);
                }
                catch (Exception ex)
                {
                    rows.Add(new TransferLinePreflightRow
                    {
                        SourceItemCode    = line.SourceItemCode,
                        BaseRequestLineNum = line.BaseRequestLineNum,
                        RequestedQty      = line.Quantity,
                        OpenQtyOnRequest  = openQtyOnRequest,
                        Outcome           = "ERROR",
                        Message           = ex.Message
                    });
                    continue;
                }
            }

            if (!mapped.IsOk)
            {
                rows.Add(new TransferLinePreflightRow
                {
                    SourceItemCode    = line.SourceItemCode,
                    BaseRequestLineNum = line.BaseRequestLineNum,
                    ArticleNumber     = mapped.ArticleNumber,
                    RequestedQty      = line.Quantity,
                    OpenQtyOnRequest  = openQtyOnRequest,
                    Outcome           = mapped.Outcome,
                    Message           = mapped.FailReason
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
                    SourceItemCode    = line.SourceItemCode,
                    BaseRequestLineNum = line.BaseRequestLineNum,
                    ArticleNumber     = mapped.ArticleNumber,
                    SourceItemName    = mapped.SourceItemName,
                    TargetItemCode    = mapped.TargetItemCode,
                    TargetItemName    = mapped.TargetItemName,
                    RequestedQty      = line.Quantity,
                    OpenQtyOnRequest  = openQtyOnRequest,
                    Outcome           = "ERROR",
                    Message           = $"Stock read failed: {ex.Message}"
                });
                continue;
            }

            if (available < line.Quantity)
            {
                rows.Add(new TransferLinePreflightRow
                {
                    SourceItemCode    = line.SourceItemCode,
                    BaseRequestLineNum = line.BaseRequestLineNum,
                    ArticleNumber     = mapped.ArticleNumber,
                    SourceItemName    = mapped.SourceItemName,
                    TargetItemCode    = mapped.TargetItemCode,
                    TargetItemName    = mapped.TargetItemName,
                    RequestedQty      = line.Quantity,
                    OpenQtyOnRequest  = openQtyOnRequest,
                    AvailableQty      = available,
                    Outcome           = TransferLineOutcome.INSUFFICIENT_STOCK,
                    Message           = $"Only {available} available in {request.SourceWarehouse}."
                });
                continue;
            }

            rows.Add(new TransferLinePreflightRow
            {
                SourceItemCode    = line.SourceItemCode,
                BaseRequestLineNum = line.BaseRequestLineNum,
                ArticleNumber     = mapped.ArticleNumber,
                SourceItemName    = mapped.SourceItemName,
                TargetItemCode    = mapped.TargetItemCode,
                TargetItemName    = mapped.TargetItemName,
                RequestedQty      = line.Quantity,
                OpenQtyOnRequest  = openQtyOnRequest,
                AvailableQty      = available,
                Outcome           = TransferLineOutcome.OK
            });
        }

        return (null, rows);
    }

    // =====================================================
    // PRIVATE — read OWTQ with validation
    // =====================================================

    private LiquiMolyDocumentDetails? TryReadOwtq(
        string sourceProfile, string targetProfile,
        int docEntry, out string? error)
    {
        // OWTQ lives in the SAP profile where the request was raised.
        // For same-company this is sourceProfile; for cross-company we try targetProfile (MolasLubes default).
        var owtqProfile = IsSameProfileTransfer(sourceProfile, targetProfile)
            ? sourceProfile
            : targetProfile;

        LiquiMolyDocumentDetails? doc;
        try
        {
            doc = _docReader.GetDocument("OWTQ", docEntry, owtqProfile);
        }
        catch (Exception ex)
        {
            error = $"Could not read OWTQ {docEntry}: {ex.Message}";
            return null;
        }

        if (doc == null)
        {
            error = $"OWTQ DocEntry {docEntry} not found in profile '{owtqProfile}'.";
            return null;
        }

        if (!string.Equals(doc.Header.DocumentStatus, "OPEN", StringComparison.OrdinalIgnoreCase))
        {
            error = $"OWTQ {docEntry} is not OPEN (status: {doc.Header.DocumentStatus}).";
            return null;
        }

        error = null;
        return doc;
    }

    // =====================================================
    // PRIVATE — helpers
    // =====================================================

    private static LiquiMolyTransferApplyResult BuildResultFromAudit(CacheLiquiMolyTransfer t) =>
        new()
        {
            TransferRef               = t.TransferRef,
            ClientReference           = t.ClientReference,
            Status                    = t.Status,
            ExecutionMode             = t.InventoryTransferDocEntry.HasValue ? "OWTR" : "GI_GR",
            BaseRequestDocEntry       = t.BaseRequestDocEntry,
            BaseRequestDocNum         = t.BaseRequestDocNum,
            InventoryTransferDocEntry = t.InventoryTransferDocEntry,
            InventoryTransferDocNum   = t.InventoryTransferDocNum,
            GoodsIssueDocEntry        = t.GoodsIssueDocEntry,
            GoodsIssueDocNum          = t.GoodsIssueDocNum,
            GoodsReceiptDocEntry      = t.GoodsReceiptDocEntry,
            GoodsReceiptDocNum        = t.GoodsReceiptDocNum,
            ErrorMessage              = t.ErrorMessage
        };

    private static bool IsSameProfileTransfer(string sourceProfile, string targetProfile) =>
        string.Equals(sourceProfile?.Trim(), targetProfile?.Trim(), StringComparison.OrdinalIgnoreCase);
}
