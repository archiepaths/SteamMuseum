using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SteamMuseum.Application;
using SteamMuseum.Domain;
using SteamMuseum.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Museum")
    ?? "Server=localhost;Port=3306;Database=steam_museum;User=museum;Password=configure-me";
builder.Services.AddDbContext<MuseumDbContext>(o => o.UseMySQL(connection));
builder.Services.AddScoped<IStore, EfStore>();
builder.Services.AddScoped<MuseumService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddIdentity<MuseumUser, IdentityRole<Guid>>(options => {
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<MuseumDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(o => {
    o.Cookie.Name = "__Host-SteamMuseum";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
var protection = builder.Services.AddDataProtection().SetApplicationName("SteamMuseum");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keyPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddAntiforgery(o => {
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "__Host-SteamMuseum-CSRF";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("Planning", p => p.RequireRole(AccessRoles.Planner, AccessRoles.Administrator))
    .AddPolicy("Assessment", p => p.RequireRole(AccessRoles.Assessor, AccessRoles.Administrator))
    .AddPolicy("StaffRecords", p => p.RequireRole(AccessRoles.Planner, AccessRoles.Assessor, AccessRoles.Administrator))
    .AddPolicy("Administration", p => p.RequireRole(AccessRoles.Administrator));
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["https://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
var app = builder.Build();

// Explicit operator commands: normal application startup never changes schema or creates an admin.
if (args.Contains("--migrate") || args.Contains("--bootstrap-admin"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MuseumDbContext>();
    if (args.Contains("--migrate")) await db.Database.MigrateAsync();
    if (args.Contains("--bootstrap-admin"))
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in AccessRoles.All)
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
            }
        var users = scope.ServiceProvider.GetRequiredService<UserManager<MuseumUser>>();
        if ((await users.GetUsersInRoleAsync(AccessRoles.Administrator)).Count != 0)
            throw new InvalidOperationException("An administrator already exists. Bootstrap is disabled.");
        var email = builder.Configuration["Bootstrap:Email"] ?? throw new InvalidOperationException("Set Bootstrap__Email.");
        var password = builder.Configuration["Bootstrap:Password"] ?? throw new InvalidOperationException("Set Bootstrap__Password.");
        await scope.ServiceProvider.GetRequiredService<AccountService>().Create(Guid.Empty,
            new(email, "Museum administrator", password, [AccessRoles.Administrator]), CancellationToken.None);
    }
    return;
}
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) => {
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var users = context.RequestServices.GetRequiredService<UserManager<MuseumUser>>();
        var user = await users.GetUserAsync(context.User);
        var db = context.RequestServices.GetRequiredService<MuseumDbContext>();
        if (user is null || !await db.Set<Member>().AnyAsync(x => x.Id == user.Id && x.Active, context.RequestAborted))
        { context.Response.StatusCode = 401; return; }
        if (user.MustChangePassword && !context.Request.Path.StartsWithSegments("/api/auth"))
        { await Results.Problem(statusCode: 403, title: "Change your temporary password before using the application.").ExecuteAsync(context); return; }
    }
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        { await Results.Problem(statusCode: 400, title: "Missing or invalid CSRF token.").ExecuteAsync(context); return; }
    }
    await next(context);
});
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery anti) => {
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { token = anti.GetAndStoreTokens(context).RequestToken });
}).AllowAnonymous();
if (app.Environment.IsDevelopment()) app.MapOpenApi().RequireAuthorization("Administration");
app.MapControllers();
app.Run();

public partial class Program;



