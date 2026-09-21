using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using SteamMuseum.Domain;
using SteamMuseum.Infrastructure;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SteamMuseum.Api;

[AllowAnonymous]
public sealed class ConnectController(UserManager<MuseumUser> users, MuseumDbContext db,
    IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations,
    IConfiguration configuration) : Controller
{
    [HttpGet("~/connect/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken ct)
    {
        if (!MobileAuthentication.Enabled(configuration)) return NotFound();
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Missing validated OpenID Connect request.");
        // Only system-browser cookie sessions can approve an authorization request.
        var session = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = session.Succeeded ? await users.GetUserAsync(session.Principal!) : null;
        var returnUrl = Request.PathBase + Request.Path + Request.QueryString;
        if (user is null)
        {
            if (request.HasPromptValue(PromptValues.None)) return ProtocolError(Errors.LoginRequired, "Sign in is required.");
            return Redirect("/mobile/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
        }
        // Honor forced reauthentication without creating a redirect loop after the login form.
        if (request.HasPromptValue(PromptValues.Login) || request.MaxAge is { } maxAge &&
            (session.Properties?.IssuedUtc is null || DateTimeOffset.UtcNow - session.Properties.IssuedUtc > TimeSpan.FromSeconds(maxAge)))
        {
            if (request.HasPromptValue(PromptValues.None)) return ProtocolError(Errors.LoginRequired, "Fresh sign in is required.");
            var parameters = Request.Query.Where(p => p.Key is not "prompt" and not "max_age")
                .SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string?>(p.Key, v)));
            return Redirect("/mobile/login?returnUrl=" + Uri.EscapeDataString(Request.PathBase + Request.Path + QueryString.Create(parameters)));
        }
        if (!await IsActive(user, ct)) return ProtocolError(Errors.AccessDenied, "This account cannot sign in.");
        if (user.MustChangePassword)
        {
            if (request.HasPromptValue(PromptValues.None)) return ProtocolError(Errors.InteractionRequired, "Change your temporary password first.");
            return Redirect("/mobile/password?returnUrl=" + Uri.EscapeDataString(returnUrl));
        }
        if (!request.HasScope(MobileAuthentication.ApiScope)) return ProtocolError(Errors.InvalidScope, "The museum_api scope is required.");
        var identity = await CreateIdentity(user);
        identity.SetScopes(request.GetScopes());
        identity.SetResources(MobileAuthentication.Audience);
        identity.SetClaim(Claims.AuthenticationTime, session.Properties!.IssuedUtc!.Value.ToUnixTimeSeconds());
        SetDestinations(identity);
        var app = await applications.FindByClientIdAsync(request.ClientId!, ct) ?? throw new InvalidOperationException("Client disappeared.");
        // Each login has its own token family: replay kills only that grant, not other devices.
        var authorization = await authorizations.CreateAsync(identity, user.Id.ToString(),
            (await applications.GetIdAsync(app, ct))!, AuthorizationTypes.AdHoc, identity.GetScopes(), ct);
        identity.SetAuthorizationId(await authorizations.GetIdAsync(authorization, ct));
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token"), EnableRateLimiting("token")]
    public async Task<IActionResult> Exchange(CancellationToken ct)
    {
        if (!MobileAuthentication.Enabled(configuration)) return NotFound();
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Missing validated token request.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return ProtocolError(Errors.UnsupportedGrantType, "Only authorization code and refresh token grants are supported.");
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal;
        var subject = principal?.GetClaim(Claims.Subject);
        var user = subject is null ? null : await users.FindByIdAsync(subject);
        if (user is null || user.MustChangePassword || !await IsActive(user, ct) ||
            principal!.GetClaim(MobileAuthentication.VersionClaim) != MobileAuthentication.SessionVersion(user))
            return ProtocolError(Errors.InvalidGrant, "This session is no longer valid. Sign in again.");
        // Preserve OpenIddict's private grant metadata and absolute refresh expiry.
        var identity = new ClaimsIdentity(principal!.Claims, "Bearer", Claims.Name, Claims.Role);
        identity.SetClaim(ClaimTypes.NameIdentifier, user.Id.ToString());
        identity.SetClaims(Claims.Role, [.. await users.GetRolesAsync(user)]);
        SetDestinations(identity);
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<bool> IsActive(MuseumUser user, CancellationToken ct) =>
        !await users.IsLockedOutAsync(user) && await db.Set<Member>().AnyAsync(x => x.Id == user.Id && x.Active, ct);

    private async Task<ClaimsIdentity> CreateIdentity(MuseumUser user)
    {
        var member = await db.Set<Member>().SingleAsync(x => x.Id == user.Id);
        var identity = new ClaimsIdentity("Bearer", Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetClaim(ClaimTypes.NameIdentifier, user.Id.ToString());
        identity.SetClaim(Claims.Name, member.DisplayName);
        identity.SetClaim(MobileAuthentication.VersionClaim, MobileAuthentication.SessionVersion(user));
        identity.SetClaims(Claims.Role, [.. await users.GetRolesAsync(user)]);
        SetDestinations(identity);
        return identity;
    }

    private static void SetDestinations(ClaimsIdentity identity) => identity.SetDestinations(claim => claim.Type switch {
        Claims.Name when claim.Subject!.HasScope(Scopes.Profile) => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.AuthenticationTime => [Destinations.IdentityToken],
        Claims.Name => [Destinations.AccessToken],
        Claims.Subject or ClaimTypes.NameIdentifier or Claims.Role or MobileAuthentication.VersionClaim => [Destinations.AccessToken],
        _ => Array.Empty<string>()
    });

    private ForbidResult ProtocolError(string error, string description) => Forbid(
        new AuthenticationProperties(new Dictionary<string, string?> {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
