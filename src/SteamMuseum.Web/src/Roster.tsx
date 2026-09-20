import { useState } from "react";
import { request } from "./api";
import { monthRange, prettyDate, roleName } from "./dates";
import { roles, type Member, type Reference, type Roster } from "./types";
import {
  ActionForm,
  Badge,
  Empty,
  Field,
  Status,
  nullable,
  useResource,
  value,
} from "./ui";

export function DutyFields({
  railways,
  locomotives,
  competence = false,
}: {
  railways: Reference[];
  locomotives: Reference[];
  competence?: boolean;
}) {
  const [role, setRole] = useState("Driver");
  return (
    <>
      <div className="fields">
        <Field label="Operating role">
          <select
            name="role"
            value={role}
            onChange={(e) => setRole(e.target.value)}
          >
            {roles.map((r) => (
              <option key={r} value={r}>
                {roleName(r)}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Railway">
          <select name="railwayId" required defaultValue="">
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
        <Field
          label={`Locomotive${["Driver", "Fireman"].includes(role) ? " (required)" : " (optional)"}`}
        >
          <select
            name="locomotiveId"
            required={["Driver", "Fireman"].includes(role)}
            defaultValue=""
          >
            <option value="">{competence ? "Railway-wide" : "None"}</option>
            {locomotives.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}
              </option>
            ))}
          </select>
        </Field>
      </div>
    </>
  );
}
export default function RosterPage({ planner }: { planner: boolean }) {
  const [range, setRange] = useState(monthRange);
  const [filter, setFilter] = useState("all");
  const roster = useResource<Roster[]>(
    `/${planner ? "" : "me/"}roster?from=${range.from}&until=${range.until}`,
  );
  const members = useResource<Member[]>(planner ? "/members" : null);
  const railways = useResource<Reference[]>("/railways");
  const locomotives = useResource<Reference[]>("/locomotives");
  const rows = roster.data?.filter(
    (row) =>
      filter === "all" ||
      (filter === "issues"
        ? row.issues.length
        : filter === "unassigned"
          ? !row.assignment || row.assignment.status === "Cancelled"
          : row.assignment?.status === filter),
  );
  return (
    <>
      <p className="intro">
        {planner
          ? "Bring the team together. Plan duties, review conflicts and publish the roster."
          : "Your published duties, with the latest operating details."}
      </p>
      <section className="card">
        <ActionForm
          submit="Show dates"
          onSubmit={async (f) => {
            const from = value(f, "from");
            const until = value(f, "until");
            const span = (Date.parse(until) - Date.parse(from)) / 86400000;
            if (span < 0 || span > 92)
              throw new Error(
                "Choose an increasing date range of no more than 93 days.",
              );
            setRange({ from, until });
          }}
        >
          <div className="fields">
            <Field label="From">
              <input
                name="from"
                type="date"
                defaultValue={range.from}
                required
              />
            </Field>
            <Field label="Until">
              <input
                name="until"
                type="date"
                defaultValue={range.until}
                required
              />
            </Field>
          </div>
        </ActionForm>
      </section>
      {planner && (
        <details className="card">
          <summary>Create a duty</summary>
          <ActionForm
            submit="Create duty"
            onSubmit={async (f) => {
              const start = value(f, "start");
              const end = value(f, "end");
              if (start >= end)
                throw new Error(
                  "The duty must end after it starts on the same day.",
                );
              await request("/duties", "POST", {
                name: value(f, "name"),
                date: value(f, "date"),
                start: `${start}:00`,
                end: `${end}:00`,
                role: value(f, "role"),
                railwayId: value(f, "railwayId"),
                locomotiveId: nullable(f, "locomotiveId"),
              });
              roster.reload();
            }}
          >
            <Field label="Duty name">
              <input name="name" required maxLength={150} />
            </Field>
            <div className="fields">
              <Field label="Date">
                <input
                  name="date"
                  type="date"
                  required
                  defaultValue={range.from}
                />
              </Field>
              <Field label="Start">
                <input name="start" type="time" required />
              </Field>
              <Field label="End">
                <input name="end" type="time" required />
              </Field>
            </div>
            <DutyFields
              railways={railways.data ?? []}
              locomotives={locomotives.data ?? []}
            />
          </ActionForm>
        </details>
      )}
      <div className="section-heading">
        <h2>{planner ? "Duty board" : "Upcoming duties"}</h2>
        {planner && (
          <Field label="Show">
            <select value={filter} onChange={(e) => setFilter(e.target.value)}>
              <option value="all">All duties</option>
              <option value="unassigned">Unassigned</option>
              <option value="Draft">Draft</option>
              <option value="Published">Published</option>
              <option value="issues">Conflicts</option>
            </select>
          </Field>
        )}
      </div>
      {[roster, members, railways, locomotives].map((r, i) => (
        <Status key={i} resource={r} />
      ))}
      {rows?.length === 0 && (
        <Empty>
          No {filter === "all" ? "" : "matching "}duties in this date range.
        </Empty>
      )}
      <div className="duty-list">
        {rows?.map((row) => (
          <section className="card duty" key={row.duty.id}>
            <div className="duty-date">
              <strong>{Number(row.duty.date.slice(-2))}</strong>
              <span>
                {new Date(`${row.duty.date}T12:00:00`).toLocaleDateString(
                  "en-GB",
                  { month: "short", weekday: "short" },
                )}
              </span>
            </div>
            <div className="duty-content">
              <div className="section-heading">
                <h3>{row.duty.name}</h3>
                <Badge
                  tone={row.assignment?.status === "Published" ? "green" : ""}
                >
                  {row.assignment?.status === "Cancelled"
                    ? "Unassigned"
                    : (row.assignment?.status ?? "Unassigned")}
                </Badge>
              </div>
              <p>
                {prettyDate(row.duty.date)} · {row.duty.start.slice(0, 5)}–
                {row.duty.end.slice(0, 5)} · <b>{roleName(row.duty.role)}</b>
              </p>
              <p className="muted">
                {railways.data?.find((r) => r.id === row.duty.railwayId)
                  ?.name ?? "Railway"}
                {row.duty.locomotiveId &&
                  ` / ${locomotives.data?.find((l) => l.id === row.duty.locomotiveId)?.name ?? "Locomotive"}`}
              </p>
              {planner &&
                row.assignment &&
                row.assignment.status !== "Cancelled" && (
                  <p>
                    Assigned to{" "}
                    <b>
                      {members.data?.find(
                        (m) => m.id === row.assignment?.memberId,
                      )?.displayName ?? row.assignment.memberId}
                    </b>
                    {row.preferredRole &&
                      ` · Prefers ${roleName(row.preferredRole)}`}
                  </p>
                )}
              {row.issues.length > 0 && (
                <div className="error" role="status">
                  <b>Needs attention</b>
                  <ul>
                    {row.issues.map((issue) => (
                      <li key={issue}>{issue}</li>
                    ))}
                  </ul>
                </div>
              )}
              {planner && (
                <ActionForm
                  key={row.assignment?.id + ":" + row.assignment?.status}
                  submit={
                    !row.assignment || row.assignment.status === "Cancelled"
                      ? "Assign as draft"
                      : "Update assignment"
                  }
                  onSubmit={async (f) => {
                    if (
                      !row.assignment ||
                      row.assignment.status === "Cancelled"
                    )
                      await request(
                        `/duties/${row.duty.id}/assignment`,
                        "POST",
                        { memberId: value(f, "memberId") },
                      );
                    else
                      await request(
                        `/assignments/${row.assignment.id}/${value(f, "action")}`,
                        "POST",
                      );
                    roster.reload();
                  }}
                >
                  {!row.assignment || row.assignment.status === "Cancelled" ? (
                    <Field label="Staff member">
                      <select name="memberId" required defaultValue="">
                        <option value="" disabled>
                          Select a member
                        </option>
                        {members.data
                          ?.filter((m) => m.active)
                          .map((m) => (
                            <option key={m.id} value={m.id}>
                              {m.displayName}
                            </option>
                          ))}
                      </select>
                    </Field>
                  ) : (
                    <Field label="Assignment action">
                      <select name="action" required defaultValue="">
                        <option value="" disabled>
                          Select an action
                        </option>
                        {row.assignment.status === "Draft" && (
                          <option value="publish">
                            Publish duty to member
                          </option>
                        )}
                        <option value="cancel">Cancel assignment</option>
                      </select>
                    </Field>
                  )}
                  <p className="muted">
                    The server checks availability, competence, overlapping
                    duties and shift limits.
                  </p>
                </ActionForm>
              )}
            </div>
          </section>
        ))}
      </div>
    </>
  );
}
