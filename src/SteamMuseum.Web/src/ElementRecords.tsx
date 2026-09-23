import { useState } from "react";
import { request } from "./api";
import { localDate, prettyDate, roleName } from "./dates";
import {
  roles,
  type CompetenceElement,
  type CompetenceRole,
  type ElementAssessment,
  type Reference,
  type RoleEligibility,
} from "./types";
import {
  ActionForm,
  Badge,
  Empty,
  Field,
  Status,
  useResource,
  value,
  nullable,
} from "./ui";

export default function ElementRecords({
  prefix,
  memberId,
  manage,
}: {
  prefix: string | null;
  memberId: string;
  manage: boolean;
}) {
  const elements = useResource<CompetenceElement[]>("/competence-elements");
  const catalog = useResource<CompetenceRole[]>("/competence-roles");
  const assessments = useResource<ElementAssessment[]>(
    prefix ? `${prefix}/element-assessments` : null,
  );
  const [date, setDate] = useState(localDate);
  const eligibility = useResource<RoleEligibility[]>(
    prefix && date ? `${prefix}/role-eligibility?on=${date}` : null,
  );
  const railways = useResource<Reference[]>("/railways");
  const locomotives = useResource<Reference[]>("/locomotives");
  const refresh = () => {
    elements.reload();
    catalog.reload();
    assessments.reload();
    eligibility.reload();
  };
  return (
    <>
      {[elements, catalog, assessments, eligibility].map((r, i) => (
        <Status key={i} resource={r} />
      ))}
      <h2>Role eligibility</h2>
      <Field label="Check competence on date">
        <input
          type="date"
          required
          value={date}
          onChange={(e) => setDate(e.target.value)}
        />
      </Field>
      {eligibility.data?.length === 0 && (
        <Empty>No competence roles have been defined yet.</Empty>
      )}
      <div className="record-grid">
        {eligibility.data?.map((role) => (
          <section className="card" key={role.roleId}>
            <div className="section-heading">
              <h3>{role.name}</h3>
              <Badge tone={role.qualified ? "green" : ""}>
                {role.qualified ? "Qualified" : "Not qualified"}
              </Badge>
            </div>
            <ul>
              {role.elements.map((e) => (
                <li key={e.elementId}>
                  {e.name}: <strong>{e.status}</strong>
                  {e.reassessmentDue &&
                    ` · Reassessment due ${prettyDate(e.reassessmentDue)}`}
                </li>
              ))}
            </ul>
            {role.issues
              .filter(
                (issue) =>
                  !role.elements.some((e) => issue.startsWith(`${e.name}:`)),
              )
              .map((issue) => (
                <p key={issue} className="notice">
                  {issue}
                </p>
              ))}
          </section>
        ))}
      </div>
      <h2>Element assessments</h2>
      <p className="muted">
        The latest assessment for an element determines competence. A failed or
        revoked assessment does not fall back to an earlier pass.
      </p>
      {manage && memberId && (
        <details className="card">
          <summary>Record assessment or reassessment</summary>
          <ActionForm
            key={memberId}
            submit="Record assessment"
            onSubmit={async (f) => {
              await request("/element-assessments", "POST", {
                memberId,
                elementId: value(f, "elementId"),
                outcome: value(f, "outcome"),
                assessedOn: value(f, "assessedOn"),
                evidence: value(f, "evidence"),
              });
              refresh();
            }}
          >
            <Field label="Competence element">
              <select name="elementId" required defaultValue="">
                <option value="" disabled>
                  Select element
                </option>
                {elements.data
                  ?.filter((e) => e.active)
                  .map((e) => (
                    <option key={e.id} value={e.id}>
                      {e.name} · {e.reassessmentMonths} months
                    </option>
                  ))}
              </select>
            </Field>
            <Field label="Assessment outcome">
              <select name="outcome" required defaultValue="">
                <option value="" disabled>
                  Select outcome
                </option>
                <option value="Competent">Competent</option>
                <option value="NotCompetent">Not competent</option>
              </select>
            </Field>
            <Field label="Assessment date">
              <input
                type="date"
                name="assessedOn"
                required
                max={localDate()}
                defaultValue={localDate()}
              />
            </Field>
            <Field label="Assessment evidence">
              <textarea name="evidence" required maxLength={2000} />
            </Field>
          </ActionForm>
        </details>
      )}
      {assessments.data?.length === 0 && (
        <Empty>No element assessments recorded.</Empty>
      )}
      <div className="record-grid">
        {[...(assessments.data ?? [])]
          .sort(
            (a, b) =>
              b.assessedOn.localeCompare(a.assessedOn) ||
              b.sequence - a.sequence,
          )
          .map((a) => (
            <article className="card" key={a.id}>
              <h3>
                {elements.data?.find((e) => e.id === a.elementId)?.name ??
                  "Element"}
              </h3>
              <Badge
                tone={
                  a.outcome === "Competent" &&
                  !a.revokedAtUtc &&
                  a.reassessmentDue > localDate()
                    ? "green"
                    : ""
                }
              >
                {a.revokedAtUtc
                  ? "Revoked"
                  : a.outcome === "Competent"
                    ? "Competent at assessment"
                    : "Not competent"}
              </Badge>
              <p>
                Assessed {prettyDate(a.assessedOn)} · Reassessment due{" "}
                {prettyDate(a.reassessmentDue)}
              </p>
              <p className="preserve">{a.evidence}</p>
              {a.revocationReason && (
                <p className="error">{a.revocationReason}</p>
              )}
              {manage && !a.revokedAtUtc && (
                <details>
                  <summary>Revoke assessment</summary>
                  <ActionForm
                    submit="Revoke assessment"
                    onSubmit={async (f) => {
                      await request(
                        `/element-assessments/${a.id}/revoke`,
                        "POST",
                        { reason: value(f, "reason") },
                      );
                      refresh();
                    }}
                  >
                    <Field label="Reason">
                      <textarea name="reason" maxLength={500} required />
                    </Field>
                  </ActionForm>
                </details>
              )}
            </article>
          ))}
      </div>
      <details className="card">
        <summary>Competence elements and role requirements</summary>
        <h2>Competence elements</h2>
        {manage && (
          <details>
            <summary>Add competence element</summary>
            <ElementForm refresh={refresh} />
          </details>
        )}
        {elements.data?.map((e) => (
          <section className="card" key={e.id}>
            <h3>
              {e.name}
              {!e.active && " (inactive)"}
            </h3>
            <p>
              {e.learningType === "TheoryAndPractical"
                ? "Theory and practical"
                : e.learningType}{" "}
              · Reassess every {e.reassessmentMonths} months
            </p>
            <p className="preserve">{e.description}</p>
            {manage && (
              <details>
                <summary>Edit element</summary>
                <ElementForm element={e} refresh={refresh} />
              </details>
            )}
          </section>
        ))}
        <h2>Roles and variants</h2>
        {manage && (
          <details>
            <summary>Add role or variant</summary>
            <RoleForm
              elements={elements.data ?? []}
              catalog={catalog.data ?? []}
              railways={railways.data ?? []}
              locomotives={locomotives.data ?? []}
              refresh={refresh}
            />
          </details>
        )}
        {catalog.data?.map((r) => (
          <section className="card" key={r.id}>
            <h3>
              {r.name}
              {!r.active && " (inactive)"}
            </h3>
            {r.baseRoleId && (
              <p>
                Inherits all requirements from{" "}
                <strong>
                  {catalog.data?.find((p) => p.id === r.baseRoleId)?.name}
                </strong>
                .
              </p>
            )}
            <ul>
              {r.requirements.map((req) => (
                <li key={req.elementId}>
                  {elements.data?.find((e) => e.id === req.elementId)?.name}
                </li>
              ))}
            </ul>
            {manage && (
              <details>
                <summary>Edit requirements</summary>
                <RoleForm
                  role={r}
                  elements={elements.data ?? []}
                  catalog={catalog.data ?? []}
                  railways={railways.data ?? []}
                  locomotives={locomotives.data ?? []}
                  refresh={refresh}
                />
              </details>
            )}
          </section>
        ))}
      </details>
    </>
  );
}
function ElementForm({
  element,
  refresh,
}: {
  element?: CompetenceElement;
  refresh: () => void;
}) {
  return (
    <ActionForm
      submit={element ? "Save element" : "Create element"}
      onSubmit={async (f) => {
        await request(
          element
            ? `/competence-elements/${element.id}`
            : "/competence-elements",
          element ? "PUT" : "POST",
          {
            name: value(f, "name"),
            description: value(f, "description"),
            learningType: value(f, "learningType"),
            reassessmentMonths: Number(value(f, "months")),
            active: value(f, "active") === "true",
          },
        );
        refresh();
      }}
    >
      <Field label="Element name">
        <input
          name="name"
          required
          maxLength={150}
          defaultValue={element?.name}
        />
      </Field>
      <Field label="Learning and assessment requirements">
        <textarea
          name="description"
          maxLength={2000}
          defaultValue={element?.description}
        />
      </Field>
      <Field label="Learning type">
        <select
          name="learningType"
          defaultValue={element?.learningType ?? "TheoryAndPractical"}
        >
          <option value="Theory">Theory</option>
          <option value="Practical">Practical</option>
          <option value="TheoryAndPractical">Theory and practical</option>
        </select>
      </Field>
      <Field label="Reassessment period (months)">
        <input
          type="number"
          name="months"
          min={1}
          max={120}
          required
          defaultValue={element?.reassessmentMonths ?? 12}
        />
      </Field>
      <p className="muted">
        Period changes apply to new assessments. Existing due dates are
        preserved.
      </p>
      <Field label="Element state">
        <select name="active" defaultValue={String(element?.active ?? true)}>
          <option value="true">Active</option>
          <option value="false">Inactive</option>
        </select>
      </Field>
    </ActionForm>
  );
}
function RoleForm({
  role,
  elements,
  catalog,
  railways,
  locomotives,
  refresh,
}: {
  role?: CompetenceRole;
  elements: CompetenceElement[];
  catalog: CompetenceRole[];
  railways: Reference[];
  locomotives: Reference[];
  refresh: () => void;
}) {
  const [baseId, setBaseId] = useState(role?.baseRoleId ?? "");
  const parent = catalog.find((r) => r.id === baseId);
  return (
    <ActionForm
      submit={role ? "Save role" : "Create role"}
      onSubmit={async (f) => {
        await request(
          role ? `/competence-roles/${role.id}` : "/competence-roles",
          role ? "PUT" : "POST",
          {
            name: value(f, "name"),
            baseRoleId: role?.baseRoleId ?? (baseId || null),
            category:
              role?.category ?? parent?.category ?? value(f, "category"),
            railwayId:
              role?.railwayId ?? parent?.railwayId ?? value(f, "railwayId"),
            locomotiveId: role
              ? role.locomotiveId
              : (parent?.locomotiveId ?? nullable(f, "locomotiveId")),
            elementIds: f.getAll("elementIds"),
            active: value(f, "active") === "true",
          },
        );
        refresh();
      }}
    >
      <Field label="Role name">
        <input name="name" required maxLength={150} defaultValue={role?.name} />
      </Field>
      <Field label="Base role">
        <select
          disabled={!!role}
          value={baseId}
          onChange={(e) => setBaseId(e.target.value)}
        >
          <option value="">None — standalone role</option>
          {catalog
            .filter((r) => !r.baseRoleId && r.active && r.id !== role?.id)
            .map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
              </option>
            ))}
        </select>
      </Field>
      {parent && (
        <p>
          Includes:{" "}
          {parent.requirements
            .map((r) => elements.find((e) => e.id === r.elementId)?.name)
            .join(", ")}
        </p>
      )}
      {!parent && (
        <>
          <Field label="Role category">
            <select
              name="category"
              disabled={!!role}
              defaultValue={role?.category ?? "Driver"}
            >
              {roles.map((r) => (
                <option key={r} value={r}>
                  {roleName(r)}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Role railway">
            <select
              name="railwayId"
              required
              disabled={!!role}
              defaultValue={role?.railwayId ?? ""}
            >
              <option value="" disabled>
                Select railway
              </option>
              {railways.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}
                </option>
              ))}
            </select>
          </Field>
        </>
      )}
      <Field label="Role locomotive (optional)">
        <select
          name="locomotiveId"
          disabled={!!role || !!parent?.locomotiveId}
          defaultValue={role?.locomotiveId ?? parent?.locomotiveId ?? ""}
        >
          <option value="">Any locomotive</option>
          {locomotives.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}
            </option>
          ))}
        </select>
      </Field>
      <fieldset className="role-options">
        <legend>
          {parent ? "Additional required elements" : "Required elements"}
        </legend>
        {elements
          .filter(
            (e) =>
              (e.active ||
                role?.requirements.some((r) => r.elementId === e.id)) &&
              !parent?.requirements.some((r) => r.elementId === e.id),
          )
          .map((e) => (
            <label key={e.id}>
              <input
                type="checkbox"
                name="elementIds"
                value={e.id}
                defaultChecked={role?.requirements.some(
                  (r) => r.elementId === e.id,
                )}
              />
              {e.name}
              {!e.active && " (inactive)"}
            </label>
          ))}
      </fieldset>
      <Field label="Role state">
        <select name="active" defaultValue={String(role?.active ?? true)}>
          <option value="true">Active</option>
          <option value="false">Inactive</option>
        </select>
      </Field>
    </ActionForm>
  );
}
