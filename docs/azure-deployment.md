# Azure SQL and Azure Container Apps

Run commands from the SteamMuseum API repository root. The sibling SteamMuseumWeb project is built and deployed separately.

## Database configuration

`Database__Provider=SqlServer` is the default. EF Core uses the SQL Server provider and the separate SteamMuseum.SqlServerMigrations assembly. The existing MySQL migrations are preserved and used only with `Database__Provider=MySql`.

Azure SQL password-authentication example (store this as an Azure Container Apps secret, never in source):

```text
Server=tcp:YOUR_SERVER.database.windows.net,1433;Database=steam_museum;User Id=YOUR_APP_USER;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30
```

Managed identity authentication is also supported by Microsoft.Data.SqlClient:

```text
Server=tcp:YOUR_SERVER.database.windows.net,1433;Database=steam_museum;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False
```

For a user-assigned identity, append `User Id=YOUR_IDENTITY_CLIENT_ID`. Assign that identity to the container app and create its database user through a Microsoft Entra administrator:

```sql
CREATE USER [YOUR_IDENTITY_NAME] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [YOUR_IDENTITY_NAME];
ALTER ROLE db_datawriter ADD MEMBER [YOUR_IDENTITY_NAME];
```

Set `ConnectionStrings__Museum` to the chosen connection string. Keep migration privileges on a separate operator identity. Configure Azure SQL networking to permit the Container Apps environment (private endpoint/VNet or explicit permitted egress addresses). Do not assume creating a SQL server makes it reachable.

The SQL Server migration creates a new database schema. It does not copy data from an existing MySQL database. Existing MySQL records and archived historical competence evidence require a separately planned data transfer before switching a live installation.

The provider intentionally uses `UseSqlServer` without EF statement retries. Business changes and native token redemption use explicit serializable transactions; blindly replaying them can duplicate writes or redeem tokens twice. Transient database failures are reported instead of replaying partially completed workflows. Do not add `EnableRetryOnFailure` or change to `UseAzureSql` without designing transaction-wide replay and commit verification.

## Build the API image

Docker must be running with Linux containers:

```powershell
docker build --tag steam-museum-api:local .
```

The multi-stage Dockerfile builds with the .NET 10 SDK and ships only the published application in the ASP.NET runtime image. It runs as the built-in non-root `app` user, listens on HTTP port 8080, and contains both provider migration assemblies. Certificates, local environment files, build outputs and Git metadata are excluded from the build context.

To build directly in Azure Container Registry without local Docker:

```powershell
az acr build --registry YOUR_REGISTRY_NAME --image steam-museum-api:YOUR_RELEASE_TAG .
```

Or push an image you built locally:

```powershell
az acr login --name YOUR_REGISTRY_NAME
docker tag steam-museum-api:local YOUR_REGISTRY_NAME.azurecr.io/steam-museum-api:YOUR_RELEASE_TAG
docker push YOUR_REGISTRY_NAME.azurecr.io/steam-museum-api:YOUR_RELEASE_TAG
```

Use a unique release tag or digest for each deployment.

## Apply migrations before serving requests

Normal API startup does not apply migrations or create accounts. Run one migration operation at a time using an operator connection with schema permissions. For example, against reachable Azure SQL from this machine:

```powershell
$env:Database__Provider = 'SqlServer'
$env:ConnectionStrings__Museum = Read-Host 'Operator SQL connection string' -MaskInput
dotnet run --project src/SteamMuseum.Api -- --migrate
Remove-Item Env:\ConnectionStrings__Museum
```

The same published image can perform this operation as a manually triggered Azure Container Apps Job: use command `dotnet` with arguments `SteamMuseum.Api.dll`, `--migrate`, the operator SQL connection secret, and `Database__Provider=SqlServer`. Give the job network access to SQL and the operator identity when using managed identity. Runtime code needs no SDK or EF tooling for this command.

Bootstrap the first administrator separately using the same image and `--bootstrap-admin`, supplying `Bootstrap__Email` and a secret `Bootstrap__Password`. Use the shared Data Protection key storage for bootstrap and normal runtime. Bootstrap refuses to run if an administrator already exists. Do not leave the bootstrap password configured on the normal API container.

Generate a reviewable idempotent SQL script when required:

```powershell
$env:Database__Provider = 'SqlServer'
dotnet tool restore
dotnet ef migrations script --idempotent --project src/SteamMuseum.SqlServerMigrations --startup-project src/SteamMuseum.Api --output schema.sql
```

