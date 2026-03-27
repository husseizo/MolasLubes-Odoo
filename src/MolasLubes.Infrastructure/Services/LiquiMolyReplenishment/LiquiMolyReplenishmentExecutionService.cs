using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.LiquiMolyReplenishment;
using MolasLubes.Application.LiquiMolyTransfers;
using MolasLubes.Domain.Entities.Cache;
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
    private readonly LiquiMolyTransferService  _transferService;
    private readonly LiquiMolyRoleService      _roleService;
    private readonly MolasCacheDbContext       _db;
    private readonly ILogger<LiquiMolyReplenishmentExecutionService> _logger;

    public LiquiMolyReplenishmentExecutionService(
        LiquiMolyTransferService  transferService,
        LiquiMolyRoleService      roleService,
        MolasCacheDbContext       db,
        ILogger<LiquiMolyReplenishmentExecutionService> logger)
    {
        _transferService = transferService;
        _roleService     = roleService;
        _db              = db;
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
        header.ErrorMessage         = result.ErrorMessage;
        header.ExecutedAt           = DateTime.UtcNow;

        header.Status = result.Status switch
        {
            "COMPLETED"      => "EXECUTED",
            "RECEIPT_PENDING" => "PARTIAL",
            _                => "FAILED"
        };

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Replenishment: execution done | Ref={Ref} | TransferRef={TRef} | Status={Status}",
            requestRef, result.TransferRef, header.Status);

        return result;
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
            header.ExecutedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return result;
    }

    // ── Private ──────────────────────────────────────────────────────

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
