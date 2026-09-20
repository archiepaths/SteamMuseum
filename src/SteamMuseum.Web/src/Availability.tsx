import { useEffect, useState } from "react";
import { CalendarDays, Check, Clock3 } from "lucide-react";
import { request } from "./api";
import { datesBetween, prettyDate, roleName } from "./dates";
import { roles, type Availability, type Day, type Window } from "./types";
import {
  ActionForm,
  Badge,
  Empty,
  Field,
  Status,
  useResource,
  value,
} from "./ui";

export default function AvailabilityPage({ planner }: { planner: boolean }) {
  const windows = useResource<Window[]>("/windows");
  const [id, setId] = useState("");
  const selected = id || windows.data?.[0]?.id || "";
  const resource = useResource<Availability>(
    selected ? `/me/availability/${selected}` : null,
  );
  return (
    <>
      <p className="intro">
        Let the team know when you can lend a hand. Every day makes a
        difference.
      </p>
      <div className="toolbar">
        <Field label="Availability window">
          <select value={selected} onChange={(e) => setId(e.target.value)}>
            {windows.data?.map((w) => (
              <option key={w.id} value={w.id}>
                {w.name}
                {w.isOpen ? "" : " · Closed"}
              </option>
            ))}
          </select>
        </Field>
      </div>
      <Status resource={windows} />
      <Status resource={resource} />
      {windows.data?.length === 0 && (
        <Empty>
          No availability windows yet. A planner can open one for the team.
        </Empty>
      )}
      {resource.data && (
        <AvailabilityEditor
          key={selected}
          data={resource.data}
          refresh={resource.reload}
        />
      )}
      {planner && (
        <details className="card">
          <summary>Manage availability windows</summary>
          <div className="split">
            <section>
              <h2>Open a new window</h2>
              <ActionForm
                submit="Create window"
                onSubmit={async (f) => {
                  await request("/windows", "POST", {
                    name: value(f, "name"),
                    kind: value(f, "kind"),
                    start: value(f, "start"),
                    end: value(f, "end"),
                    submissionDeadlineUtc: new Date(
                      value(f, "deadline"),
                    ).toISOString(),
                  });
                  windows.reload();
                }}
              >
                <Field label="Window name">
                  <input name="name" required maxLength={150} />
                </Field>
                <Field label="Type">
                  <select name="kind">
                    <option value="Monthly">Calendar month</option>
                    <option value="SpecialEvent">Special event</option>
                  </select>
                </Field>
                <div className="fields">
                  <Field label="First date">
                    <input name="start" type="date" required />
                  </Field>
                  <Field label="Last date">
                    <input name="end" type="date" required />
                  </Field>
                </div>
                <Field label="Submission deadline (your local time)">
                  <input name="deadline" type="datetime-local" required />
                </Field>
              </ActionForm>
            </section>
            {resource.data && (
              <section>
                <h2>Update {resource.data.window.name}</h2>
                <ActionForm
                  key={selected}
                  onSubmit={async (f) => {
                    await request(`/windows/${selected}/state`, "PUT", {
                      open: value(f, "open") === "true",
                      deadlineUtc: new Date(value(f, "deadline")).toISOString(),
                    });
                    windows.reload();
                    resource.reload();
                  }}
                >
                  <Field label="State">
                    <select
                      name="open"
                      defaultValue={String(resource.data.window.isOpen)}
                    >
                      <option value="true">Open</option>
                      <option value="false">Closed</option>
                    </select>
                  </Field>
                  <Field label="Submission deadline (your local time)">
                    <input
                      name="deadline"
                      type="datetime-local"
                      required
                      defaultValue={toLocalInput(
                        resource.data.window.submissionDeadlineUtc,
                      )}
                    />
                  </Field>
                </ActionForm>
              </section>
            )}
          </div>
        </details>
      )}
    </>
  );
}
function toLocalInput(iso: string) {
  const d = new Date(iso);
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000)
    .toISOString()
    .slice(0, 16);
}
export function AvailabilityEditor({
  data,
  refresh,
}: {
  data: Availability;
  refresh: () => void;
}) {
  const [days, setDays] = useState<Record<string, Day>>({});
  const [maximum, setMaximum] = useState("");
  const [selected, setSelected] = useState(data.window.start);
  const [weekdays, setWeekdays] = useState<number[]>([]);
  useEffect(() => {
    setDays(Object.fromEntries(data.days.map((d) => [d.date, d])));
    setMaximum(
      data.maximumAssignments === null ? "" : String(data.maximumAssignments),
    );
  }, [data]);
  const dates = datesBetween(data.window.start, data.window.end);
  const closed =
    !data.window.isOpen ||
    new Date(data.window.submissionDeadlineUtc).getTime() < Date.now();
  const day = days[selected];
  function update(date: string, patch: Partial<Day>) {
    setDays((prev) => ({
      ...prev,
      [date]: {
        ...(prev[date] ?? {
          date,
          status: "Available",
          from: null,
          until: null,
          preferredRole: null,
          note: null,
        }),
        ...patch,
      },
    }));
  }
  const available = Object.values(days).filter(
    (d) => d.status === "Available",
  ).length;
  return (
    <>
      <div className="stats">
        <div>
          <CalendarDays />
          <span>
            <strong>{available}</strong>Available days
          </span>
        </div>
        <div>
          <Check />
          <span>
            <strong>{data.assigned}</strong>Assigned shifts
          </span>
        </div>
        <div>
          <Clock3 />
          <span>
            <strong>
              {new Date(data.window.submissionDeadlineUtc).toLocaleDateString(
                "en-GB",
                { day: "numeric", month: "short" },
              )}
            </strong>
            Response deadline
          </span>
        </div>
      </div>
      <ActionForm
        submitDisabled={closed}
        submit="Save my availability"
        onSubmit={async () => {
          const entries = Object.values(days);
          for (const d of entries) {
            if (
              !!d.from !== !!d.until ||
              (d.from && d.until && d.from >= d.until)
            )
              throw new Error(
                `Choose a valid start and end time for ${prettyDate(d.date)}.`,
              );
          }
          await request(`/me/availability/${data.window.id}`, "PUT", {
            maximumAssignments: maximum === "" ? null : Number(maximum),
            days: entries,
          });
          refresh();
        }}
      >
        <section className="card">
          <div className="section-heading">
            <div>
              <h2>{data.window.name}</h2>
              <p>
                {prettyDate(data.window.start)} – {prettyDate(data.window.end)}
              </p>
            </div>
            <Badge tone={closed ? "muted" : "green"}>
              {closed ? "Responses closed" : "Open for responses"}
            </Badge>
          </div>
          {closed && (
            <p className="notice">
              This window is closed or its deadline has passed. Your saved
              responses are shown below.
            </p>
          )}
          <div className="availability-layout">
            <div>
              <fieldset className="weekday-tools" disabled={closed}>
                <b>Usually free on the same days?</b>
                <p>Select weekdays, then apply across this window.</p>
                <div className="weekday-buttons">
                  {["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"].map(
                    (name, i) => (
                      <label key={name}>
                        <input
                          type="checkbox"
                          checked={weekdays.includes((i + 1) % 7)}
                          onChange={(e) =>
                            setWeekdays((prev) =>
                              e.target.checked
                                ? [...prev, (i + 1) % 7]
                                : prev.filter((x) => x !== (i + 1) % 7),
                            )
                          }
                        />
                        {name}
                      </label>
                    ),
                  )}
                </div>
                <button
                  type="button"
                  disabled={!weekdays.length}
                  onClick={() =>
                    dates
                      .filter((d) =>
                        weekdays.includes(new Date(`${d}T12:00:00`).getDay()),
                      )
                      .forEach((d) => update(d, { status: "Available" }))
                  }
                >
                  Mark matching days available
                </button>
              </fieldset>
              <div
                className="calendar"
                role="group"
                aria-label="Availability calendar"
              >
                {["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"].map((x) => (
                  <div className="day-label" key={x}>
                    {x}
                  </div>
                ))}
                {Array.from(
                  {
                    length: (new Date(`${dates[0]}T12:00:00`).getDay() + 6) % 7,
                  },
                  (_, i) => (
                    <span key={`pad${i}`} />
                  ),
                )}
                {dates.map((date) => (
                  <button
                    type="button"
                    key={date}
                    aria-pressed={selected === date}
                    aria-label={`${prettyDate(date)}, ${days[date]?.status ?? "Not responded"}`}
                    className={`calendar-day ${days[date]?.status.toLowerCase() ?? ""} ${selected === date ? "focused" : ""}`}
                    onClick={() => setSelected(date)}
                  >
                    <strong>{Number(date.slice(-2))}</strong>
                    <small>
                      {new Date(`${date}T12:00:00`).toLocaleDateString(
                        "en-GB",
                        { month: "short" },
                      )}
                    </small>
                    <span>
                      {days[date]?.status === "Available"
                        ? "Available"
                        : days[date]
                          ? "Unavailable"
                          : "No response"}
                    </span>
                  </button>
                ))}
              </div>
              <div className="legend">
                <span>● Available</span>
                <span>○ No response</span>
                <span>— Unavailable</span>
              </div>
            </div>
            <fieldset className="day-editor" disabled={closed}>
              <p className="eyebrow">SELECTED DAY</p>
              <h3>{prettyDate(selected)}</h3>
              <Field label="Your response">
                <select
                  value={day?.status ?? ""}
                  onChange={(e) =>
                    update(
                      selected,
                      e.target.value === "Unavailable"
                        ? {
                            status: "Unavailable",
                            from: null,
                            until: null,
                            preferredRole: null,
                          }
                        : { status: "Available" },
                    )
                  }
                >
                  {!day && (
                    <option value="" disabled>
                      Not responded
                    </option>
                  )}
                  <option>Available</option>
                  <option>Unavailable</option>
                </select>
              </Field>
              {day?.status === "Available" && (
                <>
                  <p className="muted">
                    Leave both times empty for all-day availability.
                  </p>
                  <div className="fields">
                    <Field label="From">
                      <input
                        type="time"
                        value={day.from?.slice(0, 5) ?? ""}
                        onChange={(e) =>
                          update(selected, {
                            from: e.target.value
                              ? `${e.target.value}:00`
                              : null,
                          })
                        }
                      />
                    </Field>
                    <Field label="Until">
                      <input
                        type="time"
                        value={day.until?.slice(0, 5) ?? ""}
                        onChange={(e) =>
                          update(selected, {
                            until: e.target.value
                              ? `${e.target.value}:00`
                              : null,
                          })
                        }
                      />
                    </Field>
                  </div>
                  <Field label="Preferred role">
                    <select
                      value={day.preferredRole ?? ""}
                      onChange={(e) =>
                        update(selected, {
                          preferredRole:
                            (e.target.value as Day["preferredRole"]) || null,
                        })
                      }
                    >
                      <option value="">Any qualified role</option>
                      {roles.map((r) => (
                        <option key={r} value={r}>
                          {roleName(r)}
                        </option>
                      ))}
                    </select>
                  </Field>
                </>
              )}
              {day && (
                <Field label="Note for the planner">
                  <textarea
                    maxLength={500}
                    value={day.note ?? ""}
                    onChange={(e) => update(selected, { note: e.target.value })}
                  />
                </Field>
              )}
              <hr />
              <Field label="Maximum shifts in this window">
                <input
                  type="number"
                  min={data.assigned}
                  step="1"
                  value={maximum}
                  onChange={(e) => setMaximum(e.target.value)}
                  placeholder="No limit"
                />
              </Field>
              <p className="muted">
                Blank means no limit. Zero means no assignments. Each role slot
                counts as one shift.
              </p>
            </fieldset>
          </div>
          <p className="notice">
            Unanswered dates are not available. Responses are shared with
            overlapping windows; each window keeps its own shift limit.
          </p>
        </section>
      </ActionForm>
    </>
  );
}
