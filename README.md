# Steam Museum API

The API for staff availability, competence records and rostering, built with .NET 10, EF Core 10 and Azure SQL / SQL Server. Legacy MySQL databases remain supported through an explicit provider setting. The React web app is a separate project in the sibling SteamMuseumWeb folder.

## Included

- ASP.NET Core Identity: administrator-created accounts, secure cookie sessions, login lockout, password changes, administrator password resets and account deactivation.
- Optional native-app authentication: self-hosted OpenID Connect authorization code + S256 PKCE, short-lived JWT access tokens, rotating refresh tokens and immediate account/session revocation. See the [mobile authentication guide](docs/mobile-authentication.md).
- Member, Planner, Assessor and Administrator access roles. Roles can be combined; changing roles revokes existing sessions.
- Monthly and special-event availability windows, deadlines and open/close controls.
- Whole-day or time-limited availability, optional preferred role, notes and maximum assignments per window.
- Element assessments and reassessments with recorded outcomes and due dates. Operating roles and variants require current competence in every required element.
- Duties, draft assignments, publication and cancellation. Qualification, availability, overlap and maximum-assignment checks are enforced on the server.
- Audit records for business changes and account administration, OpenAPI in development, database migrations, example requests and automated tests.

The [React staff frontend](../SteamMuseumWeb/README.md) includes member, planner, assessor and administrator workflows. Fleet maintenance, the full rolling stock register, group bookings, Xero integration and competence import are future modules. The locomotive list here is reference data for qualifications and duties, not a maintenance system.

## Architecture

```text
src/
  SteamMuseum.Domain/          Entities and pure roster rules; no framework dependencies
  SteamMuseum.Application/     Use cases, request/response contracts and persistence ports
  SteamMuseum.Infrastructure/  EF Core providers, legacy MySQL migrations and ASP.NET Core Identity
  SteamMuseum.SqlServerMigrations/ SQL Server / Azure SQL schema migrations
  SteamMuseum.Api/             HTTP endpoints, authorization policies and composition root
tests/
  SteamMuseum.Tests/           Domain rules, authenticated API workflows and concurrency tests
```

Dependencies point inward: Application -> Domain; Infrastructure -> Application; API composes Infrastructure and Application. The business layer does not reference EF Core, ASP.NET Core or MySQL. Authentication implementation is kept in Infrastructure; business use cases are in Application.

Default provider: `Microsoft.EntityFrameworkCore.SqlServer` 10.0.11, supporting SQL Server and Azure SQL. Set `Database__Provider=MySql` for the retained `MySql.EntityFrameworkCore` 10.0.9 provider. Provider-specific migrations are selected automatically. Explicit converters preserve calendar dates, local times and UTC timestamps. See [Azure deployment](docs/azure-deployment.md) for container hosting and database configuration.

## Run locally

Requirements: .NET 10 SDK and SQL Server, or Docker with Compose (the default setup uses SQL Server 2022 Developer for local development only). Run the following from this solution directory in PowerShell.

1. Prepare the database:

   ```powershell
   Copy-Item .env.example .env
   # Edit .env and replace MSSQL_SA_PASSWORD with a strong local-only password.
   docker compose up -d --wait
   ```

   Alternatively, use SQL Server LocalDB (`Server=(localdb)\MSSQLLocalDB;Database=steam_museum;Integrated Security=True;Encrypt=True;TrustServerCertificate=True`) or create a `steam_museum` database and dedicated user in an existing SQL Server installation. Use a disposable/local account for development; application runtime does not need schema-management privileges after migrations are applied.

2. Restore and build:

   ```powershell
   dotnet restore SteamMuseum.sln
   dotnet build SteamMuseum.sln --no-restore
   dotnet dev-certs https --trust
   ```

3. Configure the connection and apply the included migrations:

   ```powershell
   $env:Database__Provider = 'SqlServer'
   $env:ConnectionStrings__Museum = 'Server=localhost,1433;Database=steam_museum;User Id=sa;Password=YOUR_LOCAL_SQL_PASSWORD;Encrypt=True;TrustServerCertificate=True'
   dotnet run --project src/SteamMuseum.Api -- --migrate
   ```

