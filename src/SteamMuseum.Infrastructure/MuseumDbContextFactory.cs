using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SteamMuseum.Infrastructure;

public sealed class MuseumDbContextFactory : IDesignTimeDbContextFactory<MuseumDbContext>
{
    public MuseumDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Museum")
            ?? "Server=localhost;Database=steam_museum_dev;User=museum;Password=configure-me";
        return new(new DbContextOptionsBuilder<MuseumDbContext>().UseMySQL(connection).Options);
    }
}


