namespace MolasLubes.Domain.Entities.Cache;

public class InternalUser
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? SapUserCode { get; set; }
    public string Role { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }

    public ICollection<InternalUserToken> Tokens { get; set; } = new List<InternalUserToken>();
}