4. Create the first administrator explicitly:

   ```powershell
   $env:Bootstrap__Email = 'your-email@example.org'
   $env:Bootstrap__Password = Read-Host 'Temporary administrator password' -MaskInput
   dotnet run --project src/SteamMuseum.Api -- --bootstrap-admin
   Remove-Item Env:\Bootstrap__Password
   Remove-Item Env:\Bootstrap__Email
   ```

   Use at least 12 characters with uppercase, lowercase, a number and a symbol. Bootstrap creates access roles and refuses to run if an administrator already exists. There are no shipped passwords or publicly accessible registration endpoints. New accounts must change their temporary passwords before accessing business endpoints.

5. Start the API:

   ```powershell
   dotnet run --project src/SteamMuseum.Api
   ```

   API: `https://localhost:7240`. Liveness: `/health/live`. The OpenAPI document at `/openapi/v1.json` is available in Development after signing in as an administrator and changing the temporary password. No Swagger UI is bundled.

For persistent local configuration, use `dotnet user-secrets --project src/SteamMuseum.Api set "ConnectionStrings:Museum" "..."` instead of committing credentials. Normal startup neither migrates the database nor creates accounts.

6. Start the separate React frontend (Node.js 22.22.2+ or 24.15.0+):

   ```powershell
   Set-Location ../SteamMuseumWeb
   npm ci
   npm run dev
   ```

   Open `https://localhost:5173` and sign in with your administrator account. See the [frontend guide](../SteamMuseumWeb/README.md) for local certificates, API configuration, tests and production hosting.

## Authentication from React

Use HTTPS for both the API and frontend. The development frontend origin defaults to `https://localhost:5173`; use the same hostname (`localhost` on both) for SameSite cookies. Production must keep the frontend and API on the same HTTPS site for the existing SameSite=Strict cookies. Separate hosts such as https://staff.example.org and https://api.example.org are supported: build the frontend with VITE_API_URL=https://api.example.org and set Cors__Origins__0=https://staff.example.org on the API. A shared origin through a reverse proxy also works. Unrelated default cloud hostnames are cross-site and will not work with these cookies.

1. Fetch `GET /api/auth/csrf` with `credentials: 'include'`.
2. Send the returned `token` as `X-CSRF-TOKEN` on **every mutating request**, including login.
3. Send `POST /api/auth/login` with `{ "email": "...", "password": "..." }`.
4. Fetch a fresh CSRF token after login, password changes, logout or switching users.
5. Use `/api/auth/me` to get profile, roles and `mustChangePassword`. If true, call `/api/auth/password` before other API calls.

```typescript
const api = 'https://localhost:7240';
let csrfToken: string;

async function refreshCsrf() {
  const response = await fetch(`${api}/api/auth/csrf`, { credentials: 'include' });
  if (!response.ok) throw new Error('Unable to obtain CSRF token');
  csrfToken = (await response.json()).token;
}

async function write(path: string, method: string, body?: unknown) {
  const response = await fetch(`${api}${path}`, {
    method,
    credentials: 'include',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) throw await response.json();
  return response.status === 204 ? undefined : response.json();
}
```

Sessions use HttpOnly, Secure, SameSite=Strict cookies. No access tokens need to be stored in browser storage. Security stamps are checked on every authenticated request; logout revokes all of that account's sessions. Deactivation, password resets and role changes also revoke sessions. Login has per-address rate limiting and account lockout after five failures. Public email-based password recovery and MFA are not yet implemented; administrators can issue a new temporary password.

See [example requests](docs/requests.http) and [endpoint guide](docs/api.md).

## Business rules

