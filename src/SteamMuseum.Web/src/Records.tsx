import { useState } from "react";
import { request } from "./api";
import { prettyDate, roleName } from "./dates";
import ElementRecords from "./ElementRecords";
import type { Availability, Member, Training, Window } from "./types";
import { ActionForm, Empty, Field, Status, useResource, value } from "./ui";

export default function RecordsPage({
  staff,
  assessor,
  planner,
}: {
  staff: boolean;
  assessor: boolean;
  planner: boolean;
}) {
  const members = useResource<Member[]>(staff ? "/members" : null);
  const [memberId, setMemberId] = useState("");
  const selected = memberId || members.data?.[0]?.id || "";
  const prefix = staff ? (selected ? `/members/${selected}` : null) : "/me";
  const training = useResource<Training[]>(
    prefix ? `${prefix}/training` : null,
  );
  return (
    <>
      <p className="intro">
        Qualifications, assessment evidence and training history, all in one
        place.
      </p>
      {staff && (
        <>
          <Status resource={members} />
          <Field label="Staff member">
            <select
              value={selected}
              onChange={(e) => setMemberId(e.target.value)}
            >
              {members.data?.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.displayName}
                  {m.active ? "" : " (inactive)"}
                </option>
              ))}
            </select>
          </Field>
          {members.data?.length === 0 && <Empty>No staff records yet.</Empty>}
        </>
      )}
      {[training].map((r, i) => (
        <Status key={i} resource={r} />
      ))}
      <ElementRecords
        key={prefix}
        prefix={prefix}
        memberId={selected}
        manage={staff && assessor}
      />
      {staff && planner && selected && (
        <MemberAvailability key={selected} memberId={selected} />
      )}
      <h2>Training history</h2>
      {training.data?.length === 0 && <Empty>No training recorded yet.</Empty>}
      {training.data?.map((t) => (
        <article className="card" key={t.id}>
          <h3>{prettyDate(t.date)}</h3>
          <p className="preserve">{t.notes}</p>
        </article>
      ))}
      {staff && assessor && selected && (
        <details className="card">
          <summary>Add training record</summary>
          <ActionForm
            key={selected}
            submit="Record training"
            onSubmit={async (f) => {
              await request("/training", "POST", {
                memberId: selected,
                date: value(f, "date"),
                notes: value(f, "notes"),
              });
              training.reload();
            }}
          >
            <Field label="Training date">
              <input name="date" type="date" required />
            </Field>
            <Field label="Training notes">
              <textarea name="notes" required maxLength={2000} />
            </Field>
          </ActionForm>
        </details>
      )}
    </>
  );
}

export function MemberAvailability({ memberId }: { memberId: string }) {
  const windows = useResource<Window[]>("/windows");
  const [windowId, setWindowId] = useState("");
  const selected = windowId || windows.data?.[0]?.id;
  const availability = useResource<Availability>(
    selected ? `/members/${memberId}/availability/${selected}` : null,
  );
  return (
    <section className="card">
      <h2>Member availability</h2>
      <Field label="Window">
        <select
          value={selected ?? ""}
          onChange={(e) => setWindowId(e.target.value)}
        >
          {windows.data?.map((w) => (
            <option key={w.id} value={w.id}>
              {w.name}
            </option>
          ))}
        </select>
      </Field>
      <Status resource={windows} />
      <Status resource={availability} />
      {availability.data && (
        <>
          <p>
            {availability.data.assigned} assignments · Maximum:{" "}
            {availability.data.maximumAssignments ?? "No limit"}
          </p>
          {availability.data.days.length === 0 && (
            <Empty>No responses recorded.</Empty>
          )}
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Response</th>
                  <th>Times</th>
                  <th>Preference / note</th>
                </tr>
              </thead>
              <tbody>
                {availability.data.days.map((d) => (
                  <tr key={d.date}>
                    <td>{prettyDate(d.date)}</td>
                    <td>{d.status}</td>
                    <td>
                      {d.from
                        ? `${d.from.slice(0, 5)}–${d.until?.slice(0, 5)}`
                        : d.status === "Available"
                          ? "All day"
                          : "—"}
                    </td>
                    <td>
                      {d.preferredRole ? roleName(d.preferredRole) : "—"}{" "}
                      {d.note}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  );
}
