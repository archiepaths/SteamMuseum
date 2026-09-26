import { Link } from "react-router";
import { useState } from "react";
import { request } from "./api";
import { localDate, prettyDate } from "./dates";
import {
  type CompetenceElement,
  type CompetenceRole,
  type ElementAssessment,
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
              <h3>
                <Link to={`/roles/${role.roleId}`}>{role.name}</Link>
              </h3>
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
      <p>
        <Link to="/roles">Browse roles and competence elements</Link>
      </p>
    </>
  );
}