- A missing daily response means **not responded**, not available. Unavailable is an explicit response.
- `from` and `until` are either both null (all day), or both present in increasing order. Overnight duties are not supported in this first version.
- Duty dates and times are local operating calendar values; submission deadlines and audit timestamps are UTC. A future multi-time-zone scheduling feature must define each railway's time zone before converting duties to instants.
- `preferredRole: null` means any qualified role. A specific role is a preference, not a restriction; it is shown on the planner roster and never bypasses competence checks.
- Each duty represents one role slot and counts as **one shift**. Two distinct assignments on one day count as two shifts. A driver-and-guard operating day therefore has separate duty slots.
- Draft and published assignments count towards limits. Cancelled assignments do not. Zero prevents any assignments; null means no member-specified limit.
- Every window covering the duty's date is checked. An event assignment counts towards the monthly maximum even if it was arranged through the event window.
- Overlapping windows share one daily response per member/date. Changing that response through an open window changes what all overlapping windows show. Each window has its own maximum.
- Saving availability upserts the supplied dates. Omitted dates are unchanged. `maximumAssignments` replaces the existing maximum; send the current maximum if only editing dates.
- Members can change availability after assignment; affected assignments show live conflict warnings on the roster. The API does not silently cancel them. There is no email/push notification service yet.
- Members cannot lower a maximum below their existing assignment count. A planner must cancel assignments first.
- Element competence is valid from the assessment date until (but not including) its reassessment due date. Every duty requires a competence role matching its railway, category and any locomotive scope. Unlinked existing duties cannot be assigned or published.
- Training records alone never authorize an assignment. An assessor must record a valid competence with assessment evidence.
- Publication rechecks every rule. Existing published assignments remain visible with warnings if availability or competence subsequently changes; planners must resolve those warnings.
- Reassign by cancelling then assigning again. Audit entries retain the original member and status history. Duty definitions are immutable through this API; duty editing/deletion workflows are not yet supplied.

All business writes acquire a database-backed mutation lock inside a transaction before reading business state. This prevents two API instances/planners from simultaneously consuming the same capacity. It deliberately serializes mutations for a museum-sized workload. This can later be replaced with finer-grained locks while preserving the tests and invariants. No API allows a planner to override competence or the member's maximum.

## Verification

```powershell
# Fast relational tests using isolated SQLite databases:
dotnet test SteamMuseum.sln

# Same tests against SQL Server, including real migrations and concurrent requests:
$env:MUSEUM_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test SteamMuseum.sln
Remove-Item Env:\MUSEUM_TEST_SQLSERVER
```

The SQL Server test account must be able to create/drop databases. Tests create and delete their own randomly named `steammuseum_test_*` databases; they do not use the database named in the connection string. Use a test server. The CI workflow runs the suite against SQLite, SQL Server 2022 and legacy MySQL 8.4, and builds the Docker image. To test MySQL locally, set only `MUSEUM_TEST_MYSQL` to a disposable-server connection string; keep `MUSEUM_TEST_SQLSERVER` unset.

Local verification includes SQL Server LocalDB and SQLite with .NET SDK 10.0.401, plus earlier MySQL runs. See the [verification record](docs/verification.md) for results and the local Docker-engine limitation.

To add migrations:

```powershell
dotnet tool restore
$env:Database__Provider = 'SqlServer'
dotnet ef migrations add YourChange --project src/SteamMuseum.SqlServerMigrations --startup-project src/SteamMuseum.Api --output-dir Migrations
```

For legacy MySQL development, use `docker compose -f compose.mysql.yaml up -d --wait`, set `Database__Provider=MySql`, and supply your MySQL connection string. Generate MySQL migrations in `src/SteamMuseum.Infrastructure` with `Database__Provider=MySql`. Moving existing records between providers requires a separate data-transfer procedure; the SQL Server migrations initialize a new database.

## Deployment configuration

- Supply `ConnectionStrings__Museum` through secret configuration and use database TLS for remote database connections.
- Set `AllowedHosts` and `Cors__Origins__0` to your actual application hosts. Do not use wildcard credentialed CORS.
- Terminate HTTPS securely. If using a reverse proxy, configure trusted forwarded headers for that specific deployment; do not blindly trust arbitrary forwarding headers. Without this, login throttling sees the proxy address.
- Persist the ASP.NET Data Protection key ring (`DataProtection__KeysPath`) with restricted access and an appropriate at-rest protection mechanism. Share the key ring across API instances. Back it up along with the database.
- Apply migrations with an operator/deployment identity before starting the runtime application. Establish backups and test recovery before entering real records.
- Review the access-role assignments and operating rules with the museum before live rostering.

## Next modules

1. Candidate eligibility previews and richer planner calendar views.
2. Competence export/import with preview and manual verification.
3. Locomotive and rolling stock asset details, maintenance records, scheduled work and service restrictions that also gate assignments.
4. Group bookings and pricing; Xero OAuth connection, invoice mapping, idempotent creation, retry/reconciliation and invoice status updates. No Xero credentials or API calls are part of this implementation.

Competence elements, reassessments, operating roles and variants: see [the competence workflow guide](docs/competence-elements.md).
