import { useState } from "react";
import { request } from "./api";
import { roleName } from "./dates";
import {
  roles,
  type CompetenceElement,
  type CompetenceRole,
  type Reference,
} from "./types";
import { ActionForm, Field, value, nullable } from "./ui";
export function ElementForm({
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
export function RoleForm({
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
