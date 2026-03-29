using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace MolasLubes.Infrastructure.Security;

public sealed class AppUserRepository
{
    private readonly string _connStr;

    public AppUserRepository(IConfiguration configuration)
    {
        _connStr = configuration.GetConnectionString("MolasCacheDb")
                   ?? throw new InvalidOperationException("MolasCacheDb connection string is missing.");
    }

    public async Task<AppUser?> FindAsync(string sapUserCode)
    {
        const string sql = """
            SELECT SapUserCode, PasswordHash, IsActive, DisplayName
            FROM AppUsers
            WHERE SapUserCode = @code
            """;

        await using var conn = new SqlConnection(_connStr);
        return await conn.QuerySingleOrDefaultAsync<AppUser>(sql, new { code = sapUserCode });
    }

    public async Task UpdateLastLoginAsync(string sapUserCode)
    {
        const string sql = "UPDATE AppUsers SET LastLoginAt = GETUTCDATE() WHERE SapUserCode = @code";
        await using var conn = new SqlConnection(_connStr);
        await conn.ExecuteAsync(sql, new { code = sapUserCode });
    }
}
