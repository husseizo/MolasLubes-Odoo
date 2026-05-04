using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.LiquiMolyReplenishment;
using MolasLubes.Application.LiquiMolyTransfers;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Integrations.SapB1.DiApi;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Security;
using MolasLubes.Infrastructure.Services.LiquiMolyTransfers;

namespace MolasLubes.Infrastructure.Services.LiquiMolyReplenishment;

/// <summary>
/// Executes an APPROVED replenishment request by delegating to the existing
/// GI → GR transfer saga in LiquiMolyTransferService.
///
/// Status flow on the replenishment request:
///   APPROVED → EXECUTING → EXECUTED  (all lines ok)
///                        → PARTIAL   (some lines failed)
///                        → FAILED    (GI or total failure)
/// </summary>
public class LiquiMolyReplenishmentExecutionService
{
    private readonly LiquiMolyTransferService              _transferService;
    private readonly LiquiMolyRoleService                  _roleService;
    private readonly MolasCacheDbContext                   _db;
    private readonly SapInterCompanySalesOrderWriter       _soWriter;
    private readonly SapPurchaseOrderWriter                _poWriter;
    private readonly SapGoodsReceiptWriter                 _grWriter;
    private readonly ILogger<LiquiMolyReplenishmentExecutionService> _logger;

    public LiquiMolyReplenishmentExecutionService(
        LiquiMolyTransferService              transferService,
        LiquiMolyRoleService                  roleService,
        MolasCacheDbContext                   db,
        SapInterCompanySalesOrderWriter       soWriter,
        SapPurchaseOrderWriter                poWriter,
        SapGoodsReceiptWriter                 grWriter,
        ILogger<LiquiMolyReplenishmentExecutionService> logger)
    {
        _transferService = transferService;
        _roleService     = roleService;
        _db              = db;
        _soWriter        = soWriter;
        _poWriter        = poWriter;
        _grWriter        = grWriter;
        _logger          = logger;
    }

    // ── Execute ───────────────────────────────────────────────────────

    public async Task<LiquiMolyTransferApplyResult> ExecuteApprovedRequestAsync(
        string requestRef,
        ExecuteReplenishmentRequest request,
        CancellationToken ct = default)
    {
        _roleService.Authorize(request.Actor.SapUserCode, LiquiMolyRole.Executor);

        var header = await LoadApprovedOrThrow(requestRef, ct);

        header.Status          = "EXECUTING";
        header.ExecutedBySapUser = request.Actor.SapUserCode;
        await _db.SaveChangesAsync(ct);

        var transferRequest = BuildTransferRequest(header);

        LiquiMolyTransferApplyResult result;
        try
        {
            result = await _transferService.ApplyAsync(transferRequest, ct);
        }
        catch (Exception ex)
        {
            header.Status       = "FAILED";
            header.ErrorMessage = $"Transfer execution threw: {ex.Message}";
            header.ExecutedAt   = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogError(ex,
                "Replenishment execution exception | Ref={Ref}", requestRef);

            throw;
        }

        // Propagate GI/GR references back to the replenishment record
        header.TransferRef          = result.TransferRef;
        header.GoodsIssueDocEntry   = result.GoodsIssueDocEntry;
        header.GoodsIssueDocNum     = result.GoodsIssueDocNum;
        header.GoodsReceiptDocEntry = result.GoodsReceiptDocEntry;
        header.GoodsReceiptDocNum   = result.GoodsReceiptDocNum;
        header.ErrorMessage         = result.ErrorMessage;  // captures preflight errors too
        header.ExecutedAt           = DateTime.UtcNow;

        header.Status = result.Status switch
        {
            "COMPLETED"       => "EXECUTED",
            "RECEIPT_PENDING" => "PARTIAL",
            _                 => "FAILED"
        };

        // Update per-line execution status from the transfer preflight outcome rows
        UpdateLineStatuses(header.Lines, result);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Replenishment: execution done | Ref={Ref} | TransferRef={TRef} | Status={Status} | Error={Error}",
            requestRef, result.TransferRef, header.Status, result.ErrorMessage ?? "-");

