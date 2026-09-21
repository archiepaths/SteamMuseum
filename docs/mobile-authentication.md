# Mobile authentication

The React website continues to use secure Identity cookies and CSRF tokens. Native apps use the same API and staff records through a self-hosted OpenIddict 7.7.1 authorization server. No paid identity service, new user database, public registration or password grant is needed.

## Security model

- OpenID Connect authorization code flow with **S256 PKCE**, a public client (no client secret), an exact registered redirect URI, and the system browser for sign-in.
- API access tokens are signed JWTs lasting **10 minutes**, scoped to `museum_api` and audience `steam-museum-api`. ID tokens are rejected as API credentials.
- Refresh tokens rotate on every use. Their lifetime is **14 days from the original issuance**, without sliding renewal. A redeemed token has no reuse grace period: replay invalidates every token in that login's family, including its access tokens. Other device logins remain independent.
- The complete token exchange is serialized using the database-backed mutation lock already used for museum writes. Validation, redemption, token persistence and replay revocation share one transaction across API instances. Responses are released only after commit. OpenIddict uses tracked updates because Connector/NET cannot translate its joined bulk-revocation query correctly. This trades throughput for correctness at museum scale.
- Token and authorization status are checked on every bearer request, with OpenIddict entity caching disabled. A hash of the existing Identity security stamp is also compared with current account state. Deactivation, role changes, password changes/resets and logout invalidate existing access and refresh tokens immediately; reactivation does not restore old sessions. The raw security stamp is never sent in access/ID tokens.
- Login lockout, access roles, member isolation and required temporary-password changes still apply. Temporary passwords must be changed in the browser **before an authorization code is issued**. An issued code is also rejected if account state changes before exchange.
- Invalid bearer credentials never fall back to a browser cookie. Cookie-authenticated writes still require CSRF tokens. Only successfully bearer-authenticated `/api` writes bypass that check. The OAuth `/connect/token` endpoint is protected separately by OpenIddict's protocol validation. Hosted login/password forms always require antiforgery protection.
- `/api/auth/logout` preserves the existing **sign out everywhere** behavior. After a native password change, discard tokens and sign in again. Native password changes do not refresh a browser cookie.

## Enable locally

Mobile authentication is enabled in `appsettings.Development.json`, with issuer `https://localhost:7240/`. It is disabled by default in other environments. Tests explicitly enable it with ephemeral signing/encryption keys and isolated databases.

From the repository root, using the normal configured museum database connection:

```powershell
# Apply the new tables before using mobile sign-in on an existing database.
dotnet run --project src/SteamMuseum.Api -- --migrate

# Choose your actual app identifier and exact callback.
$env:MobileAuth__ClientId = 'museum-staff-native'
$env:MobileAuth__RedirectUri = 'org.steammuseum.staff:/oauth/callback'
dotnet run --project src/SteamMuseum.Api -- --register-mobile-client
Remove-Item Env:\MobileAuth__ClientId
Remove-Item Env:\MobileAuth__RedirectUri

dotnet run --project src/SteamMuseum.Api
```

The registration command creates or updates just that client, with a fixed allowlist of scopes and grant types. Registration is not a public HTTP endpoint and never runs on normal startup. Register only first-party museum apps: these registrations use implicit consent after staff authentication. Use different client IDs when separate apps need different callbacks. Prefer verified HTTPS universal/app links for production; private schemes must use a reverse-domain name. Wildcard callbacks are not supported.

No migration or client registration was applied to real museum data during implementation. Apply the migration and register your app before attempting a real mobile login.

## Endpoints and client flow

| Endpoint | Purpose |
|---|---|
| `GET /.well-known/openid-configuration` | OIDC discovery, including the signing-key endpoint |
| `GET /connect/authorize` | Browser sign-in and authorization code |
| `GET/POST /mobile/login` | Staff login, antiforgery protected |
| `GET/POST /mobile/password` | Required temporary-password change, antiforgery protected |
| `POST /connect/token` | Form-encoded code exchange or refresh |
| `/api/*` | Existing endpoints, accepting cookie or scoped bearer credentials |

