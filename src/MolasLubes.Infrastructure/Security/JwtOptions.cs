namespace MolasLubes.Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string SecretKey  { get; set; } = "";
    public string Issuer     { get; set; } = "MolasLubesApi";
    public string Audience   { get; set; } = "MolasLubesClients";
    public int    ExpiryMinutes      { get; set; } = 60;
    public int    RefreshExpiryDays  { get; set; } = 30;
}
