using System.Collections.Concurrent;

namespace MolasLubes.Infrastructure.Security;

public sealed class RefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshEntry> _store = new();

    public void Save(string refreshToken, string sapUserCode, int expiryDays)
    {
        _store[refreshToken] = new RefreshEntry(sapUserCode, DateTime.UtcNow.AddDays(expiryDays));
        PruneExpired();
    }

    public string? Consume(string refreshToken)
    {
        if (_store.TryRemove(refreshToken, out var entry) && entry.Expiry > DateTime.UtcNow)
            return entry.SapUserCode;
        return null;
    }

    private void PruneExpired()
    {
        var expired = _store.Where(kv => kv.Value.Expiry <= DateTime.UtcNow).Select(kv => kv.Key).ToList();
        foreach (var k in expired) _store.TryRemove(k, out _);
    }

    private sealed record RefreshEntry(string SapUserCode, DateTime Expiry);
}