        return result;
    }

    // ── Execute via SO → PO → GR (inter-company) ─────────────────────

    /// <summary>
    /// Executes an APPROVED replenishment request via the inter-company Sales Order → Purchase Order → Goods Receipt flow:
    /// <list type="number">
    ///   <item>Phase 1 — Creates a Sales Order (ORDR) in MolasLubes for customer SHP00118</item>
    ///   <item>Phase 2 — Creates a Purchase Order (OPOR) in AutoHub for vendor SUP00001</item>
    ///   <item>Phase 3 — Creates a Goods Receipt PO (OPDN) in AutoHub against the PO</item>
    /// </list>
    /// Each phase saves its DocEntry/DocNum immediately so a retry can resume from where it failed.
    /// </summary>
    public async Task<LiquiMolyTransferApplyResult> ExecuteWithSalesAndPurchaseAsync(
        string requestRef,
        ExecuteReplenishmentRequest request,
        CancellationToken ct = default)
    {
        _roleService.Authorize(request.Actor.SapUserCode, LiquiMolyRole.Executor);

        var header = await LoadApprovedOrThrow(requestRef, ct);

        header.Status            = "EXECUTING";
        header.ExecutionMode     = "SALES_PURCHASE";
        header.ExecutedBySapUser = request.Actor.SapUserCode;
        await _db.SaveChangesAsync(ct);

        var soLines = header.Lines
            .Select(l => new InterCompanySalesOrderLine(
                SourceItemCode: l.SourceItemCode,
                Quantity:       l.ApprovedQty ?? l.SuggestedQty))
            .Where(l => l.Quantity > 0)
            .ToList();

        // ── Phase 1: Sales Order in MolasLubes ───────────────────────
        SapDocumentRef soRef;
        try
        {
            soRef = _soWriter.CreateSalesOrder(
                profileKey:  header.SourceProfile,
                transferRef: header.RequestRef,
                comments:    $"Replenishment {header.RequestRef}",
                lines:       soLines);

            header.SalesOrderDocEntry = soRef.DocEntry;
            header.SalesOrderDocNum   = int.TryParse(soRef.DocNum, out var soNum) ? soNum : null;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Replenishment SO created | Ref={Ref} | SODocEntry={Entry} | SODocNum={Num}",
                requestRef, soRef.DocEntry, soRef.DocNum);
        }
        catch (Exception ex)
        {
            header.Status       = "FAILED";
            header.ErrorMessage = $"SO creation failed: {ex.Message}";
            header.ExecutedAt   = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Replenishment SO failed | Ref={Ref}", requestRef);
            throw;
        }

        // ── Phase 2: Purchase Order in AutoHub ───────────────────────
        SapDocumentRef poRef;
        try
        {
            var poLines = header.Lines
                .Select(l => new PurchaseOrderLine(
                    TargetItemCode: l.TargetItemCode,
                    Quantity:       l.ApprovedQty ?? l.SuggestedQty,
                    UnitPrice:      0m,          // SAP will pull price from vendor price list; SO price drives the SO side
                    WarehouseCode:  header.TargetWarehouse))
                .Where(l => l.Quantity > 0)
                .ToList();

            poRef = _poWriter.CreatePurchaseOrder(
                profileKey:  header.TargetProfile,
                transferRef: header.RequestRef,
                comments:    $"Replenishment {header.RequestRef} — SO {soRef.DocNum}",
                lines:       poLines);

            header.PurchaseOrderDocEntry = poRef.DocEntry;
            header.PurchaseOrderDocNum   = int.TryParse(poRef.DocNum, out var poNum) ? poNum : null;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Replenishment PO created | Ref={Ref} | PODocEntry={Entry} | PODocNum={Num}",
                requestRef, poRef.DocEntry, poRef.DocNum);
        }
        catch (Exception ex)
        {
            header.Status       = "PARTIAL";  // SO exists, PO failed
            header.ErrorMessage = $"PO creation failed (SO {soRef.DocNum} exists): {ex.Message}";
            header.ExecutedAt   = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Replenishment PO failed | Ref={Ref} | SODocEntry={Entry}", requestRef, soRef.DocEntry);
            throw;
        }

        // ── Phase 3: Goods Receipt PO in AutoHub ─────────────────────
        try
        {
            var grRef = _grWriter.CreateGoodsReceiptFromPurchaseOrder(
                profileKey:  header.TargetProfile,
                poDocEntry:  poRef.DocEntry,
                transferRef: header.RequestRef,
                comments:    $"Replenishment {header.RequestRef} — PO {poRef.DocNum}");

            header.GoodsReceiptDocEntry = grRef.DocEntry;
            header.GoodsReceiptDocNum   = grRef.DocNum;
            header.Status               = "EXECUTED";
            header.ExecutedAt           = DateTime.UtcNow;

            foreach (var line in header.Lines)
                line.ExecutionStatus = "EXECUTED";

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Replenishment GR PO created | Ref={Ref} | GRDocEntry={Entry} | GRDocNum={Num} | Status=EXECUTED",
                requestRef, grRef.DocEntry, grRef.DocNum);
        }
        catch (Exception ex)
        {
            header.Status       = "PARTIAL";  // SO + PO exist, GR failed
            header.ErrorMessage = $"GR creation failed (SO {soRef.DocNum}, PO {poRef.DocNum} exist): {ex.Message}";
            header.ExecutedAt   = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Replenishment GR PO failed | Ref={Ref} | PODocEntry={Entry}", requestRef, poRef.DocEntry);
            throw;
        }

        return new LiquiMolyTransferApplyResult
        {
            TransferRef           = header.RequestRef,
            Status                = "COMPLETED",
            ExecutionMode         = "SALES_PURCHASE",
            SalesOrderDocEntry    = header.SalesOrderDocEntry,
            SalesOrderDocNum      = header.SalesOrderDocNum,
            PurchaseOrderDocEntry = header.PurchaseOrderDocEntry,
            PurchaseOrderDocNum   = header.PurchaseOrderDocNum,
            GoodsReceiptDocEntry  = header.GoodsReceiptDocEntry,
            GoodsReceiptDocNum    = header.GoodsReceiptDocNum,
            Lines                 = soLines
                .Select(l => new TransferLinePreflightRow
                {
                    SourceItemCode = l.SourceItemCode,
                    RequestedQty   = l.Quantity,
                    Outcome        = TransferLineOutcome.OK
                })
                .ToList()
        };
    }

    // ── Retry ─────────────────────────────────────────────────────────

    public async Task<LiquiMolyTransferApplyResult> RetryExecutionAsync(
        string requestRef,
        ExecuteReplenishmentRequest request,
        CancellationToken ct = default)
    {
        _roleService.Authorize(request.Actor.SapUserCode, LiquiMolyRole.Executor);

        var header = await _db.CacheLiquiMolyReplenishmentRequests
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.RequestRef == requestRef, ct)
            ?? throw new KeyNotFoundException($"Replenishment request '{requestRef}' not found.");

        if (header.Status != "PARTIAL" && header.Status != "FAILED")
            throw new InvalidOperationException(
                $"Only PARTIAL or FAILED requests can be retried. Status: '{header.Status}'.");

        if (string.Equals(header.ExecutionMode, "SALES_PURCHASE", StringComparison.OrdinalIgnoreCase))
        {
            if (!header.PurchaseOrderDocEntry.HasValue)
                throw new InvalidOperationException(
                    "No PurchaseOrderDocEntry on record — cannot retry inter-company GR. Re-execute from scratch.");

            SapDocumentRef grRef;
            try
            {
                grRef = _grWriter.FindGoodsReceiptPoByTransferRef(header.TargetProfile, header.RequestRef)
                    ?? _grWriter.CreateGoodsReceiptFromPurchaseOrder(
                        profileKey:  header.TargetProfile,
                        poDocEntry:  header.PurchaseOrderDocEntry.Value,
                        transferRef: header.RequestRef,
                        comments:    $"Retry GR for replenishment {header.RequestRef} — PO {header.PurchaseOrderDocNum}");
            }
            catch (Exception ex)
            {
                header.Status       = "PARTIAL";
                header.ErrorMessage = $"GR retry failed (PO {header.PurchaseOrderDocNum} exists): {ex.Message}";
                await _db.SaveChangesAsync(ct);
                _logger.LogError(ex,
                    "Replenishment GR retry failed | Ref={Ref} | PODocEntry={Entry}",
                    requestRef, header.PurchaseOrderDocEntry.Value);
                throw;
            }

            header.GoodsReceiptDocEntry = grRef.DocEntry;
            header.GoodsReceiptDocNum   = grRef.DocNum;
            header.ErrorMessage         = null;
            header.Status               = "EXECUTED";
            header.ExecutedAt           = DateTime.UtcNow;

            foreach (var line in header.Lines)
                line.ExecutionStatus = "EXECUTED";

            await _db.SaveChangesAsync(ct);

            return new LiquiMolyTransferApplyResult
            {
                TransferRef           = header.RequestRef,
                Status                = "COMPLETED",
                ExecutionMode         = "SALES_PURCHASE",
                SalesOrderDocEntry    = header.SalesOrderDocEntry,
                SalesOrderDocNum      = header.SalesOrderDocNum,
                PurchaseOrderDocEntry = header.PurchaseOrderDocEntry,
                PurchaseOrderDocNum   = header.PurchaseOrderDocNum,
                GoodsReceiptDocEntry  = header.GoodsReceiptDocEntry,
                GoodsReceiptDocNum    = header.GoodsReceiptDocNum,
                ErrorMessage          = null,
                Lines = header.Lines
                    .Select(l => new TransferLinePreflightRow
                    {
                        SourceItemCode = l.SourceItemCode,
                        RequestedQty   = l.ApprovedQty ?? l.SuggestedQty,
                        Outcome        = TransferLineOutcome.OK
                    })
                    .ToList()
            };
        }

        if (string.IsNullOrWhiteSpace(header.TransferRef))
            throw new InvalidOperationException(
                "No TransferRef on record — cannot retry. Re-execute from scratch.");

        // Delegate to the transfer saga's retry-receipt path
        var result = await _transferService.RetryReceiptAsync(header.TransferRef!, ct);

        header.GoodsReceiptDocEntry = result.GoodsReceiptDocEntry;
        header.GoodsReceiptDocNum   = result.GoodsReceiptDocNum;
        header.ErrorMessage         = result.ErrorMessage;

        header.Status = result.Status switch
        {
            "COMPLETED" => "EXECUTED",
            _           => header.Status  // stay in PARTIAL/FAILED
        };

        if (header.Status == "EXECUTED")
        {
            header.ExecutedAt = DateTime.UtcNow;
            // GR succeeded on retry — promote all GI_ISSUED lines to EXECUTED
            foreach (var line in header.Lines.Where(l => l.ExecutionStatus == "GI_ISSUED"))
                line.ExecutionStatus = "EXECUTED";
        }

        await _db.SaveChangesAsync(ct);

        return result;
    }

    // ── Private ──────────────────────────────────────────────────────

    /// <summary>
    /// Propagates per-line outcomes from the transfer result back to the
    /// replenishment request lines so the execution audit is complete.
    ///
    /// Status values:
    ///   EXECUTED  — line shipped and received (GI + GR both posted)
    ///   GI_ISSUED — GI posted, GR pending (RECEIPT_PENDING / PARTIAL)
    ///   FAILED    — preflight rejected this line or overall failure
    /// </summary>
    private static void UpdateLineStatuses(
        IEnumerable<CacheLiquiMolyReplenishmentRequestLine> lines,
        LiquiMolyTransferApplyResult result)
    {
        // Build lookup from transfer preflight rows by SourceItemCode
        var outcomeLookup = result.Lines
            .ToDictionary(l => l.SourceItemCode, l => l, StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (!outcomeLookup.TryGetValue(line.SourceItemCode, out var row))
            {
                // Line not present in result — shouldn't happen; mark conservatively
                line.ExecutionStatus  = result.Status == "COMPLETED" ? "EXECUTED" : "FAILED";
                line.ExecutionMessage = "Line not found in transfer result.";
                continue;
            }

            if (row.Outcome != "OK")
            {
                line.ExecutionStatus  = "FAILED";
                line.ExecutionMessage = row.Message;
            }
            else
            {
                line.ExecutionStatus = result.Status switch
                {
                    "COMPLETED"       => "EXECUTED",
                    "RECEIPT_PENDING" => "GI_ISSUED",  // GI posted, GR still pending
                    _                 => "FAILED"
                };
                line.ExecutionMessage = result.Status == "FAILED" ? result.ErrorMessage : null;
            }
        }
    }

    private async Task<CacheLiquiMolyReplenishmentRequest> LoadApprovedOrThrow(
        string requestRef, CancellationToken ct)
    {
        var header = await _db.CacheLiquiMolyReplenishmentRequests
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.RequestRef == requestRef, ct);

        if (header == null)
            throw new KeyNotFoundException($"Replenishment request '{requestRef}' not found.");

        if (header.Status != "APPROVED")
            throw new InvalidOperationException(
                $"Only APPROVED requests can be executed. Current status: '{header.Status}'.");

        return header;
    }

    private static CreateLiquiMolyTransferRequest BuildTransferRequest(
        CacheLiquiMolyReplenishmentRequest header)
    {
        var lines = header.Lines
            .Select(l => new TransferLineRequest
            {
                // SourceItemCode is the MolasLubes internal code (LUB1000xx)
                SourceItemCode = l.SourceItemCode,
                Quantity       = l.ApprovedQty ?? l.SuggestedQty
            })
            .Where(l => l.Quantity > 0)
            .ToList();

        return new CreateLiquiMolyTransferRequest
        {
            SourceProfile   = header.SourceProfile,
            TargetProfile   = header.TargetProfile,
            SourceWarehouse = header.SourceWarehouse,
            TargetWarehouse = header.TargetWarehouse,
            Comments        = $"Replenishment {header.RequestRef}",
            Lines           = lines
        };
    }
}
