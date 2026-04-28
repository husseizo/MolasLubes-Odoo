using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Application.LiquiMolyReplenishment;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Infrastructure.Services.LiquiMolyReplenishment;

/// <summary>
/// Orchestrates the replenishment request lifecycle:
///   GenerateDraft → Submit → Approve/Reject
/// Execution is handled by LiquiMolyReplenishmentExecutionService.
/// </summary>
public class LiquiMolyReplenishmentService
{
    private readonly LiquiMolyReplenishmentAnalyzer  _analyzer;
    private readonly ReplenishmentRefGenerator       _refGen;
    private readonly LiquiMolyRoleService            _roleService;
    private readonly MolasCacheDbContext             _db;
    private readonly ILogger<LiquiMolyReplenishmentService> _logger;

    public LiquiMolyReplenishmentService(
        LiquiMolyReplenishmentAnalyzer  analyzer,
        ReplenishmentRefGenerator       refGen,
        LiquiMolyRoleService            roleService,
        MolasCacheDbContext             db,
        ILogger<LiquiMolyReplenishmentService> logger)
    {
        _analyzer    = analyzer;
        _refGen      = refGen;
        _roleService = roleService;
        _db          = db;
        _logger      = logger;
    }

    // ── Generate Draft ────────────────────────────────────────────────

    public async Task<(string RequestRef, IReadOnlyList<LiquiMolyRecommendationRow> Rows)>
        GenerateDraftAsync(GenerateReplenishmentRequest request, CancellationToken ct = default)
    {
        _roleService.Authorize(request.Actor.SapUserCode, LiquiMolyRole.Planner);

        var rows = _analyzer.Analyze(
            request.SourceProfile,
            request.TargetProfile,
            request.SourceWarehouse,
            request.TargetWarehouse,
            request.TargetDays);

        // Only create lines for items that need replenishment
        var actionable = rows.Where(r => r.SuggestedQty > 0).ToList();

        var requestRef = await GenerateUniqueRequestRefAsync(ct);

        var header = new CacheLiquiMolyReplenishmentRequest
        {
            RequestRef    = requestRef,
            SourceProfile = request.SourceProfile,
            TargetProfile = request.TargetProfile,
            SourceWarehouse = request.SourceWarehouse,
            TargetWarehouse = request.TargetWarehouse,
            Status          = "DRAFT",
            RequestedBySapUser = request.Actor.SapUserCode,
            Comments        = request.Actor.Comment,
            CreatedAt       = DateTime.UtcNow
        };

        foreach (var row in actionable)
        {
            header.Lines.Add(new CacheLiquiMolyReplenishmentRequestLine
            {
                SourceItemCode        = row.SourceItemCode,
                TargetItemCode        = row.TargetItemCode,
                ArticleNumber         = row.ArticleNumber,
                ItemName              = row.ItemName,
                CurrentStockTarget    = row.CurrentStockTarget,
                AvailableSupplierStock = row.AvailableSupplierStock,
                QtySold30d            = row.QtySold30d,
                QtySold60d            = row.QtySold60d,
                QtySold90d            = row.QtySold90d,
                AvgDailySales30d      = row.AvgDailySales30d,
                DaysOfStock           = row.DaysOfStock,
                SuggestedQty          = row.SuggestedQty,
                TrendCategory         = row.TrendCategory,
                Priority              = row.Priority,
                ExecutionStatus       = "PENDING"
            });
        }

        _db.CacheLiquiMolyReplenishmentRequests.Add(header);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Replenishment: draft generated | Ref={Ref} | Lines={Count} | Actor={Actor}",
            requestRef, actionable.Count, request.Actor.SapUserCode);

