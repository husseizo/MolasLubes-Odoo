using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MolasLubes.Infrastructure.Security;

public sealed class JwtService
{
    private readonly JwtOptions              _opts;
    private readonly LiquiMolyPermissionsOptions _perms;

    public JwtService(IOptions<JwtOptions> opts, IOptions<LiquiMolyPermissionsOptions> perms)
    {
        _opts  = opts.Value;
        _perms = perms.Value;
    }

    public string IssueAccessToken(string sapUserCode)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var role  = ResolveRole(sapUserCode);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,  sapUserCode),
            new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("sapUserCode", sapUserCode)
        };

        var token = new JwtSecurityToken(
            issuer:             _opts.Issuer,
            audience:           _opts.Audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(_opts.ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string IssueRefreshToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SecretKey));

        try
        {
            return handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = key,
                ValidateIssuer           = true,
                ValidIssuer              = _opts.Issuer,
                ValidateAudience         = true,
                ValidAudience            = _opts.Audience,
                ClockSkew                = TimeSpan.Zero
            }, out _);
        }
        catch { return null; }
    }

    public string ResolveRolePublic(string sapUserCode) => ResolveRole(sapUserCode);

    internal string ResolveRole(string sapUserCode)
    {
        if (_perms.Admins.Contains(sapUserCode,   StringComparer.OrdinalIgnoreCase)) return LiquiMolyRole.Admin;
        if (_perms.Supervisors.Contains(sapUserCode, StringComparer.OrdinalIgnoreCase)) return LiquiMolyRole.Supervisor;
        if (_perms.Executors.Contains(sapUserCode,   StringComparer.OrdinalIgnoreCase)) return LiquiMolyRole.Executor;
        if (_perms.Planners.Contains(sapUserCode,    StringComparer.OrdinalIgnoreCase)) return LiquiMolyRole.Planner;
        return LiquiMolyRole.Viewer;
    }
}
