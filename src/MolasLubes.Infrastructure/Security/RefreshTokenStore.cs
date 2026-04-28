using System.Collections.Concurrent;

namespace MolasLubes.Infrastructure.Security;

/// <summary>
/// In-memory refresh token store.
/// For production with multiple instances, replace with Redis or database.
/// </summary>
public class RefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshTokenSession> _tokens = new();

    /// <summary>
    /// Stores a new refresh token session.
    /// </summary>
    public void Store(RefreshTokenSession session)
    {
        _tokens[session.Token] = session;
    }

    /// <summary>
    /// Retrieves and validates a refresh token session.
    /// Returns null if token doesn't exist or has expired.
    /// </summary>
    public RefreshTokenSession? GetAndValidate(string token)
    {
        if (!_tokens.TryGetValue(token, out var session))
            return null;

        if (session.ExpiresAt < DateTime.UtcNow)
        {
            // Token expired - remove it
            _tokens.TryRemove(token, out _);
            return null;
        }

        return session;
    }

    /// <summary>
    /// Revokes a refresh token (logout).
    /// </summary>
    public void Revoke(string token)
    {
        _tokens.TryRemove(token, out _);
    }

    /// <summary>
    /// Revokes all refresh tokens for a user.
    /// </summary>
    public void RevokeAllForUser(string sapUserCode)
    {
        var tokensToRemove = _tokens.Values
            .Where(s => s.SapUserCode == sapUserCode)
            .Select(s => s.Token)
            .ToList();

        foreach (var token in tokensToRemove)
        {
            _tokens.TryRemove(token, out _);
        }
    }

    /// <summary>
    /// Cleans up expired tokens (called periodically by background job).
    /// </summary>
    public int CleanupExpired()
    {
        var expiredTokens = _tokens.Values
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .Select(s => s.Token)
            .ToList();

        foreach (var token in expiredTokens)
        {
            _tokens.TryRemove(token, out _);
        }

        return expiredTokens.Count;
    }
}