        return (requestRef, rows);
    }

    // ── Submit for Approval ───────────────────────────────────────────

    public async Task<CacheLiquiMolyReplenishmentRequest> SubmitForApprovalAsync(
        string requestRef, SubmitReplenishmentRequest request, CancellationToken ct = default)
    {
        _roleService.Authorize(request.Actor.SapUserCode, LiquiMolyRole.Planner);

        var header = await LoadOrThrow(requestRef, ct);

        if (header.Status != "DRAFT")
            throw new InvalidOperationException(
                $"Only DRAFT requests can be submitted. Current status: '{header.Status}'.");

        header.Status      = "PENDING_APPROVAL";
        header.SubmittedAt = DateTime.UtcNow;
        // RequestedBySapUser already set at draft time; update if submitter differs
        if (!string.IsNullOrWhiteSpace(request.Actor.SapUserCode))
            header.RequestedBySapUser = request.Actor.SapUserCode;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Replenishment: submitted | Ref={Ref} | Actor={Actor}",
            requestRef, request.Actor.SapUserCode);

        return header;
    }

    // ── Approve ───────────────────────────────────────────────────────

    public async Task<CacheLiquiMolyReplenishmentRequest> ApproveAsync(
        string requestRef, ApproveReplenishmentRequest request, CancellationToken ct = default)
    {
        _roleService.Authorize(request.Actor.SapUserCode, LiquiMolyRole.Supervisor);

        var header = await LoadOrThrow(requestRef, ct);

        if (header.Status != "PENDING_APPROVAL")
            throw new InvalidOperationException(
                $"Only PENDING_APPROVAL requests can be approved. Current status: '{header.Status}'.");

        // Apply optional per-line quantity overrides
        if (request.LineQuantities?.Count > 0)
        {
            var overrideMap = request.LineQuantities.ToDictionary(x => x.LineId, x => x.ApprovedQty);
            foreach (var line in header.Lines)
            {
                if (overrideMap.TryGetValue(line.Id, out var qty))
                {
                    if (qty <= 0)
                        throw new InvalidOperationException(
                            $"ApprovedQty for line {line.Id} must be greater than zero.");
                    line.ApprovedQty = qty;
                }
            }
        }

        header.Status          = "APPROVED";
        header.ApprovedBySapUser = request.Actor.SapUserCode;
        header.ApprovedAt      = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.Actor.Comment))
            header.Comments = request.Actor.Comment;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Replenishment: approved | Ref={Ref} | Actor={Actor}",
            requestRef, request.Actor.SapUserCode);

        return header;
    }

    // ── Reject ────────────────────────────────────────────────────────

    public async Task<CacheLiquiMolyReplenishmentRequest> RejectAsync(
        string requestRef, RejectReplenishmentRequest request, CancellationToken ct = default)
    {
        _roleService.Authorize(request.Actor.SapUserCode, LiquiMolyRole.Supervisor);

        var header = await LoadOrThrow(requestRef, ct);

        if (header.Status != "PENDING_APPROVAL")
            throw new InvalidOperationException(
                $"Only PENDING_APPROVAL requests can be rejected. Current status: '{header.Status}'.");

        header.Status          = "REJECTED";
        header.RejectedBySapUser = request.Actor.SapUserCode;
        header.RejectedAt      = DateTime.UtcNow;
        header.RejectionReason = request.Reason;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Replenishment: rejected | Ref={Ref} | Actor={Actor} | Reason={Reason}",
            requestRef, request.Actor.SapUserCode, request.Reason);

        return header;
    }

    // ── Read ──────────────────────────────────────────────────────────

    public async Task<CacheLiquiMolyReplenishmentRequest?> GetAsync(
        string requestRef, CancellationToken ct = default)
    {
        // Set explicit command timeout for potentially long-running query
        var previousTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(120); // 2 minutes

        try
        {
            return await _db.CacheLiquiMolyReplenishmentRequests
                .AsNoTracking()
                .AsSplitQuery()
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.RequestRef == requestRef, ct);
        }
        finally
        {
            // Restore previous timeout
            _db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    public async Task<(IReadOnlyList<CacheLiquiMolyReplenishmentRequest> Items, bool HasMore)>
        ListAsync(string? status, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        // Set explicit command timeout for potentially long-running query
        var previousTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(120); // 2 minutes

        try
        {
            var query = _db.CacheLiquiMolyReplenishmentRequests.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(r => r.Status == status);

            var items = await query
                .AsNoTracking()
                .AsSplitQuery()
                .OrderByDescending(r => r.CreatedAt)
                .Skip(skip)
                .Take(take + 1)
                .Include(r => r.Lines)
                .ToListAsync(ct);

            var hasMore = items.Count > take;
            if (hasMore) items.RemoveAt(items.Count - 1);

            return (items, hasMore);
        }
        finally
        {
            // Restore previous timeout
            _db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    /// <summary>
    /// Like ListAsync but filters on multiple statuses in a single query,
    /// preserving correct skip/take semantics across the combined result set.
    /// Used by the approvals report which needs APPROVED + REJECTED together.
    /// </summary>
    public async Task<(IReadOnlyList<CacheLiquiMolyReplenishmentRequest> Items, bool HasMore)>
        ListByStatusesAsync(IReadOnlyList<string> statuses, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        // Set explicit command timeout for potentially long-running query
        var previousTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(120); // 2 minutes

        try
        {
            var items = await _db.CacheLiquiMolyReplenishmentRequests
                .AsNoTracking()
                .AsSplitQuery()
                .Where(r => statuses.Contains(r.Status))
                .OrderByDescending(r => r.ApprovedAt ?? r.RejectedAt ?? r.CreatedAt)
                .Skip(skip)
                .Take(take + 1)
                .Include(r => r.Lines)
                .ToListAsync(ct);

            var hasMore = items.Count > take;
            if (hasMore) items.RemoveAt(items.Count - 1);

            return (items, hasMore);
        }
        finally
        {
            // Restore previous timeout
            _db.Database.SetCommandTimeout(previousTimeout);
        }
    }

    // ── Private ──────────────────────────────────────────────────────

    private async Task<CacheLiquiMolyReplenishmentRequest> LoadOrThrow(
        string requestRef, CancellationToken ct)
    {
        var header = await _db.CacheLiquiMolyReplenishmentRequests
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.RequestRef == requestRef, ct);

        if (header == null)
            throw new KeyNotFoundException($"Replenishment request '{requestRef}' not found.");

        return header;
    }

    private async Task<string> GenerateUniqueRequestRefAsync(CancellationToken ct)
    {
        // ReplenishmentRefGenerator is process-local. After an API restart its counter
        // resets, so we must guard against collisions with refs already persisted in SQL.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = _refGen.Generate();
            var exists = await _db.CacheLiquiMolyReplenishmentRequests
                .AnyAsync(r => r.RequestRef == candidate, ct);

            if (!exists)
                return candidate;
        }

        throw new InvalidOperationException(
            "Unable to generate a unique replenishment request reference after 100 attempts.");
    }
}
