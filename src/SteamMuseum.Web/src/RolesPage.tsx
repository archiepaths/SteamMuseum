import { Link, useParams } from "react-router";
import type { CompetenceElement, CompetenceRole, Reference } from "./types";
import { Badge, Empty, Status, useResource } from "./ui";
import { ElementForm, RoleForm } from "./CompetenceForms";

export default function RolesPage({ manage }: { manage: boolean }) {
  const { roleId } = useParams();
  const catalog = useResource<CompetenceRole[]>("/competence-roles");
  const elements = useResource<CompetenceElement[]>("/competence-elements");
  const railways = useResource<Reference[]>("/railways");
  const locomotives = useResource<Reference[]>("/locomotives");
  const refresh = () => {
    catalog.reload();
    elements.reload();
  };
  const role = catalog.data?.find((r) => r.id === roleId);
  const parent = catalog.data?.find((r) => r.id === role?.baseRoleId);
  const formProps = {
    catalog: catalog.data ?? [],
    elements: elements.data ?? [],
    railways: railways.data ?? [],
    locomotives: locomotives.data ?? [],
    refresh,
  };
  function elementCard(element: CompetenceElement, inherited = false) {
    return (
      <article className="card" key={element.id}>
        <div className="section-heading">
          <h3>{element.name}</h3>
          <Badge>
            {inherited ? "Inherited" : "Required"}
            {!element.active && " · Inactive"}
          </Badge>
        </div>
        <p>
          {element.learningType === "TheoryAndPractical"
            ? "Theory and practical"
            : element.learningType}{" "}
          · Reassess every {element.reassessmentMonths} months
        </p>
        <p className="preserve">{element.description}</p>
        {manage && (
          <details>
            <summary>Edit element</summary>
            <ElementForm element={element} refresh={refresh} />
          </details>
        )}
      </article>
    );
  }
  return (
    <>
      {[catalog, elements, railways, locomotives].map((r, i) => (
        <Status key={i} resource={r} />
      ))}
      {roleId ? (
        <>
          <Link to="/roles">← All roles</Link>
          {catalog.data && !role && <Empty>Role not found.</Empty>}
          {role && (
            <>
              <h2>{role.name}</h2>
              <Badge tone={role.active ? "green" : ""}>
                {role.active ? "Active" : "Inactive"}
              </Badge>
              <p>
                {railways.data?.find((r) => r.id === role.railwayId)?.name}
                {role.locomotiveId &&
                  ` · ${locomotives.data?.find((l) => l.id === role.locomotiveId)?.name ?? "Locomotive"}`}
              </p>
              {parent && (
                <p>
                  Includes every requirement of{" "}
                  <Link to={`/roles/${parent.id}`}>{parent.name}</Link>, plus
                  the additional elements below.
                </p>
              )}
              <h2>Required competence elements</h2>
              <div className="record-grid">
                {[
                  ...new Set([
                    ...(parent?.requirements ?? []).map((r) => r.elementId),
                    ...role.requirements.map((r) => r.elementId),
                  ]),
                ].map((id) => {
                  const element = elements.data?.find((e) => e.id === id);
                  return element ? (
                    elementCard(
                      element,
                      !!parent?.requirements.some((r) => r.elementId === id),
                    )
                  ) : (
                    <p key={id}>Element details unavailable.</p>
                  );
                })}
              </div>
              {manage && (
                <details className="card">
                  <summary>Edit role requirements</summary>
                  <RoleForm key={role.id} role={role} {...formProps} />
                </details>
              )}
              {catalog.data?.some((r) => r.baseRoleId === role.id) && (
                <section className="card">
                  <h2>Variants</h2>
                  <ul>
                    {catalog.data
                      .filter((r) => r.baseRoleId === role.id)
                      .map((r) => (
                        <li key={r.id}>
                          <Link to={`/roles/${r.id}`}>{r.name}</Link>
                        </li>
                      ))}
                  </ul>
                </section>
              )}
            </>
          )}
        </>
      ) : (
        <>
          <p className="intro">
            Select a role to see its required learning and assessment elements.
          </p>
          {catalog.data?.length === 0 && <Empty>No roles defined yet.</Empty>}
          <div className="record-grid">
            {catalog.data?.map((r) => (
              <article className="card" key={r.id}>
                <h2>
                  <Link to={`/roles/${r.id}`}>{r.name}</Link>
                </h2>
                <Badge>
                  {r.baseRoleId ? "Variant" : "Base role"}
                  {!r.active && " · Inactive"}
                </Badge>
                <p>
                  {r.requirements.length}{" "}
                  {r.baseRoleId ? "additional" : "required"} elements
                </p>
                {r.baseRoleId && (
                  <p>
                    Based on{" "}
                    {catalog.data?.find((p) => p.id === r.baseRoleId)?.name}
                  </p>
                )}
              </article>
            ))}
          </div>
          {manage && (
            <details className="card">
              <summary>Add role or variant</summary>
              <RoleForm {...formProps} />
            </details>
          )}
          <details className="card">
            <summary>Competence element library</summary>
            {manage && (
              <details>
                <summary>Add competence element</summary>
                <ElementForm refresh={refresh} />
              </details>
            )}
            {elements.data?.map((e) => elementCard(e))}
          </details>
        </>
      )}
    </>
  );
}
