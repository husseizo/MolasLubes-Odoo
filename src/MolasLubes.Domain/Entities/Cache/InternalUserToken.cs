namespace MolasLubes.Domain.Entities.Cache;

public class InternalUserToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public InternalUser User { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? DeviceHint { get; set; }
}
