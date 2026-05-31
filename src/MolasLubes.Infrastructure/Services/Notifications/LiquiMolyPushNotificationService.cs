using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MolasLubes.Application.Notifications;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Persistence;
using MolasLubes.Infrastructure.Security;

namespace MolasLubes.Infrastructure.Services.Notifications;

public class LiquiMolyPushNotificationService
{
    private readonly MolasCacheDbContext _db;
    private readonly LiquiMolyPermissionsOptions _permissions;
    private readonly ApnsOptions _apnsOptions;
    private readonly ApnsNotificationSender _apnsSender;
    private readonly ILogger<LiquiMolyPushNotificationService> _logger;

    public LiquiMolyPushNotificationService(
        MolasCacheDbContext db,
        IOptions<LiquiMolyPermissionsOptions> permissionOptions,
        IOptions<ApnsOptions> apnsOptions,
        ApnsNotificationSender apnsSender,
        ILogger<LiquiMolyPushNotificationService> logger)
    {
        _db = db;
        _permissions = permissionOptions.Value;
        _apnsOptions = apnsOptions.Value;
        _apnsSender = apnsSender;
        _logger = logger;
    }

    public async Task<DeviceTokenMutationResponse> RegisterDeviceTokenAsync(
        string authenticatedSapUserCode,
        RegisterDeviceTokenRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authenticatedSapUserCode))
            throw new UnauthorizedAccessException("Authenticated SAP user not found in token.");

        if (request is null)
            throw new ArgumentException("Request body is required.");

        if (!string.Equals(request.SapUserCode?.Trim(), authenticatedSapUserCode.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("sapUserCode must match authenticated user.");

        if (!string.Equals(request.Platform?.Trim(), "ios", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only platform='ios' is supported.");

        if (string.IsNullOrWhiteSpace(request.DeviceToken))
            throw new ArgumentException("deviceToken is required.");

        var token = request.DeviceToken.Trim();
        var bundleId = ResolveBundleId(request.BundleId);
        var userCode = authenticatedSapUserCode.Trim();

        var existing = await _db.CacheNotificationDeviceTokens
            .FirstOrDefaultAsync(x =>
                x.Platform == "ios" &&
                x.DeviceToken == token &&
                x.BundleId == bundleId, ct);

        if (existing == null)
        {
            _db.CacheNotificationDeviceTokens.Add(new CacheNotificationDeviceToken
            {
                Platform = "ios",
                DeviceToken = token,
                BundleId = bundleId,
                SapUserCode = userCode,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DeactivatedAt = null,
                FailureCount = 0,
                LastError = null
            });
        }
        else
        {
            existing.SapUserCode = userCode;
            existing.IsActive = true;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.DeactivatedAt = null;
            existing.FailureCount = 0;
            existing.LastError = null;
        }

        await _db.SaveChangesAsync(ct);

        return new DeviceTokenMutationResponse
        {
            Success = true,
            Affected = 1,
            Message = "Device token registered."
        };
    }

    public async Task<DeviceTokenMutationResponse> RemoveDeviceTokenAsync(
        string authenticatedSapUserCode,
        RemoveDeviceTokenRequest? request,
        string? tokenFromQuery,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(authenticatedSapUserCode))
            throw new UnauthorizedAccessException("Authenticated SAP user not found in token.");

        var sapUserCode = authenticatedSapUserCode.Trim();
        var token = FirstNonEmpty(request?.DeviceToken, tokenFromQuery);
        var bundleId = FirstNonEmpty(request?.BundleId, _apnsOptions.BundleId);
        var platform = FirstNonEmpty(request?.Platform, "ios");

        IQueryable<CacheNotificationDeviceToken> query = _db.CacheNotificationDeviceTokens
            .Where(x =>
                x.SapUserCode == sapUserCode &&
                x.IsActive);

        if (!string.IsNullOrWhiteSpace(token))
            query = query.Where(x => x.DeviceToken == token);

        if (!string.IsNullOrWhiteSpace(bundleId))
            query = query.Where(x => x.BundleId == bundleId);

        if (!string.IsNullOrWhiteSpace(platform))
            query = query.Where(x => x.Platform == platform);

        var rows = await query.ToListAsync(ct);
        var nowUtc = DateTime.UtcNow;

        foreach (var row in rows)
        {
            row.IsActive = false;
            row.UpdatedAt = nowUtc;
            row.DeactivatedAt = nowUtc;
        }

        await _db.SaveChangesAsync(ct);

        return new DeviceTokenMutationResponse
        {
            Success = true,
            Affected = rows.Count,
            Message = rows.Count == 0
                ? "No active token found for this user."
                : "Device token removed."
        };
    }

    public async Task NotifyPendingApprovalAsync(CacheLiquiMolyReplenishmentRequest request, CancellationToken ct = default)
    {
        var recipients = DistinctSapUsers(_permissions.Supervisors, _permissions.Admins);
        var isMolasTransfer = IsMolasWarehouseTransfer(request);
        await NotifyAsync(
            recipients,
            request,
            status: "PENDING_APPROVAL",
            title: isMolasTransfer ? "Molas transfer pending approval" : "Request pending approval",
            body: $"{request.RequestRef} is waiting for approval.",
            ct);
    }

    public async Task NotifyApprovedAsync(CacheLiquiMolyReplenishmentRequest request, CancellationToken ct = default)
    {
        var recipients = IsMolasWarehouseTransfer(request)
            ? DistinctSapUsers(new[] { request.RequestedBySapUser })
            : DistinctSapUsers(
                new[] { request.RequestedBySapUser },
                _permissions.Executors,
                _permissions.Planners,
                _permissions.Admins);

        await NotifyAsync(
            recipients,
            request,
            status: "APPROVED",
            title: "Request approved",
            body: $"{request.RequestRef} has been approved.",
            ct);
    }

    public async Task NotifyRejectedAsync(CacheLiquiMolyReplenishmentRequest request, CancellationToken ct = default)
    {
        var recipients = DistinctSapUsers(new[] { request.RequestedBySapUser });
        await NotifyAsync(
            recipients,
            request,
            status: "REJECTED",
            title: "Request rejected",
            body: $"{request.RequestRef} was rejected.",
            ct);
    }

    public async Task NotifyExecutionStartedAsync(CacheLiquiMolyReplenishmentRequest request, CancellationToken ct = default)
    {
        var recipients = ExecutionRecipients(request);
        await NotifyAsync(
            recipients,
            request,
            status: "EXECUTING",
            title: "Execution started",
            body: $"{request.RequestRef} execution has started.",
            ct);
    }

    public async Task NotifyExecutionResultAsync(CacheLiquiMolyReplenishmentRequest request, CancellationToken ct = default)
    {
        var recipients = ExecutionRecipients(request);
        var status = request.Status;
        var title = status switch
        {
            "EXECUTED" => "Execution completed",
            "PARTIAL" => "Execution partial",
            "FAILED" => "Execution failed",
            _ => "Execution update"
        };
        var body = status switch
        {
            "EXECUTED" => $"{request.RequestRef} execution completed successfully.",
            "PARTIAL" => $"{request.RequestRef} execution completed with partial result.",
            "FAILED" => $"{request.RequestRef} execution failed.",
            _ => $"{request.RequestRef} execution status is {status}."
        };

        await NotifyAsync(recipients, request, status, title, body, ct);
    }

    private async Task NotifyAsync(
        IReadOnlyCollection<string> sapUsers,
        CacheLiquiMolyReplenishmentRequest request,
        string status,
        string title,
        string body,
        CancellationToken ct)
    {
        if (sapUsers.Count == 0)
            return;

        if (!_apnsOptions.IsConfigured || !_apnsSender.IsConfigured())
        {
            _logger.LogWarning(
                "Push skipped (APNS not configured) | Ref={Ref} | Status={Status}",
                request.RequestRef, status);
            return;
        }

        var tokens = await _db.CacheNotificationDeviceTokens
            .Where(x =>
                x.IsActive &&
                x.Platform == "ios" &&
                x.BundleId == _apnsOptions.BundleId &&
                sapUsers.Contains(x.SapUserCode))
            .ToListAsync(ct);

        if (tokens.Count == 0)
            return;

        var payload = new
        {
            aps = new
            {
                alert = new { title, body },
                sound = "default",
                badge = 1
            },
            type = IsMolasWarehouseTransfer(request)
                ? "MOLAS_TRANSFER_STATUS_CHANGED"
                : "REPLENISHMENT_STATUS_CHANGED",
            requestKind = IsMolasWarehouseTransfer(request)
                ? "MOLAS_WAREHOUSE_TRANSFER"
                : "AUTOHUB_REPLENISHMENT",
            requestRef = request.RequestRef,
            status,
            route = $"/replenishments/{request.RequestRef}",
            sourceProfile = request.SourceProfile,
            targetProfile = request.TargetProfile,
            sourceWarehouse = request.SourceWarehouse,
            targetWarehouse = request.TargetWarehouse
        };

        var successCount = 0;
        foreach (var token in tokens)
        {
            var result = await _apnsSender.SendAsync(token.DeviceToken, payload, ct);

            token.UpdatedAt = DateTime.UtcNow;
            token.LastPushedAt = result.IsSuccess ? DateTime.UtcNow : token.LastPushedAt;
            token.LastError = result.IsSuccess ? null : result.Error;
            token.FailureCount = result.IsSuccess ? 0 : token.FailureCount + 1;

            if (result.IsPermanentTokenFailure)
            {
                token.IsActive = false;
                token.DeactivatedAt = DateTime.UtcNow;
            }

            if (result.IsSuccess)
                successCount++;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Push sent | Ref={Ref} | Status={Status} | Targets={Targets} | Success={Success}",
            request.RequestRef, status, tokens.Count, successCount);
    }

    private IReadOnlyCollection<string> ExecutionRecipients(CacheLiquiMolyReplenishmentRequest request) =>
        DistinctSapUsers(
            new[] { request.RequestedBySapUser },
            _permissions.Executors,
            _permissions.Planners,
            _permissions.Admins);

    private static IReadOnlyCollection<string> DistinctSapUsers(params IEnumerable<string?>[] sets)
    {
        var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in sets)
        {
            foreach (var user in set)
            {
                var normalized = user?.Trim();
                if (!string.IsNullOrWhiteSpace(normalized))
                    users.Add(normalized);
            }
        }
        return users.ToList();
    }

    private string ResolveBundleId(string? requestBundleId)
    {
        var fromRequest = requestBundleId?.Trim();
        if (string.IsNullOrWhiteSpace(fromRequest))
            return _apnsOptions.BundleId;

        if (string.IsNullOrWhiteSpace(_apnsOptions.BundleId))
            return fromRequest;

        if (!string.Equals(fromRequest, _apnsOptions.BundleId, StringComparison.Ordinal))
            throw new ArgumentException($"bundleId must be '{_apnsOptions.BundleId}'.");

        return fromRequest;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static bool IsMolasWarehouseTransfer(CacheLiquiMolyReplenishmentRequest request) =>
        string.Equals(request.SourceProfile, "MolasLubes", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(request.TargetProfile, "MolasLubes", StringComparison.OrdinalIgnoreCase);
}