In Expo/React Native, use a maintained OAuth/OIDC client such as Expo AuthSession with a development build and registered callback. Request `openid profile offline_access museum_api`; generate a fresh cryptographically random verifier, `state` and `nonce` for each login, use `S256`, and verify the returned state. If relying on an ID token for identity, use an OIDC-capable client to validate its signature, issuer, audience and nonce; alternatively obtain the staff profile from `/api/auth/me` using the access token. Never collect museum passwords in an embedded WebView or put a client secret in an app bundle.

Send the browser to the discovered authorization endpoint with:

```text
response_type=code
client_id=museum-staff-native
redirect_uri=org.steammuseum.staff:/oauth/callback
scope=openid profile offline_access museum_api
state=<fresh random state>
nonce=<fresh random nonce>
code_challenge=<base64url SHA-256 of the verifier>
code_challenge_method=S256
```

After the callback, send a form-encoded POST to the token endpoint:

```text
grant_type=authorization_code
client_id=museum-staff-native
redirect_uri=org.steammuseum.staff:/oauth/callback
code=<returned code>
code_verifier=<original verifier>
```

Use the access token only in the request header over HTTPS:

```http
GET /api/auth/me
Authorization: Bearer <access_token>
```

To refresh, send `grant_type=refresh_token`, `client_id`, and `refresh_token` as form data to `/connect/token`. Atomically save the replacement refresh token in secure device storage (for example Expo SecureStore); keep the access token in memory. Use a **single shared refresh operation** for concurrent API requests. Do not retry the same refresh token after an ambiguous network failure: the server may already have redeemed it, so reauthenticate if the replacement was lost. On `invalid_grant`, discard the session and sign in again. Never blindly retry a business write during refresh recovery.

Native clients do not need `/api/auth/csrf`; the web client still does. A physical phone's `localhost` refers to the phone. Use a reachable HTTPS development/staging issuer with a certificate trusted by the device; do not disable TLS validation. Set `MobileAuth:Issuer` to the actual API origin and permit its host in `AllowedHosts`.

## Production configuration

Enable explicitly and provide a stable issuer and **two persistent certificates with private keys**, one for signing and one for encryption, through secret configuration:

```text
MobileAuth__Enabled=true
MobileAuth__Issuer=https://staff-api.your-museum.example/
MobileAuth__SigningCertificatePath=/run/secrets/mobile-signing.pfx
MobileAuth__SigningCertificatePassword=<secret>
MobileAuth__EncryptionCertificatePath=/run/secrets/mobile-encryption.pfx
MobileAuth__EncryptionCertificatePassword=<secret>
```

Missing, expired or keyless production certificates fail startup when mobile authentication is enabled. Development certificates are only used in Development; ephemeral keys are only used in Testing. These certificates are separate from the server's HTTPS certificate. Share the same keys and issuer across API instances/restarts and protect their private-key files. Plan key rotation with overlap for existing refresh sessions. The current configuration accepts one certificate per purpose, so a zero-interruption rotation requires extending it to load retiring keys too.

Continue to configure trusted forwarded headers for the actual reverse proxy, HTTPS, restricted web CORS and the persistent Identity data-protection key ring. CORS is a browser policy, not protection against native callers. Token exchange is limited per address (60/minute); hosted and API logins use the existing 10/minute limit and account lockout. Do not log passwords, tokens, authorization codes or query strings containing them at the proxy/monitoring layer.

The feature does not add MFA, per-device session management or an automatic token-pruning job. Include token tables in backup and retention planning; do not prune redeemed refresh tokens while their 14-day grant can still be active, because these records support replay detection.

## Verification

Run `dotnet test SteamMuseum.sln` for the isolated SQLite suite. Set `MUSEUM_TEST_MYSQL` to a disposable MySQL server connection to run the same tests with real migrations; randomly named test databases are cleaned up. Tests cover cookie login, hosted forms, discovery, PKCE, bearer API access, role restrictions, CSRF separation, refresh rotation/replay, concurrent refresh, account revocation, code replay and signature/issuer/audience/expiry validation.

References: [OpenIddict server integration](https://documentation.openiddict.com/guides/getting-started/creating-your-own-server-instance), [token storage](https://documentation.openiddict.com/configuration/token-storage), [OAuth for native apps](https://www.rfc-editor.org/rfc/rfc8252), [OAuth security best current practice](https://www.rfc-editor.org/rfc/rfc9700), [Expo authentication](https://docs.expo.dev/guides/authentication/).
