# Competence elements, assessments and roles

In **Staff records**, assessors and administrators can open **Competence elements and role requirements** to define the learning catalogue. Members can view the catalogue and their own assessments/eligibility. Planners can view staff records but cannot assess or edit the catalogue. These operating roles are separate from account access roles such as Administrator and Planner.

## Elements and assessments

An element represents a discrete learning requirement, for example health and safety induction, personal track safety, Railway A steam driving, or Locomotive B familiarisation. Set a description of the learning/assessment requirements, a learning type (theory, practical, or both), and a reassessment period of 1–120 calendar months. Elements can be edited or deactivated, with an audit record.

Record each assessment and reassessment separately, with an assessment date, competent/not competent outcome and evidence. The authenticated assessor and recording time are stored automatically. Future-dated assessments are rejected. Training history is retained separately and does not confer competence.

The due date is calculated by adding the element's period to the assessment date. A pass is valid **before** its reassessment due date; on that date reassessment is required. Calendar-month arithmetic handles month ends and leap years. The period is copied into each assessment, so later catalogue changes only affect new assessments and never rewrite existing due dates.

For an element, the latest assessment on or before the date being checked determines competence. Where assessments share a date, the latest recorded sequence wins. A failed, revoked or expired assessment cannot reveal an older pass. Backdated entries remain ordered by assessment date. Revocation retains evidence and its reason and invalidates the affected latest result; record a new reassessment to restore competence. Deactivating an element prevents it from qualifying a member.

## Roles and variants

Create a named base role with its railway, operating category, optional locomotive scope and one or more required elements. For example, Railway A Steam Driver can require induction, track safety and Railway A steam driving.

A variant selects a base role and adds its own requirements, such as Locomotive B familiarisation. It inherits the base railway/category and every current base requirement. It may narrow the locomotive scope. Variants cannot inherit other variants. Changing a base role's requirements immediately affects its variants; deactivating the base disables their eligibility too. Role identity/scope and parent are immutable after creation; make a new role for a different operating scope. Names, requirements and active state are editable.

Every inherited and additional element must have a current competent result. The role eligibility cards explain missing, failed, revoked, inactive and overdue elements. Use the date selector to check a future duty date. A qualification that expires before a planned duty will not qualify its holder for that duty.

## Rostering and existing records

New duties select a competence role/variant, which fixes their operating category and railway and any locomotive scope. Assignment and publication evaluate its elements on the duty date alongside the existing availability, overlapping-duty and shift-limit checks. Roster conflict reporting also uses these checks. Later assessment failures or requirement changes flag published duties for planner attention; they do not silently cancel assignments or notify staff.

The old qualification feature and `/competences` endpoints have been removed. All new duties require an element-based competence role, even before the role catalogue has been populated. Existing duties without a linked role are flagged and cannot be assigned or published until a planner uses **Set competence role** on the duty board.

`RetireLegacyCompetence` renames the old table to `ArchivedCompetence` to retain historical evidence without exposing or using it in the application. No element passes are inferred from those records. Audit history is retained. Rolling back this migration restores the original table name.

## Setup

Apply the included `AddElementCompetenceAndRoles` and `RetireLegacyCompetence` migrations using the configured database connection:

```powershell
dotnet run --project src/SteamMuseum.Api -- --migrate
```

Restart the API after migration. Define elements, roles/variants and real assessments before using the new roles for duties. No production/staff data is seeded by the migration.

## API additions

- `GET/POST /api/competence-elements`, `PUT /api/competence-elements/{id}`: element catalogue; writes require Assessment policy.
- `GET/POST /api/competence-roles`, `PUT /api/competence-roles/{id}`: role catalogue; writes require Assessment policy. Requests contain `name`, `baseRoleId`, `category`, `railwayId`, `locomotiveId`, `elementIds`, `active`.
- `POST /api/element-assessments`: `memberId`, `elementId`, `outcome` (`Competent` or `NotCompetent`), `assessedOn`, `evidence`; Assessment policy.
- `POST /api/element-assessments/{id}/revoke`: `reason`; Assessment policy.
- `GET /api/me/element-assessments`, `GET /api/me/role-eligibility?on=YYYY-MM-DD`: own history and calculated eligibility; date defaults to current UTC date.
- Corresponding `/api/members/{memberId}/element-assessments` and `/role-eligibility` reads require StaffRecords policy.
- `POST /api/duties` accepts `competenceRoleId`; existing category/railway/locomotive fields must match its scope.
- `PUT /api/duties/{id}/competence-role`: `{ "roleId": "..." }`; Planning policy. Rechecks scope and changes the requirements used for the duty, including conflict reporting on existing assignments.

All mutations retain the existing transaction lock, auditing, authentication and CSRF rules.