## Configure the Container Apps environment

Create a Container Apps environment, Azure SQL database, private Azure Container Registry and user-assigned managed identity. Grant the identity image-pull permission on the registry (`AcrPull` for a registry using standard RBAC), and enable managed identity image pulls for that registry. The checked-in YAML assumes these resources already exist.

Create a private Azure Files share for the Data Protection key ring, then register it on the Container Apps environment with storage name `museum-keys` and ReadWrite access. The template mounts it at `/var/steam-museum/keys`; mount options give UID/GID 1654 (the .NET `app` user) access. These keys keep authentication and CSRF cookies valid across replica changes and restarts. Use encrypted storage with restricted access and retain the key ring with backups. Do not rely on the writable container filesystem for production keys. A host-mounted local directory also needs permissions for UID 1654.

Azure ingress terminates HTTPS and forwards requests internally over HTTP. Set `ReverseProxy__KnownNetworks__0` to the actual trusted ingress proxy CIDR for your environment. Additional ranges use `__1`, `__2`, etc. Determine the immediate peer network for your deployed environment; do not assume the VNet subnet is always the proxy's source network. The API accepts forwarded scheme and client-address headers only from allowlisted proxy addresses, with a single forwarding hop. Do not set the broad `ASPNETCORE_FORWARDEDHEADERS_ENABLED` switch or trust every network. Verify HTTPS requests are recognized as HTTPS before enabling authentication; otherwise redirects and native OIDC transport validation may fail.

Use an API hostname in `AllowedHosts` and the web app's exact HTTPS origin in `Cors__Origins__0`. For cookie authentication, web and API must be on the same HTTPS site, for example `staff.example.org` and `api.example.org`. Configure their custom domains and TLS certificates. Build the separate frontend with `VITE_API_URL=https://api.example.org`.

## Deploy the configured template

```powershell
Copy-Item deploy/azure-container-app.yaml deploy/azure-container-app.local.yaml
```

Replace every `REPLACE_*` placeholder in the local copy, including the image reference, region, environment ID, registry login server, managed identity resource ID, SQL secret, trusted ingress CIDR, API hostname and web hostname. `deploy/*.local.yaml` is ignored by Git. The template sets HTTPS-only external ingress on port 8080, non-root-compatible shared key storage, 0.5 CPU/1 GiB RAM, probes, and scaling from zero to two replicas.

```powershell
az containerapp create --name steam-museum-api --resource-group YOUR_RESOURCE_GROUP --yaml deploy/azure-container-app.local.yaml
```

For subsequent image releases, update the local template's image reference and run:

```powershell
az containerapp update --name steam-museum-api --resource-group YOUR_RESOURCE_GROUP --yaml deploy/azure-container-app.local.yaml
```

Startup and liveness probes use `/health/live` and do not depend on SQL. Readiness uses `/health/ready` and returns 503 if the database or seeded mutation-lock table cannot be read. Probe requests bypass HTTPS redirection and supply the API Host header to satisfy host filtering. Health endpoints are anonymous and expose no credentials. Apply migrations before deploying the revision, then verify readiness and sign-in from the real web origin. Scaling to zero introduces a cold-start delay; set `minReplicas: 1` if keeping the API warm is preferable.

Native OIDC authentication remains disabled by default in production. Enabling it additionally requires the persistent signing/encryption certificates and issuer configuration in [mobile-authentication.md](mobile-authentication.md).

## Verification

```powershell
dotnet test SteamMuseum.sln
$env:MUSEUM_TEST_SQLSERVER = 'Server=(localdb)\MSSQLLocalDB;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test SteamMuseum.sln
Remove-Item Env:\MUSEUM_TEST_SQLSERVER
```

The SQL Server integration suite creates and drops randomly named `steammuseum_test_*` databases on a disposable test server. It applies the real SQL Server migration and tests the same authentication, authorization, token replay, date handling and concurrent-write invariants as the SQLite suite. Azure SQL-specific identity, networking and ingress need verification against your actual Azure resources.

References: [SQL Server provider](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/), [connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency), [Container Apps ingress](https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview), [Azure Files mounts](https://learn.microsoft.com/en-us/azure/container-apps/storage-mounts), [managed identity image pulls](https://learn.microsoft.com/en-us/azure/container-apps/managed-identity-image-pull).
