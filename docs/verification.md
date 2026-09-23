# Verification record

## Legacy qualification retirement — 23 September 2026

- Backend suite: **37 passed**, after replacing obsolete qualification tests with element-based checks. The MySQL-only archive assertion is exercised in the separate provider run below.
- Targeted MySQL suite: **7 passed**, including archival migration up/down preservation, removed endpoints, unlinked-duty rejection, element-based roster workflow and concurrent shift limits.
- Frontend: **24 passed**, production build successful. EF reports no pending model changes.
- SQLite test factories now disable pooling instead of clearing every factory's pool, fixing a parallel-test disposal race found during verification.
- `RetireLegacyCompetence` preserves old evidence by renaming the table to `ArchivedCompetence`; the application has no legacy entity, routes, screens or authorization fallback. Existing duties require a linked competence role before assignment/publication.
- No migration was applied to the user's application database.

## Competence elements and roles — 23 September 2026

- Release backend suite against isolated SQLite databases: **42 passed**. The three element-competence tests were rerun successfully after the final requirement-edit validation changes.
- Four affected competence and existing roster tests against disposable MySQL Server 26.7.0: **4 passed**, including real migrations and persisted inherited requirements.
- Frontend: **24 tests passed**; production build successful.
- EF model/migration consistency: no pending changes.
- Checks cover base/variant requirements, per-member isolation, due dates, failed/revoked reassessments, inactive roles/elements, inherited edits, preserved assessment periods, assignment/publication rejection, catalogue permissions and duty scope submission.
- No existing application database was migrated and no real assessment records were created. Existing development API process was left running; Release outputs were used to avoid its locked Debug files.

## Availability date ranges and window notes — 21 September 2026

- Full backend suite against isolated SQLite databases: **39 passed**.
- Four affected range, notes, roster and limit tests against disposable MySQL Server 26.7.0: **4 passed**, with real migrations.
- Frontend: **19 tests passed**; production build successful.
- EF model/migration consistency: no pending changes.
- Coverage includes nonconsecutive event dates, gap rejection, shared responses, assignment counts and limits, notes persistence/editing/clearing, planner permissions, range validation and calendar group selection.
- Added `AddWindowDateRangesAndNotes`; existing windows keep their original dates. No application database was migrated during this work.

## Mobile authentication — 20 September 2026

- Release solution build: successful, zero warnings and errors.
- Full suite using isolated SQLite databases: **36 passed, 0 failed**.
- Full suite using an isolated MySQL Server 26.7.0 instance on a separate loopback port: **36 passed, 0 failed**, including real migrations and concurrent refresh exchanges.
- React frontend: **13 tests passed**, production build successful.
- EF Core model/migration consistency: no pending changes after adding the OpenIddict tables.
- OpenIddict: 7.7.1. Resolved EF Core runtime: 10.0.11. Connector/NET: 10.0.9.

New integration tests cover discovery, signed access tokens, S256 PKCE, redirect/client/verifier rejection, hosted login and forced password-change forms, access roles and member isolation, cookie/bearer CSRF separation, refresh rotation and replay-family revocation, concurrent refreshes, code replay, account deactivation/password/role/logout revocation, and token signature/issuer/audience/expiry rejection. The original web-cookie and rostering tests run in the same suite.

MySQL verification identified a provider-generated joined bulk UPDATE that could prevent replay revocation. OpenIddict's supported per-record update path is now used inside the exchange transaction, and replay/concurrency checks pass on both providers. Token subjects are bounded to the existing 36-character GUID member IDs to keep MySQL composite indexes within its limits.

No real museum accounts or database records were changed. Production enablement, certificate provisioning and registration of the actual native app callback remain operator steps in the [mobile authentication guide](mobile-authentication.md). No React Native app or device-level OAuth round trip was built/tested in this change. Remote CI was not run in this session.

## Original backend — 19 September 2026

Verified on 19 September 2026.

- Release build: successful, zero warnings and zero errors.
- Automated suite using SQLite: **21 passed, 0 failed**.
- Same suite against an isolated **MySQL Server 26.7.0** instance: **21 passed, 0 failed**.
- EF Core model/migration consistency: no pending model changes.
- SDK: .NET 10.0.401. EF Core and MySQL provider: 10.0.9.

The suite covers real Identity cookie sign-in, CSRF enforcement, member record isolation, authorization policies, forced initial password change, deactivation, role-change session revocation, logout revocation, lockout, availability validation/rollback, closed periods, locomotive/railway qualification matching, qualification dates/revocation, draft visibility, publication checks, overlapping duties, overlapping-window limits and concurrent assignment requests.

MySQL tests apply the included migrations to freshly created test databases. The concurrency test submits two assignments at once with one remaining slot and checks that exactly one succeeds and the other returns a conflict.

The MySQL 8.4 CI configuration is included but was not executed on a remote CI runner in this session. The React frontend, external email delivery, competence import and Xero integration are not included or tested. Production hosting and operational acceptance remain deployment work.

During local verification, Windows TLS restrictions prevented direct NuGet access from the .NET process. Packages were retrieved from official NuGet endpoints through a temporary loopback TLS bridge; the delivered NuGet configuration uses the normal official HTTPS source. Vulnerability auditing was disabled for that local restore only; the delivered project and CI retain NuGet's default auditing behaviour.
