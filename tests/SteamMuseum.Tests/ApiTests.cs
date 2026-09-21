using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SteamMuseum.Application;
using SteamMuseum.Domain;
using SteamMuseum.Infrastructure;
using Xunit;

namespace SteamMuseum.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string? mysql = Environment.GetEnvironmentVariable("MUSEUM_TEST_MYSQL");
    private readonly string mysqlDatabase = $"steammuseum_test_{Guid.NewGuid():N}";
    private readonly string database = Path.Combine(Path.GetTempPath(), $"museum-tests-{Guid.NewGuid():N}.db");
    public readonly Guid AdminId = Guid.NewGuid();
    public readonly Guid MemberId = Guid.NewGuid();
    public readonly Guid OtherId = Guid.NewGuid();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("MobileAuth:Enabled", "true");
        builder.UseSetting("MobileAuth:Issuer", "https://localhost/");
        builder.UseSetting("MobileAuth:ClientId", "museum-native-test");
        builder.UseSetting("MobileAuth:RedirectUri", "org.steammuseum.staff:/oauth/callback");
        builder.ConfigureLogging(o => { o.ClearProviders(); o.AddConsole(); o.SetMinimumLevel(LogLevel.Warning); });
        builder.ConfigureServices(services => {
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.RemoveAll<DbContextOptions<MuseumDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<MuseumDbContext>>();
            services.AddDbContext<MuseumDbContext>(o => {
                if (mysql is null) o.UseSqlite($"Data Source={database}");
                else { var connection = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(mysql) { Database = mysqlDatabase }; o.UseMySQL(connection.ConnectionString); }
            });
        });
    }
    public async Task Seed()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MuseumDbContext>();
        if (mysql is null) await db.Database.EnsureCreatedAsync(); else await db.Database.MigrateAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in AccessRoles.All) Assert.True((await roles.CreateAsync(new(role))).Succeeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<MuseumUser>>();
        foreach (var (id, email, role) in new[] { (AdminId, "admin@example.test", AccessRoles.Administrator), (MemberId, "member@example.test", AccessRoles.Member), (OtherId, "other@example.test", AccessRoles.Member) })
        {
            var user = new MuseumUser { Id = id, UserName = email, Email = email, MustChangePassword = false };
            Assert.True((await users.CreateAsync(user, "Test-password-123!")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            db.Add(new Member { Id = id, DisplayName = email });
        }
        await db.SaveChangesAsync();
        await SteamMuseum.Api.MobileAuthentication.RegisterClient(scope.ServiceProvider,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
    }
    public HttpClient Client() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
    public static async Task Csrf(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var response = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
    }
    public static async Task Login(HttpClient client, string email)
    {
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Test-password-123!" })).StatusCode);
        await Csrf(client);
    }
    private bool cleaned;
    protected override void Dispose(bool disposing)
    {
        if (disposing && !cleaned && mysql is not null) {
            cleaned = true;
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<MuseumDbContext>().Database.EnsureDeleted();
        }
        base.Dispose(disposing);
        if (disposing) { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(database)) File.Delete(database); }
    }
}

public sealed class ApiTests : IAsyncLifetime
{
    private readonly ApiFactory factory = new();
    public Task InitializeAsync() => factory.Seed();
    public Task DisposeAsync() { factory.Dispose(); return Task.CompletedTask; }
    private static async Task<JsonElement> Success(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<JsonElement>(body);
    }
    [Fact]
    public async Task Special_event_ranges_exclude_gaps_and_preserve_shared_responses_and_notes()
    {
        using var admin = factory.Client(); using var member = factory.Client();
        await ApiFactory.Login(admin, "admin@example.test"); await ApiFactory.Login(member, "member@example.test");
        var monthly = (await Success(await admin.PostAsJsonAsync("/api/windows", new { name = "October", kind = "Monthly", start = "2030-10-01", end = "2030-10-31", submissionDeadlineUtc = "2030-09-30T23:59:00Z" }))).GetProperty("id").GetGuid();
        await Success(await member.PutAsJsonAsync($"/api/me/availability/{monthly}", new { maximumAssignments = 5, days = new[] {
            new { date = "2030-10-02", status = "Available" }, new { date = "2030-10-10", status = "Available" }, new { date = "2030-10-20", status = "Available" }
        }}));
        var created = await Success(await admin.PostAsJsonAsync("/api/windows", new { name = "Two weekends", kind = "SpecialEvent", submissionDeadlineUtc = "2030-09-30T23:59:00Z", notes = "Meet at the station.\nBring lunch.", dateRanges = new[] {
            new { start = "2030-10-20", end = "2030-10-21" }, new { start = "2030-10-01", end = "2030-10-02" }
        }}));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("2030-10-01", created.GetProperty("start").GetString());
        Assert.Equal("2030-10-21", created.GetProperty("end").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MuseumDbContext>();
            var railway = new Railway { Name = "Test railway" }; db.Add(railway);
            foreach (var date in new[] { new DateOnly(2030, 10, 2), new DateOnly(2030, 10, 10) })
            {
                var duty = new Duty { Name = "Test shift", Date = date, Start = new(9, 0), End = new(12, 0), Role = DutyRole.Guard, RailwayId = railway.Id };
                db.Add(duty); db.Add(new Assignment { DutyId = duty.Id, MemberId = factory.MemberId, Status = AssignmentStatus.Published });
            }
            await db.SaveChangesAsync();
        }
        var view = await member.GetFromJsonAsync<JsonElement>($"/api/me/availability/{id}");
        Assert.Equal(1, view.GetProperty("assigned").GetInt32());
        Assert.Equal(2, view.GetProperty("window").GetProperty("dateRanges").GetArrayLength());
        Assert.Equal("Meet at the station.\nBring lunch.", view.GetProperty("window").GetProperty("notes").GetString());
        Assert.Equal(new[] { "2030-10-02", "2030-10-20" }, view.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("date").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PutAsJsonAsync($"/api/me/availability/{id}", new { maximumAssignments = 1, days = new[] { new { date = "2030-10-10", status = "Unavailable" } } })).StatusCode);
        var unchanged = await member.GetFromJsonAsync<JsonElement>($"/api/me/availability/{monthly}");
        Assert.All(unchanged.GetProperty("days").EnumerateArray(), d => Assert.Equal("Available", d.GetProperty("status").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync($"/api/windows/{id}/notes", new { notes = "Not allowed" })).StatusCode);
        await Success(await admin.PutAsJsonAsync($"/api/windows/{id}/notes", new { notes = "Updated instructions" }));
        await Success(await admin.PutAsJsonAsync($"/api/windows/{id}/state", new { open = false, deadlineUtc = "2030-09-30T23:59:00Z" }));
        var windows = await member.GetFromJsonAsync<JsonElement>("/api/windows");
        Assert.Equal("Updated instructions", windows.EnumerateArray().Single(w => w.GetProperty("id").GetGuid() == id).GetProperty("notes").GetString());
        await Success(await admin.PutAsJsonAsync($"/api/windows/{id}/notes", new { notes = (string?)null }));
        view = await member.GetFromJsonAsync<JsonElement>($"/api/me/availability/{id}");
        Assert.Equal(JsonValueKind.Null, view.GetProperty("window").GetProperty("notes").ValueKind);
    }
    [Fact]
    public async Task Invalid_window_ranges_and_long_notes_are_rejected()
    {
        using var admin = factory.Client(); await ApiFactory.Login(admin, "admin@example.test");
        foreach (var ranges in new object[][] {
            [],
            [new { start = "2030-10-02", end = "2030-10-01" }],
            [new { start = "2030-10-01", end = "2030-10-03" }, new { start = "2030-10-03", end = "2030-10-05" }],
            [new { start = "2030-10-01", end = "2032-10-01" }]
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/windows", new { name = "Invalid", kind = "SpecialEvent", dateRanges = ranges, submissionDeadlineUtc = "2030-09-30T23:59:00Z" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/windows", new { name = "Invalid monthly", kind = "Monthly", start = "2030-10-01", end = "2030-10-31", dateRanges = new[] { new { start = "2030-10-01", end = "2030-10-02" } }, submissionDeadlineUtc = "2030-09-30T23:59:00Z" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/windows", new { name = "Long notes", kind = "SpecialEvent", start = "2030-10-01", end = "2030-10-02", notes = new string('x', 2001), submissionDeadlineUtc = "2030-09-30T23:59:00Z" })).StatusCode);
    }
    [Fact]
    public async Task Authentication_authorization_and_csrf_are_enforced()
    {
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/windows")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { email = "member@example.test", password = "Test-password-123!" })).StatusCode);
        await ApiFactory.Login(client, "member@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/members/{factory.OtherId}/availability/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/competences", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/accounts", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/competences")).StatusCode);
    }
    [Fact]
    public async Task Changing_roles_revokes_old_sessions_and_restricts_new_ones()
    {
        using var admin = factory.Client(); using var member = factory.Client();
        await ApiFactory.Login(admin, "admin@example.test"); await ApiFactory.Login(member, "member@example.test");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/accounts/{factory.MemberId}/roles", new { roles = new[] { "Planner" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/members")).StatusCode);
        await ApiFactory.Login(member, "member@example.test");
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/competences", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/accounts/{factory.MemberId}/roles", new { roles = new[] { "Member" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/members")).StatusCode);
        await ApiFactory.Login(member, "member@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/members")).StatusCode);
    }
    [Fact]
    public async Task Repeated_bad_passwords_lock_out_even_the_correct_password()
    {
        using var member = factory.Client(); await ApiFactory.Csrf(member);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await member.PostAsJsonAsync("/api/auth/login", new { email = "member@example.test", password = "wrong-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.PostAsJsonAsync("/api/auth/login", new { email = "member@example.test", password = "Test-password-123!" })).StatusCode);
    }
    [Fact]
    public async Task Invalid_and_closed_window_submissions_do_not_change_availability()
    {
        using var admin = factory.Client(); using var member = factory.Client();
        await ApiFactory.Login(admin, "admin@example.test"); await ApiFactory.Login(member, "member@example.test");
        var window = (await Success(await admin.PostAsJsonAsync("/api/windows", new { name = "October", kind = "Monthly", start = "2030-10-01", end = "2030-10-31", submissionDeadlineUtc = "2030-09-30T23:59:00Z" }))).GetProperty("id").GetGuid();
        var invalid = new { maximumAssignments = 4, days = new object[] {
            new { date = "2030-10-01", status = "Available" },
            new { date = "2030-10-02", status = "Available", from = "16:00:00", until = "10:00:00" }
        }};
        Assert.Equal(HttpStatusCode.BadRequest, (await member.PutAsJsonAsync($"/api/me/availability/{window}", invalid)).StatusCode);
        var unchanged = await member.GetFromJsonAsync<JsonElement>($"/api/me/availability/{window}");
        Assert.Empty(unchanged.GetProperty("days").EnumerateArray());
        await Success(await admin.PutAsJsonAsync($"/api/windows/{window}/state", new { open = false, deadlineUtc = "2030-09-30T23:59:00Z" }));
        Assert.Equal(HttpStatusCode.Conflict, (await member.PutAsJsonAsync($"/api/me/availability/{window}", new { maximumAssignments = 4, days = new[] { new { date = "2030-10-01", status = "Available" } } })).StatusCode);
    }
    [Fact]
    public async Task Logout_revokes_other_sessions()
    {
        using var first = factory.Client(); using var second = factory.Client();
        await ApiFactory.Login(first, "member@example.test"); await ApiFactory.Login(second, "member@example.test");
        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/windows")).StatusCode);
    }
    [Fact]
    public async Task Temporary_password_must_be_changed_and_deactivation_revokes_access()
    {
        using var admin = factory.Client(); await ApiFactory.Login(admin, "admin@example.test");
        var created = await Success(await admin.PostAsJsonAsync("/api/accounts", new { email = "new@example.test", displayName = "New member", temporaryPassword = "Test-password-123!", roles = new[] { "Member" } }));
        var id = created.GetProperty("id").GetGuid();
        using var member = factory.Client(); await ApiFactory.Login(member, "new@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/windows")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Test-password-123!", newPassword = "Changed-password-456!" })).StatusCode);
        await ApiFactory.Csrf(member);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/windows")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/accounts/{id}/active", new { active = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/windows")).StatusCode);
    }
    [Fact]
    public async Task Availability_roster_and_competence_workflow_enforces_rules()
    {
        using var admin = factory.Client(); using var member = factory.Client();
        await ApiFactory.Login(admin, "admin@example.test"); await ApiFactory.Login(member, "member@example.test");
        var railway = (await Success(await admin.PostAsJsonAsync("/api/railways", new { name = "Museum railway" }))).GetProperty("id").GetGuid();
        var loco = (await Success(await admin.PostAsJsonAsync("/api/locomotives", new { name = "Locomotive A" }))).GetProperty("id").GetGuid();
        var window = (await Success(await admin.PostAsJsonAsync("/api/windows", new { name = "October", kind = "Monthly", start = "2030-10-01", end = "2030-10-31", submissionDeadlineUtc = "2030-09-30T23:59:00Z" }))).GetProperty("id").GetGuid();
        var eventWindow = (await Success(await admin.PostAsJsonAsync("/api/windows", new { name = "Gala", kind = "SpecialEvent", start = "2030-10-08", end = "2030-10-08", submissionDeadlineUtc = "2030-10-07T23:59:00Z" }))).GetProperty("id").GetGuid();
        var competence = (await Success(await admin.PostAsJsonAsync("/api/competences", new { memberId = factory.MemberId, role = "Driver", railwayId = railway, locomotiveId = loco, validFrom = "2030-01-01", validUntil = "2030-12-31", evidence = "Practical assessment passed" }))).GetProperty("id").GetGuid();
        var days = new[] {
            new { date = "2030-10-01", status = "Available", from = "10:00:00", until = "16:00:00", preferredRole = "Guard" },
            new { date = "2030-10-08", status = "Available", from = "10:00:00", until = "16:00:00", preferredRole = "Guard" }
        };
        await Success(await member.PutAsJsonAsync($"/api/me/availability/{window}", new { maximumAssignments = 1, days }));
        var shared = await member.GetFromJsonAsync<JsonElement>($"/api/me/availability/{eventWindow}");
        Assert.Single(shared.GetProperty("days").EnumerateArray());
        async Task<Guid> Duty(string date, string start = "10:00:00", string end = "15:00:00") =>
            (await Success(await admin.PostAsJsonAsync("/api/duties", new { name = "Driver turn", date, start, end, role = "Driver", railwayId = railway, locomotiveId = loco }))).GetProperty("id").GetGuid();
        var tooEarly = await Duty("2030-10-01", "09:00:00");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/duties/{tooEarly}/assignment", new { memberId = factory.MemberId })).StatusCode);
        var first = await Duty("2030-10-01");
        var assignment = (await Success(await admin.PostAsJsonAsync($"/api/duties/{first}/assignment", new { memberId = factory.MemberId }))).GetProperty("id").GetGuid();
        var mine = await member.GetFromJsonAsync<JsonElement>("/api/me/roster?from=2030-10-01&until=2030-10-31");
        Assert.Empty(mine.EnumerateArray());
        var second = await Duty("2030-10-08");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/duties/{second}/assignment", new { memberId = factory.MemberId })).StatusCode);
        await Success(await admin.PostAsync($"/api/assignments/{assignment}/publish", null));
        mine = await member.GetFromJsonAsync<JsonElement>("/api/me/roster?from=2030-10-01&until=2030-10-31");
        Assert.Single(mine.EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await member.PutAsJsonAsync($"/api/me/availability/{window}", new { maximumAssignments = 0, days = Array.Empty<object>() })).StatusCode);
        await Success(await member.PutAsJsonAsync($"/api/me/availability/{window}", new { maximumAssignments = 1, days = new[] { new { date = "2030-10-01", status = "Unavailable" } } }));
        var roster = await admin.GetFromJsonAsync<JsonElement>("/api/roster?from=2030-10-01&until=2030-10-31");
        Assert.Contains(roster.EnumerateArray(), x => x.GetProperty("issues").GetArrayLength() > 0);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/assignments/{assignment}/publish", null)).StatusCode);
        await Success(await admin.PostAsync($"/api/assignments/{assignment}/cancel", null));
        var nextAssignment = (await Success(await admin.PostAsJsonAsync($"/api/duties/{second}/assignment", new { memberId = factory.MemberId }))).GetProperty("id").GetGuid();
        await Success(await admin.PostAsJsonAsync($"/api/competences/{competence}/revoke", new { reason = "Reassessment required" }));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/assignments/{nextAssignment}/publish", null)).StatusCode);
        var history = await member.GetFromJsonAsync<JsonElement>("/api/me/competences");
        Assert.NotEqual(JsonValueKind.Null, history[0].GetProperty("revokedAtUtc").ValueKind);
    }
}








