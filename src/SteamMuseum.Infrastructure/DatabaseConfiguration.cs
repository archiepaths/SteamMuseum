using Microsoft.EntityFrameworkCore;

namespace SteamMuseum.Infrastructure;

public static class DatabaseConfiguration
{
    public const string SqlServerMigrationsAssembly = "SteamMuseum.SqlServerMigrations";
    public const string DefaultConnection = "Server=localhost,1433;Database=steam_museum;User Id=sa;Password=configure-me;Encrypt=True;TrustServerCertificate=True";

    public static string DefaultConnectionFor(string? provider) =>
        string.Equals(provider, "MySql", StringComparison.OrdinalIgnoreCase)
            ? "Server=localhost;Port=3306;Database=steam_museum;User=museum;Password=configure-me"
            : DefaultConnection;

    public static DbContextOptionsBuilder UseMuseumDatabase(this DbContextOptionsBuilder options,
        string? provider, string connection)
    {
        switch (provider?.ToLowerInvariant() ?? "sqlserver")
        {
            case "sqlserver":
                // UseSqlServer supports Azure SQL. Keep statement replay disabled: business writes
                // and token redemption use explicit transactions and must not be blindly replayed.
                return options.UseSqlServer(connection, sql => sql.MigrationsAssembly(SqlServerMigrationsAssembly));
            case "mysql":
                return options.UseMySQL(connection);
            default:
                throw new InvalidOperationException("Database:Provider must be SqlServer or MySql.");
        }
    }
}
