using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SteamMuseum.Infrastructure;

public sealed class MuseumDbContextFactory : IDesignTimeDbContextFactory<MuseumDbContext>
{
    public MuseumDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("Database__Provider");
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Museum")
            ?? DatabaseConfiguration.DefaultConnectionFor(provider);
        var options = new DbContextOptionsBuilder<MuseumDbContext>();
        options.UseMuseumDatabase(provider, connection);
        return new(options.Options);
    }
}
