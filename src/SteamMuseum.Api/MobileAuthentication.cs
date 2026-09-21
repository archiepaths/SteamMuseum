using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;
using SteamMuseum.Infrastructure;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SteamMuseum.Api;

public static class MobileAuthentication
{
    public const string Scheme = "MuseumCookieOrBearer";
    public const string ApiScope = "museum_api";
    public const string Audience = "steam-museum-api";
    public const string VersionClaim = "museum_session_version";
    public const string ValidBearerItem = "Museum.ValidBearer";

    public static bool Enabled(IConfiguration configuration) => configuration.GetValue<bool>("MobileAuth:Enabled");
    public static string SessionVersion(MuseumUser user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.SecurityStamp ?? throw new InvalidOperationException("Missing security stamp."))));

    public static void AddMuseumMobileAuthentication(this WebApplicationBuilder builder)
    {
        var oidc = builder.Services.AddOpenIddict().AddCore(options => {
            // Connector/NET does not support the joined UPDATE SQL emitted by EF's bulk
            // revocation query. Use supported tracked updates inside the exchange transaction.
            options.UseEntityFrameworkCore().UseDbContext<MuseumDbContext>().DisableBulkOperations();
            // Revocation must be visible across instances immediately, not after a cache lifetime.
            options.DisableEntityCaching();
        });
        if (!Enabled(builder.Configuration)) return;

        builder.Services.AddAuthentication(options => {
            options.DefaultAuthenticateScheme = Scheme;
            options.DefaultChallengeScheme = Scheme;
            options.DefaultForbidScheme = Scheme;
        }).AddPolicyScheme(Scheme, null, options => {
            // Never fall back to a cookie when a caller presents an invalid bearer token.
            options.ForwardDefaultSelector = context => context.Request.Headers.Authorization.ToString()
                .StartsWith("Bearer", StringComparison.OrdinalIgnoreCase)
                    ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme : IdentityConstants.ApplicationScheme;
        });

        oidc.AddServer(options => {
            var issuer = builder.Configuration["MobileAuth:Issuer"];
            if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Set MobileAuth:Issuer to the public HTTPS API origin.");
            options.SetIssuer(uri);
            options.SetAuthorizationEndpointUris("/connect/authorize").SetTokenEndpointUris("/connect/token");
            options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
            options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.OfflineAccess, ApiScope);
            options.SetAccessTokenLifetime(TimeSpan.FromMinutes(10));
            options.SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(2));
            options.SetRefreshTokenLifetime(TimeSpan.FromDays(14));
            options.DisableSlidingRefreshTokenExpiration();
            options.SetRefreshTokenReuseLeeway(null);
            options.Configure(o => o.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));
            // Signed JWT access tokens; authorization codes and refresh tokens remain encrypted.
            options.DisableAccessTokenEncryption();
            if (builder.Environment.IsDevelopment())
                options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
            else if (builder.Environment.IsEnvironment("Testing"))
                options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
            else
            {
                options.AddSigningCertificate(LoadCertificate(builder.Configuration, "Signing"));
                options.AddEncryptionCertificate(LoadCertificate(builder.Configuration, "Encryption"));
            }
            options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
        });
        oidc.AddValidation(options => {
            options.UseLocalServer();
            options.AddAudiences(Audience);
            options.EnableTokenEntryValidation();
            options.EnableAuthorizationEntryValidation();
            options.UseAspNetCore();
        });
    }

    private static X509Certificate2 LoadCertificate(IConfiguration configuration, string purpose)
    {
        var path = configuration[$"MobileAuth:{purpose}CertificatePath"];
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException($"Set MobileAuth:{purpose}CertificatePath and its password before enabling mobile authentication in production.");
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(path,
            configuration[$"MobileAuth:{purpose}CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
            throw new InvalidOperationException($"The {purpose} certificate must have a private key and be currently valid.");
        return certificate;
    }

    public static async Task RegisterClient(IServiceProvider services, IConfiguration configuration, CancellationToken ct = default)
    {
        if (!Enabled(configuration)) throw new InvalidOperationException("Enable MobileAuth before registering a native client.");
        var clientId = configuration["MobileAuth:ClientId"];
        var redirect = configuration["MobileAuth:RedirectUri"];
        if (string.IsNullOrWhiteSpace(clientId) || !Uri.TryCreate(redirect, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.Scheme != "https" && (!uri.Scheme.Contains('.') || uri.Scheme is "http" or "javascript" or "file")))
            throw new InvalidOperationException("Supply MobileAuth:ClientId and an exact HTTPS app link or reverse-domain private-scheme MobileAuth:RedirectUri (for example org.example.museum:/oauth/callback).");
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor {
            ClientId = clientId, DisplayName = "Museum staff mobile app", ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            RedirectUris = { uri },
            Permissions = {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code, Permissions.Scopes.Profile, Permissions.Prefixes.Scope + ApiScope
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };
        var existing = await manager.FindByClientIdAsync(clientId, ct);
        if (existing is null) await manager.CreateAsync(descriptor, ct);
        else await manager.UpdateAsync(existing, descriptor, ct);
    }
}

// Wrap the COMPLETE OpenIddict exchange (validation, redemption, issuance) in the same
// database lock used for staff changes. Two instances cannot rotate one token concurrently.
// Buffer the small protocol response until commit, including replay-triggered revocations.
public sealed class TokenExchangeTransactionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, MuseumDbContext db, IConfiguration configuration)
    {
        if (!MobileAuthentication.Enabled(configuration) || context.Request.Path != "/connect/token")
        { await next(context); return; }
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, context.RequestAborted);
        var count = await db.Set<MutationLock>().Where(x => x.Id == 1)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revision, x => x.Revision + 1), context.RequestAborted);
        if (count != 1) throw new InvalidOperationException("Apply the database migrations before using mobile authentication.");
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
            await transaction.CommitAsync(context.RequestAborted);
            buffer.Position = 0;
            await buffer.CopyToAsync(original, context.RequestAborted);
        }
        finally { context.Response.Body = original; }
    }
}
