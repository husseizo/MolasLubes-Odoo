using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MolasLubes.Infrastructure.Persistence;

public class Live2021CacheDbContextFactory
    : IDesignTimeDbContextFactory<Live2021CacheDbContext>
{
    public Live2021CacheDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration[
            "IntegrationProfiles:Profiles:AutoHub:ConnectionStrings:CacheDb"];

        var optionsBuilder = new DbContextOptionsBuilder<Live2021CacheDbContext>();

        optionsBuilder.UseSqlServer(
            connectionString,
            x => x.MigrationsAssembly("MolasLubes.Infrastructure")
                   .MigrationsHistoryTable("__EFMigrationsHistory_Live2021Cache")
        );

        return new Live2021CacheDbContext(optionsBuilder.Options);
    }
}
