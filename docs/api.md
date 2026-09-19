# Endpoint guide

All endpoints use the `/api` prefix. JSON enums are strings. Mutating requests require the CSRF header and cookie. Unauthenticated requests receive 401, insufficient access receives 403, validation receives 400, missing resources receive 404 and business conflicts receive 409. Error responses use Problem Details. Login throttling returns 429.

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
