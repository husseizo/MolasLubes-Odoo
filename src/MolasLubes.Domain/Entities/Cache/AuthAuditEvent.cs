namespace MolasLubes.Domain.Entities.Cache;

public class AuthAuditEvent
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = null!;
    public int? UserId { get; set; }
    public int? ActorId { get; set; }
    public string? Detail { get; set; }
    public string? IpHint { get; set; }
}
