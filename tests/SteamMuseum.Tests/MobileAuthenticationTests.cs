using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Server;
using OpenIddict.Validation;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using SteamMuseum.Infrastructure;
using Xunit;

namespace SteamMuseum.Tests;

public sealed class MobileAuthenticationTests : IAsyncLifetime
{
    private readonly ApiFactory factory = new();
    private const string ClientId = "museum-native-test";
    private const string Redirect = "org.steammuseum.staff:/oauth/callback";
    private const string Password = "Test-password-123!";
    public Task InitializeAsync() => factory.Seed();
    public Task DisposeAsync() { factory.Dispose(); return Task.CompletedTask; }
    private static string Verifier() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Challenge(string verifier) => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string AuthorizeUrl(string verifier, string redirect = Redirect, string? challengeMethod = "S256", string clientId = ClientId) =>
        QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?> {
            ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code",
            ["scope"] = "openid profile offline_access museum_api", ["state"] = "test-state", ["nonce"] = "test-nonce",
            ["code_challenge"] = challengeMethod is null ? null : challengeMethod == "plain" ? verifier : Challenge(verifier),
            ["code_challenge_method"] = challengeMethod
        });
    private async Task<string> Code(HttpClient browser, string verifier)
    {
        var response = await browser.GetAsync(AuthorizeUrl(verifier));
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        Assert.StartsWith(Redirect + "?", response.Headers.Location!.OriginalString);
        var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
        Assert.Equal("test-state", query["state"].ToString());
        return query["code"].ToString();
    }
    private static async Task<HttpResponseMessage> Exchange(HttpClient client, string code, string verifier, string redirect = Redirect, string clientId = ClientId) =>
        await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["redirect_uri"] = redirect,
            ["code"] = code, ["code_verifier"] = verifier
        }));
    private static Task<HttpResponseMessage> Refresh(HttpClient client, string token) =>
        client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = "refresh_token", ["client_id"] = ClientId, ["refresh_token"] = token
        }));
    private static async Task<JsonElement> Success(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<JsonElement>(body);
    }
    private async Task<JsonElement> Tokens(string email = "member@example.test")
    {
        using var browser = factory.Client();
        await ApiFactory.Login(browser, email);
        var verifier = Verifier();
        var code = await Code(browser, verifier);
        using var native = factory.Client();
        return await Success(await Exchange(native, code, verifier));
    }
    private HttpClient Bearer(string token)
    {
        var client = factory.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Discovery_and_pkce_exchange_produce_scoped_tokens_for_existing_members()
    {
        using var client = factory.Client();
        var discovery = await client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        Assert.Equal("https://localhost/connect/token", discovery.GetProperty("token_endpoint").GetString());
        Assert.DoesNotContain(discovery.GetProperty("grant_types_supported").EnumerateArray(), x => x.GetString() == "password");
        Assert.DoesNotContain(discovery.GetProperty("code_challenge_methods_supported").EnumerateArray(), x => x.GetString() == "plain");
        var tokens = await Tokens();
        Assert.Equal("Bearer", tokens.GetProperty("token_type").GetString());
        Assert.InRange(tokens.GetProperty("expires_in").GetInt32(), 1, 600);
        var access = tokens.GetProperty("access_token").GetString()!;
        Assert.Equal(3, access.Split('.').Length);
        using var native = Bearer(access);
        var me = await native.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(factory.MemberId, me.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await native.GetAsync("/api/windows")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await native.GetAsync("/api/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await native.GetAsync($"/api/members/{factory.OtherId}/element-assessments")).StatusCode);
        using var idTokenClient = Bearer(tokens.GetProperty("id_token").GetString()!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await idTokenClient.GetAsync("/api/windows")).StatusCode);
    }

    [Fact]
    public async Task Bearer_writes_work_but_cookies_and_invalid_bearers_cannot_bypass_csrf()
    {
        var tokens = await Tokens("admin@example.test");
        using var native = Bearer(tokens.GetProperty("access_token").GetString()!);
        await Success(await native.PostAsJsonAsync("/api/railways", new { name = "Mobile railway" }));
        using var browser = factory.Client(); await ApiFactory.Login(browser, "admin@example.test");
        browser.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/railways", new { name = "Cookie without CSRF" })).StatusCode);
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/windows")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/railways", new { name = "Invalid bearer" })).StatusCode);
        using var member = Bearer((await Tokens()).GetProperty("access_token").GetString()!);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/railways", new { name = "Member forbidden" })).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("plain")]
    public async Task Missing_or_plain_pkce_is_rejected(string? method)
    {
        using var browser = factory.Client(); await ApiFactory.Login(browser, "member@example.test");
        var response = await browser.GetAsync(AuthorizeUrl(Verifier(), challengeMethod: method));
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || response.Headers.Location?.Query.Contains("error=") == true);
        Assert.False(response.Headers.Location?.Query.Contains("code=") == true);
    }

    [Fact]
    public async Task Unregistered_redirect_and_wrong_verifier_or_client_are_rejected()
    {
        using var browser = factory.Client(); await ApiFactory.Login(browser, "member@example.test");
        var verifier = Verifier();
        var badRedirect = await browser.GetAsync(AuthorizeUrl(verifier, "https://attacker.example/callback"));
        Assert.Equal(HttpStatusCode.BadRequest, badRedirect.StatusCode);
        Assert.Null(badRedirect.Headers.Location);
        using var native = factory.Client();
        var code = await Code(browser, verifier);
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(native, code, Verifier())).StatusCode);
        code = await Code(browser, verifier);
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(native, code, verifier, redirect: "org.steammuseum.staff:/different")).StatusCode);
        code = await Code(browser, verifier);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Exchange(native, code, verifier, clientId: "unregistered-client")).StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_the_entire_family_but_not_other_logins()
    {
        var tokens = await Tokens(); var separate = await Tokens();
        using var client = factory.Client();
        var oldRefresh = tokens.GetProperty("refresh_token").GetString()!;
        var rotated = await Success(await Refresh(client, oldRefresh));
        Assert.NotEqual(oldRefresh, rotated.GetProperty("refresh_token").GetString());
        var replay = await Refresh(client, oldRefresh);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var access = Bearer(rotated.GetProperty("access_token").GetString()!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await access.GetAsync("/api/windows")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(client, rotated.GetProperty("refresh_token").GetString()!)).StatusCode);
        using var unaffected = Bearer(separate.GetProperty("access_token").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await unaffected.GetAsync("/api/windows")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_refresh_has_only_one_winner_and_replay_revokes_its_tokens()
    {
        var tokens = await Tokens();
        using var first = factory.Client(); using var second = factory.Client();
        var results = await Task.WhenAll(Refresh(first, tokens.GetProperty("refresh_token").GetString()!), Refresh(second, tokens.GetProperty("refresh_token").GetString()!));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.BadRequest);
        var winner = await Success(results.Single(r => r.IsSuccessStatusCode));
        using var native = Bearer(winner.GetProperty("access_token").GetString()!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await native.GetAsync("/api/windows")).StatusCode);
    }

    [Theory]
    [InlineData("roles")]
    [InlineData("deactivate")]
    [InlineData("reset")]
    [InlineData("password")]
    [InlineData("logout")]
    public async Task Account_changes_immediately_revoke_access_and_refresh_tokens(string operation)
    {
        var tokens = await Tokens();
        using var native = Bearer(tokens.GetProperty("access_token").GetString()!);
        using var admin = factory.Client(); await ApiFactory.Login(admin, "admin@example.test");
        var response = operation switch {
            "roles" => await admin.PutAsJsonAsync($"/api/accounts/{factory.MemberId}/roles", new { roles = new[] { "Planner" } }),
            "deactivate" => await admin.PutAsJsonAsync($"/api/accounts/{factory.MemberId}/active", new { active = false }),
            "reset" => await admin.PostAsJsonAsync($"/api/accounts/{factory.MemberId}/reset-password", new { temporaryPassword = "Temporary-password-456!" }),
            "password" => await native.PostAsJsonAsync("/api/auth/password", new { currentPassword = Password, newPassword = "Changed-password-456!" }),
            _ => await native.PostAsync("/api/auth/logout", null)
        };
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await native.GetAsync("/api/windows")).StatusCode);
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await Refresh(client, tokens.GetProperty("refresh_token").GetString()!)).StatusCode);
    }

    [Fact]
    public async Task Replayed_authorization_code_revokes_issued_tokens()
    {
        using var browser = factory.Client(); await ApiFactory.Login(browser, "member@example.test");
        using var client = factory.Client(); var verifier = Verifier();
        var code = await Code(browser, verifier);
        var tokens = await Success(await Exchange(client, code, verifier));
        Assert.Equal(HttpStatusCode.BadRequest, (await Exchange(client, code, verifier)).StatusCode);
        using var native = Bearer(tokens.GetProperty("access_token").GetString()!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await native.GetAsync("/api/windows")).StatusCode);
    }

    [Fact]
    public async Task Browser_login_and_forced_password_change_complete_before_any_code_is_issued()
    {
        using var admin = factory.Client(); await ApiFactory.Login(admin, "admin@example.test");
        await Success(await admin.PostAsJsonAsync("/api/accounts", new { email = "temporary@example.test", displayName = "Temporary staff", temporaryPassword = Password, roles = new[] { "Member" } }));
        using var browser = factory.Client();
        var verifier = Verifier(); var returnUrl = AuthorizeUrl(verifier);
        var initial = await browser.GetAsync(returnUrl);
        Assert.StartsWith("/mobile/login?", initial.Headers.Location!.OriginalString);
        var loginPage = await browser.GetStringAsync(initial.Headers.Location);
        var csrf = FormToken(loginPage);
        var rejected = await browser.PostAsync("/mobile/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "temporary@example.test", ["Password"] = Password, ["ReturnUrl"] = returnUrl }));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var login = await browser.PostAsync("/mobile/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "temporary@example.test", ["Password"] = Password, ["ReturnUrl"] = returnUrl, ["__RequestVerificationToken"] = csrf }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var gate = await browser.GetAsync(login.Headers.Location);
        Assert.StartsWith("/mobile/password?", gate.Headers.Location!.OriginalString);
        var passwordPage = await browser.GetStringAsync(gate.Headers.Location);
        var changed = await browser.PostAsync("/mobile/password", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["CurrentPassword"] = Password, ["NewPassword"] = "Changed-password-456!", ["ConfirmPassword"] = "Changed-password-456!", ["ReturnUrl"] = returnUrl, ["__RequestVerificationToken"] = FormToken(passwordPage)
        }));
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        var code = await Code(browser, verifier);
        using var client = factory.Client();
        await Success(await Exchange(client, code, verifier));
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync("/mobile/login?returnUrl=https://attacker.example")).StatusCode);
    }

    [Fact]
    public async Task Expired_and_tampered_access_tokens_are_rejected()
    {
        var tokens = await Tokens(); var access = tokens.GetProperty("access_token").GetString()!;
        using var native = Bearer(access[..(access.LastIndexOf('.') + 1)] + "invalid-signature");
        Assert.Equal(HttpStatusCode.Unauthorized, (await native.GetAsync("/api/windows")).StatusCode);
        // Use the test issuer's actual key so each rejection tests claims, not a bad signature.
        var credentials = factory.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue.SigningCredentials.First();
        foreach (var (claim, value) in new (string, object)[] {
            ("iss", "https://another-issuer.example/"), ("aud", "another-api") })
        {
            var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(WebEncoders.Base64UrlDecode(access.Split('.')[1]))!;
            payload[claim] = value;
            var modified = new JsonWebTokenHandler().CreateToken(JsonSerializer.Serialize(payload), credentials,
                new Dictionary<string, object> { ["typ"] = "at+jwt" });
            native.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", modified);
            var response = await native.GetAsync("/api/windows");
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"Invalid {claim} accepted: {response.StatusCode}");
        }
        // Entry validation restores the original expiry from the database. Advance the
        // validation clock to test genuine expiration rather than rewriting a signed payload.
        factory.Services.GetRequiredService<IOptionsMonitor<OpenIddictValidationOptions>>().CurrentValue.TimeProvider =
            new FutureClock(DateTimeOffset.UtcNow.AddHours(1));
        native.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await native.GetAsync("/api/windows")).StatusCode);
    }

    private static string FormToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "The hosted form must include an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private sealed class FutureClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
