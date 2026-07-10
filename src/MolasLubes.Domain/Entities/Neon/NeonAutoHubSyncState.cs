namespace MolasLubes.Domain.Entities.Neon;

public class NeonAutoHubSyncState
{
    public string DocType      { get; set; } = null!;
    public DateTime LastSyncedAt { get; set; }
}
