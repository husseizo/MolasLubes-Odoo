namespace MolasLubes.Domain.Entities.Cache;

/// <summary>
/// Stores mobile push tokens mapped to SAP users.
/// Used for APNs notifications in replenishment workflow events.
/// </summary>
public class CacheNotificationDeviceToken
{
    public int Id { get; set; }

    public string Platform { get; set; } = "ios";
    public string DeviceToken { get; set; } = null!;
    public string SapUserCode { get; set; } = null!;
    public string BundleId { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeactivatedAt { get; set; }

    public DateTime? LastPushedAt { get; set; }
    public int FailureCount { get; set; }
    public string? LastError { get; set; }
}

