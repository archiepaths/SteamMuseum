using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SteamMuseum.Api;
using SteamMuseum.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace SteamMuseum.Tests;

public sealed class HostingTests
{
    [Fact]
    public async Task Health_probes_allow_http_without_a_session_and_readiness_requires_schema()
    {
        using var factory = new ApiFactory();
        await factory.Seed();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuseumDbContext>();
        await db.Set<MutationLock>().ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
    [Theory]
    [InlineData("10.42.0.5", "https", "203.0.113.10")]
    [InlineData("198.51.100.5", "http", "198.51.100.5")]
    public async Task Forwarded_headers_only_apply_from_configured_ingress(string peer, string scheme, string client)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ReverseProxy:KnownNetworks:0"] = "10.42.0.0/24"
        }).Build();
        var services = new ServiceCollection().AddMuseumProxyHeaders(configuration).BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<ForwardedHeadersOptions>>();
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, options);
        await middleware.Invoke(context);
        Assert.Equal(scheme, context.Request.Scheme);
        Assert.Equal(client, context.Connection.RemoteIpAddress!.ToString());
    }

    [Fact]
    public void Sql_server_uses_its_own_migrations_and_does_not_replay_transactions()
    {
        var options = new DbContextOptionsBuilder<MuseumDbContext>();
        options.UseMuseumDatabase("SqlServer", DatabaseConfiguration.DefaultConnection);
        using var db = new MuseumDbContext(options.Options);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        Assert.False(db.Database.CreateExecutionStrategy().RetriesOnFailure);
        Assert.Single(db.Database.GetMigrations());
        var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("[__EFMigrationsHistory]", script);
        Assert.Contains("uniqueidentifier", script);
        Assert.Contains("time(6)", script);
        Assert.DoesNotContain("utf8mb4", script);
    }

    [Theory]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("MySql", "MySql.EntityFrameworkCore")]
    public void Provider_defaults_use_a_compatible_connection_string(string provider, string expected)
    {
        var options = new DbContextOptionsBuilder<MuseumDbContext>();
        options.UseMuseumDatabase(provider, DatabaseConfiguration.DefaultConnectionFor(provider));
        using var db = new MuseumDbContext(options.Options);
        Assert.Equal(expected, db.Database.ProviderName);
        Assert.NotEmpty(db.Database.GetMigrations());
    }
    [Fact]
    public void Unknown_database_provider_fails_before_connecting()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new DbContextOptionsBuilder<MuseumDbContext>().UseMuseumDatabase("typo", "unused"));
    }
}
