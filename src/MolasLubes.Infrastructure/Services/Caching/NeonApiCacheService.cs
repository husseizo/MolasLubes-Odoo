using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MolasLubes.Domain.Entities.Neon;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Services.Caching;

public class NeonApiCacheService
{
    private static readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NeonDbContext _db;
    private readonly ILogger<NeonApiCacheService> _logger;

    public NeonApiCacheService(NeonDbContext db, ILogger<NeonApiCacheService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<string?> GetRawAsync(string cacheKey, CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var entry = await _db.ApiCaches
                .AsNoTracking()
                .Where(e => e.CacheKey == cacheKey && e.ExpiresAt > now)
                .Select(e => e.DataJson)
                .FirstOrDefaultAsync(ct);
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Neon cache read failed for key {Key}", cacheKey);
            return null;
        }
    }

    public async Task SetAsync<T>(string cacheKey, string endpoint, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _camelCase);
            var now = DateTime.UtcNow;
            var entry = new NeonApiCache
            {
                CacheKey = cacheKey,
                Endpoint = endpoint,
                DataJson = json,
                CachedAt = now,
                ExpiresAt = now.Add(ttl)
            };

            var existing = await _db.ApiCaches.FindAsync([cacheKey], ct);
            if (existing == null)
                _db.ApiCaches.Add(entry);
            else
            {
                existing.DataJson = json;
                existing.CachedAt = now;
                existing.ExpiresAt = now.Add(ttl);
                existing.Endpoint = endpoint;
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Neon cache write failed for key {Key}", cacheKey);
        }
    }

    public async Task SetRawAsync(string cacheKey, string endpoint, string json, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var entry = new NeonApiCache
            {
                CacheKey = cacheKey,
                Endpoint = endpoint,
                DataJson = json,
                CachedAt = now,
                ExpiresAt = now.Add(ttl)
            };

            var existing = await _db.ApiCaches.FindAsync([cacheKey], ct);
            if (existing == null)
                _db.ApiCaches.Add(entry);
            else
            {
                existing.DataJson = json;
                existing.CachedAt = now;
                existing.ExpiresAt = now.Add(ttl);
                existing.Endpoint = endpoint;
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Neon cache write failed for key {Key}", cacheKey);
        }
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            return await _db.ApiCaches
                .Where(e => e.ExpiresAt <= now)
                .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Neon cache purge failed");
            return 0;
        }
    }
}
