namespace MolasLubes.Domain.Entities.Neon;

public class NeonApiCache
{
    public string CacheKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string DataJson { get; set; } = string.Empty;
    public DateTime CachedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
