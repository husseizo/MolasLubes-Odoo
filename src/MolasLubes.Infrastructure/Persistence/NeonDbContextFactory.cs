using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MolasLubes.Infrastructure.Persistence;

public class NeonDbContextFactory
    : IDesignTimeDbContextFactory<NeonDbContext>
{
    public NeonDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<NeonDbContext>();

        optionsBuilder.UseNpgsql(
            configuration.GetConnectionString("NeonDb"),
            x => x.MigrationsAssembly("MolasLubes.Infrastructure")
        );

        return new NeonDbContext(optionsBuilder.Options);
    }
}