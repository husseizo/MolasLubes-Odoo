namespace MolasLubes.Infrastructure.Services.Notifications;

public class ApnsOptions
{
    public const string SectionName = "Apns";

    public string KeyId { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string BundleId { get; set; } = string.Empty;
    public string AuthKeyPath { get; set; } = string.Empty;
    public string Env { get; set; } = "production";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(KeyId) &&
        !string.IsNullOrWhiteSpace(TeamId) &&
        !string.IsNullOrWhiteSpace(BundleId) &&
        !string.IsNullOrWhiteSpace(AuthKeyPath);

    public bool IsProduction =>
        string.Equals(Env, "production", StringComparison.OrdinalIgnoreCase);
}
