using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MolasLubes.Infrastructure.Persistence;

public class AutoHubDbContextFactory
    : IDesignTimeDbContextFactory<AutoHubDbContext>
{
    public AutoHubDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration[
            "IntegrationProfiles:Profiles:AutoHub:ConnectionStrings:NeonDb"];

        var optionsBuilder = new DbContextOptionsBuilder<AutoHubDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString,
            x => x.MigrationsAssembly("MolasLubes.Infrastructure")
                   .MigrationsHistoryTable("__EFMigrationsHistory_AutoHub")
        );

        return new AutoHubDbContext(optionsBuilder.Options);
    }
}
