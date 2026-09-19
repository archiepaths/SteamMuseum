# Verification record

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
