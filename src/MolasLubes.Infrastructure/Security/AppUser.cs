namespace MolasLubes.Infrastructure.Security;

public sealed class AppUser
{
    public string SapUserCode  { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool   IsActive     { get; set; } = true;
    public string? DisplayName { get; set; }
}
