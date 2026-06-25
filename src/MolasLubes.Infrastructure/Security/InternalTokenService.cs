using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Security;

public class InternalTokenService
{
    private readonly MolasCacheDbContext _db;

    public InternalTokenService(MolasCacheDbContext db)
    {
        _db = db;
    }

    public async Task<string> IssueTokenAsync(int userId, int expiryDays = 30, string? deviceHint = null)
    {
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(rawBytes);

        _db.InternalUserTokens.Add(new InternalUserToken
        {
            UserId = userId,
            TokenHash = HashToken(rawToken),
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
            DeviceHint = deviceHint
        });
        await _db.SaveChangesAsync();

        return rawToken;
    }

    public async Task<InternalUser?> ResolveTokenAsync(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;
        var hash = HashToken(rawToken);
        return await _db.InternalUserTokens
            .Where(t => t.TokenHash == hash
                     && t.RevokedAt == null
                     && t.ExpiresAt > DateTime.UtcNow)
            .Select(t => t.User)
            .FirstOrDefaultAsync();
    }

    public async Task RevokeTokenAsync(string rawToken)
    {
        var hash = HashToken(rawToken);
        var record = await _db.InternalUserTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null);
        if (record != null)
        {
            record.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<int> RevokeAllForUserAsync(int userId)
    {
        var active = await _db.InternalUserTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        if (active.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var t in active)
            t.RevokedAt = now;
        await _db.SaveChangesAsync();
        return active.Count;
    }

    public static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToBase64String(bytes);
    }
}
