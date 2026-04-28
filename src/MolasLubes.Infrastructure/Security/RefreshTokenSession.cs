namespace MolasLubes.Infrastructure.Security;

/// <summary>
/// Represents an active refresh token session.
/// Stored in-memory for simplicity. For production with multiple instances,
/// consider using Redis or database storage.
/// </summary>
public class RefreshTokenSession
{
    public string Token { get; set; } = string.Empty;
    public string SapUserCode { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
