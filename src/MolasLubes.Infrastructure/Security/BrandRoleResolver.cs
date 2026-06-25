using Microsoft.EntityFrameworkCore;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Infrastructure.Persistence;

namespace MolasLubes.Infrastructure.Security;

public class BrandRoleResolver
{
    private readonly MolasCacheDbContext _db;

    public BrandRoleResolver(MolasCacheDbContext db)
    {
        _db = db;
    }

    public async Task<string?> ResolveAsync(InternalUser user, string brand)
    {
        if (!string.IsNullOrWhiteSpace(user.SapUserCode))
            return user.SapUserCode;

        return await _db.BrandRoleMappings
            .Where(m => m.Brand == brand && m.Role == user.Role)
            .Select(m => m.SapUserCode)
            .FirstOrDefaultAsync();
    }

    public string? Resolve(InternalUser user, string brand)
    {
        if (!string.IsNullOrWhiteSpace(user.SapUserCode))
            return user.SapUserCode;

        return _db.BrandRoleMappings
            .Where(m => m.Brand == brand && m.Role == user.Role)
            .Select(m => m.SapUserCode)
            .FirstOrDefault();
    }
}
