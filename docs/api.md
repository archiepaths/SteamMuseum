# Endpoint guide

Business endpoints use the `/api` prefix. JSON enums are strings. Cookie-authenticated writes require the CSRF header and cookie; successfully validated mobile bearer requests do not. Unauthenticated requests receive 401, insufficient access receives 403, validation receives 400, missing resources receive 404 and business conflicts receive 409. Business errors use Problem Details. Login/token throttling returns 429.

Native clients use `/connect/authorize` and `/connect/token` through OpenID Connect discovery. Protocol errors use OAuth error responses. See [mobile authentication](mobile-authentication.md) for PKCE, registration, token rotation and revocation. Existing browser sign-in endpoints and CSRF behavior are unchanged.

## Accounts

| Method | Route | Access | Purpose |
|---|---|---|---|
| GET | `/auth/csrf` | Public | Issue CSRF cookie/token |
| POST | `/auth/login` | Public + CSRF | Email/password login; 204 with cookie |
| GET | `/auth/me` | Signed in | Current identity, roles, password-change flag |
| POST | `/auth/password` | Signed in | `{currentPassword,newPassword}` |
| POST | `/auth/logout` | Signed in | Revoke all sessions and clear cookie |
| POST | `/accounts` | Administrator | `{email,displayName,temporaryPassword,roles}` |
| PUT | `/accounts/{id}/active` | Administrator | `{active}` |
| PUT | `/accounts/{id}/roles` | Administrator | `{roles:["Member","Planner"]}`; Member is always retained |
| POST | `/accounts/{id}/reset-password` | Administrator | `{temporaryPassword}`; forces password change |

## Availability and reference data

| Method | Route | Access |
|---|---|---|
| GET | `/windows` | Signed in |
| POST | `/windows` | Planner or Administrator |
| PUT | `/windows/{id}/state` | Planner or Administrator |
| GET / PUT | `/me/availability/{windowId}` | Own records only |
| GET | `/members/{memberId}/availability/{windowId}` | Planner or Administrator |
| GET | `/members` | Planner, Assessor or Administrator |
| GET | `/railways`, `/locomotives` | Signed in |
| POST | `/railways`, `/locomotives` | Administrator |

Window creation: `{name,kind,start,end,submissionDeadlineUtc}`, with `kind` equal to `Monthly` or `SpecialEvent`. State changes: `{open,deadlineUtc}`. Monthly windows cover exactly a calendar month and cannot be duplicated.

Availability writes: `{maximumAssignments,days:[{date,status,from,until,preferredRole,note}]}`. Status is `Available` or `Unavailable`. Available days with no time restriction use null `from` and `until`. Unavailable days must omit times and role. Dates are unique within each request. GET returns `{window,maximumAssignments,assigned,days}`.

## Competence and training

| Method | Route | Access |
|---|---|---|
| GET | `/me/competences`, `/me/training` | Own records only |
| GET | `/members/{memberId}/competences`, `/members/{memberId}/training` | Planner, Assessor or Administrator |
| POST | `/competences` | Assessor or Administrator |
| POST | `/competences/{id}/revoke` | Assessor or Administrator |
| POST | `/training` | Assessor or Administrator |

Competence creation: `{memberId,role,railwayId,locomotiveId,validFrom,validUntil,evidence}`. `validUntil` and, for non-driving roles, `locomotiveId` may be null. Revoke: `{reason}`. Training: `{memberId,date,notes}`. Roles: `Driver`, `Guard`, `Fireman`, `StationStaff`. These operating roles are distinct from account access roles.

## Rostering

| Method | Route | Access |
|---|---|---|
| POST | `/duties` | Planner or Administrator |
| POST | `/duties/{id}/assignment` | Planner or Administrator |
| POST | `/assignments/{id}/publish` | Planner or Administrator |
| POST | `/assignments/{id}/cancel` | Planner or Administrator |
| GET | `/roster?from=YYYY-MM-DD&until=YYYY-MM-DD` | Planner or Administrator |
| GET | `/me/roster?from=YYYY-MM-DD&until=YYYY-MM-DD` | Own published assignments only |
| GET | `/audit?sinceUtc=...Z` | Administrator |

Duty creation: `{name,date,start,end,role,railwayId,locomotiveId}`. Assignment: `{memberId}`. Publication/cancellation require no body. Roster range is limited to 93 calendar days; the audit query accepts a UTC start within the preceding 31 days.

Each roster row contains `{duty,assignment,issues,preferredRole}`. Unassigned duties have a null assignment. Issues are recalculated from current records on every read. The current endpoints are intended for a single museum deployment; tenant isolation is not implemented.

### Special-event ranges and window notes

`POST /api/windows` accepts optional `notes` (up to 2,000 characters). Special events also accept `dateRanges`, for example:

```json
{
  "name": "Autumn gala weekends",
  "kind": "SpecialEvent",
  "dateRanges": [
    { "start": "2030-10-05", "end": "2030-10-06" },
    { "start": "2030-10-12", "end": "2030-10-13" }
  ],
  "submissionDeadlineUtc": "2030-09-30T23:00:00Z",
  "notes": "Meet at the station.\nPlease bring lunch."
}
```

Ranges are inclusive, sorted by the server, must not overlap, and must fit within the existing 367-day window span. With `dateRanges`, the returned `start`/`end` are the earliest/latest event dates. Without it, the existing `start`/`end` request remains supported. Monthly windows continue to require one complete calendar month and do not accept `dateRanges`.

Gap dates are excluded from the calendar, availability reads/writes and event assignment counts/limits. Daily responses still remain shared with overlapping windows. Existing windows retain their original consecutive dates.

Planners can update or clear notes with `PUT /api/windows/{id}/notes` and `{ "notes": "Updated instructions" }` (use null or an empty string to clear). Notes appear above the availability calendar and preserve line breaks. The existing state/deadline endpoint preserves notes.

Apply the `AddWindowDateRangesAndNotes` migration before running this version against an existing database, using the migration command in the README. It adds a nullable notes column and a date-range table; it does not change existing responses.
