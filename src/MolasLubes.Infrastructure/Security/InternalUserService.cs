using Microsoft.EntityFrameworkCore;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Security;

public class InternalUserService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const int MinPasswordLength = 8;

    private readonly MolasCacheDbContext _db;

    public InternalUserService(MolasCacheDbContext db)
    {
        _db = db;
    }

    // ── Login ─────────────────────────────────────────────────────────────

    public async Task<(InternalUser? User, bool IsLocked, DateTime? LockedUntil)> ValidateLoginAsync(
        string username, string password)
    {
        var user = await _db.InternalUsers
            .FirstOrDefaultAsync(u => u.Username == username.ToLowerInvariant());

        if (user == null || !user.IsActive)
            return (null, false, null);

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
            return (null, true, user.LockedUntil);

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
                user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
                return (null, true, user.LockedUntil);
            return (null, false, null);
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (user, false, null);
    }

    // ── CRUD ─────────────────────────────────────────────────────────────

    public async Task<InternalUser?> GetByIdAsync(int id)
        => await _db.InternalUsers.FirstOrDefaultAsync(u => u.Id == id);

    public async Task<(List<InternalUser> Items, int Total)> GetListAsync(
        string? search, string? role, bool? isActive, int skip, int take)
    {
        var q = _db.InternalUsers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(u => u.Username.Contains(search) || u.DisplayName.Contains(search));
        if (!string.IsNullOrWhiteSpace(role))
            q = q.Where(u => u.Role == role);
        if (isActive.HasValue)
            q = q.Where(u => u.IsActive == isActive.Value);

        var total = await q.CountAsync();
        var items = await q.OrderBy(u => u.Username).Skip(skip).Take(take).ToListAsync();
        return (items, total);
    }

    public async Task<(InternalUser? User, string? Error)> CreateAsync(
        string username, string password, string displayName, string role, string? sapUserCode)
    {
        username = username.ToLowerInvariant().Trim();

        if (await _db.InternalUsers.AnyAsync(u => u.Username == username))
            return (null, $"Username '{username}' is already taken.");

        if (password.Length < MinPasswordLength)
            return (null, $"Password must be at least {MinPasswordLength} characters.");

        var user = new InternalUser
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            DisplayName = displayName,
            Role = role,
            SapUserCode = string.IsNullOrWhiteSpace(sapUserCode) ? null : sapUserCode,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.InternalUsers.Add(user);
        await _db.SaveChangesAsync();
        return (user, null);
    }

    public async Task<(InternalUser? User, string? Error)> UpdateAsync(
        int id, string? displayName, string? role, string? sapUserCode, bool? isActive)
    {
        var user = await _db.InternalUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return (null, "User not found.");

        if (displayName != null) user.DisplayName = displayName;
        if (role != null) user.Role = role;
        if (sapUserCode != null) user.SapUserCode = string.IsNullOrWhiteSpace(sapUserCode) ? null : sapUserCode;
        if (isActive.HasValue) user.IsActive = isActive.Value;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (user, null);
    }

    public async Task<(string? GeneratedPassword, string? Error)> ResetPasswordAsync(
        int id, string? newPassword)
    {
        var user = await _db.InternalUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return (null, "User not found.");

        var generated = string.IsNullOrWhiteSpace(newPassword);
        if (generated)
            newPassword = GenerateRandomPassword(12);
        else if (newPassword!.Length < MinPasswordLength)
            return (null, $"Password must be at least {MinPasswordLength} characters.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return (generated ? newPassword : null, null);
    }

    public async Task<string?> ChangePasswordAsync(int id, string currentPassword, string newPassword)
    {
        var user = await _db.InternalUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return "User not found.";

        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return "Current password is incorrect.";

        if (newPassword.Length < MinPasswordLength)
            return $"Password must be at least {MinPasswordLength} characters.";

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return null;
    }

    public async Task DeactivateAsync(int id)
    {
        var user = await _db.InternalUsers.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return;
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<bool> UnlockAsync(string username)
    {
        var user = await _db.InternalUsers
            .FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return false;
        user.LockedUntil = null;
        user.FailedLoginCount = 0;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Audit ─────────────────────────────────────────────────────────────

    public async Task AddAuditEventAsync(
        string eventType,
        int? userId = null,
        int? actorId = null,
        string? detail = null,
        string? ipHint = null)
    {
        _db.AuthAuditEvents.Add(new AuthAuditEvent
        {
            OccurredAt = DateTime.UtcNow,
            EventType = eventType,
            UserId = userId,
            ActorId = actorId,
            Detail = detail,
            IpHint = ipHint
        });
        await _db.SaveChangesAsync();
    }

    // ── Seed Admin ────────────────────────────────────────────────────────

    public async Task<(InternalUser User, string Password)> EnsureAdminAsync()
    {
        const string adminUsername = "admin";
        var existing = await _db.InternalUsers.FirstOrDefaultAsync(u => u.Username == adminUsername);
        if (existing != null)
            throw new InvalidOperationException(
                "Admin user already exists. Use the reset-password endpoint to change the password.");

        var password = GenerateRandomPassword(16);
        var user = new InternalUser
        {
            Username = adminUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            DisplayName = "Administrator",
            Role = LiquiMolyRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.InternalUsers.Add(user);
        await _db.SaveChangesAsync();
        return (user, password);
    }

    // ── Private ───────────────────────────────────────────────────────────

    private static string GenerateRandomPassword(int length = 12)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#";
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }
}
