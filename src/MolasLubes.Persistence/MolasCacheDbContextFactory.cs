using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MolasLubes.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations add</c> when the Persistence
/// project is used as the startup project.  The connection string is a placeholder —
/// migrations generation does not require a live database.
/// </summary>
public class MolasCacheDbContextFactory : IDesignTimeDbContextFactory<MolasCacheDbContext>
{
    public MolasCacheDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MolasCacheDbContext>()
            .UseSqlServer("Server=.;Database=MolasCacheDb_Design;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new MolasCacheDbContext(options);
    }
}
