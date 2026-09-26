import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import AvailabilityCalendar, {
  AvailabilityView,
  windowRanges,
} from "./AvailabilityCalendar";
import { useEffect, useState } from "react";
import { DayPicker } from "@daypicker/react";
import "@daypicker/react/style.css";
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

export default function AvailabilityPage({
  planner,
  editing = false,
  managing = false,
}: {
  planner: boolean;
  editing?: boolean;
  managing?: boolean;
}) {
  const { windowId } = useParams();
  const navigate = useNavigate();
  const [search] = useSearchParams();
  const windows = useResource<Window[]>("/windows");
  const selected =
    windowId ||
    (managing ? search.get("window") : null) ||
    windows.data?.[0]?.id ||
    "";
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
          <select
            value={selected}
            onChange={(e) =>
              navigate(
                managing
                  ? `/availability/manage?window=${e.target.value}`
                  : `/availability/${e.target.value}${editing ? "/edit" : ""}`,
              )
            }
          >
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
      {planner && !managing && (
        <p>
          <Link to="/availability/manage">Manage availability windows</Link>
        </p>
      )}
      {managing && (
        <p>
          <Link to="/availability">Back to availability</Link>
        </p>
      )}
      {selected && !managing && (
        <p>
          <Link to={`/availability/${selected}${editing ? "" : "/edit"}`}>
            {editing ? "Back to availability" : "Edit availability"}
          </Link>
        </p>
      )}
      {resource.data &&
        !managing &&
        (editing ? (
          <AvailabilityEditor
            key={selected}
            data={resource.data}
            refresh={resource.reload}
          />
        ) : (
          <AvailabilityView data={resource.data} />
        ))}
      {planner && managing && (
        <details className="card" open>
          <summary>Manage availability windows</summary>
          <div className="split">
            <section>
              <h2>Open a new window</h2>
              <CreateWindowForm refresh={windows.reload} />
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
                <ActionForm
                  key={`${selected}-notes`}
                  submit="Save window notes"
                  onSubmit={async (f) => {
                    await request(`/windows/${selected}/notes`, "PUT", {
                      notes: value(f, "notes") || null,
                    });
                    windows.reload();
                    resource.reload();
                  }}
                >
                  <Field label="Window notes">
                    <textarea
                      name="notes"
                      maxLength={2000}
                      rows={5}
                      defaultValue={resource.data.window.notes ?? ""}
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
export function CreateWindowForm({ refresh }: { refresh: () => void }) {
  const [month, setMonth] = useState("");
  const [kind, setKind] = useState<Window["kind"]>("Monthly");
  const [ranges, setRanges] = useState([{ start: "", end: "" }]);
  function editRange(index: number, key: "start" | "end", value: string) {
    setRanges((prev) =>
      prev.map((r, i) => (i === index ? { ...r, [key]: value } : r)),
    );
  }
  return (
    <ActionForm
      submit="Create window"
      onSubmit={async (f) => {
        const dates =
          kind === "SpecialEvent"
            ? ranges
            : [
                {
                  start: month ? `${month}-01` : "",
                  end: month
                    ? `${month}-${new Date(Number(month.slice(0, 4)), Number(month.slice(5, 7)), 0).getDate()}`
                    : "",
                },
              ];
        const ordered = [...dates].sort((a, b) =>
          a.start.localeCompare(b.start),
        );
        if (ordered.some((r) => !r.start || !r.end || r.start > r.end))
          throw new Error("Choose valid start and end dates for every range.");
        if (ordered.some((r, i) => i > 0 && r.start <= ordered[i - 1].end))
          throw new Error("Date ranges must not overlap.");
        await request("/windows", "POST", {
          name: value(f, "name"),
          kind,
          start: ordered[0].start,
          end: ordered[ordered.length - 1].end,
          ...(kind === "SpecialEvent" ? { dateRanges: ordered } : {}),
          notes: value(f, "notes") || null,
          submissionDeadlineUtc: new Date(value(f, "deadline")).toISOString(),
        });
        refresh();
      }}
    >
      <Field label="Window name">
        <input name="name" required maxLength={150} />
      </Field>
      <Field label="Type">
        <select
          value={kind}
          onChange={(e) => setKind(e.target.value as Window["kind"])}
        >
          <option value="Monthly">Calendar month</option>
          <option value="SpecialEvent">Special event</option>
        </select>
      </Field>
      {kind === "Monthly" && (
        <Field label="Calendar month">
          <input
            type="month"
            required
            value={month}
            onChange={(e) => setMonth(e.target.value)}
          />
        </Field>
      )}
      {(kind === "SpecialEvent" ? ranges : []).map((range, i) => (
        <fieldset key={i} className="window-range">
          {kind === "SpecialEvent" && <legend>Date range {i + 1}</legend>}
          <details className="range-picker">
            <summary>Choose dates on calendar</summary>
            <p>
              Choose the first and last day. For one day, choose the same date
              twice.
            </p>
            <WindowRangePicker
              range={range}
              onChange={(next) =>
                setRanges((prev) =>
                  prev.map((r, index) => (index === i ? next : r)),
                )
              }
            />
          </details>
          <div className="fields">
            <Field
              label={
                kind === "SpecialEvent" ? `First date ${i + 1}` : "First date"
              }
            >
              <input
                type="date"
                required
                value={range.start}
                onChange={(e) => editRange(i, "start", e.target.value)}
              />
            </Field>
            <Field
              label={
                kind === "SpecialEvent" ? `Last date ${i + 1}` : "Last date"
              }
            >
              <input
                type="date"
                required
                min={range.start}
                value={range.end}
                onChange={(e) => editRange(i, "end", e.target.value)}
              />
            </Field>
          </div>
          {kind === "SpecialEvent" && ranges.length > 1 && (
            <button
              type="button"
              onClick={() =>
                setRanges((prev) => prev.filter((_, index) => index !== i))
              }
            >
              Remove date range {i + 1}
            </button>
          )}
        </fieldset>
      ))}
      {kind === "SpecialEvent" && (
        <button
          type="button"
          disabled={ranges.length >= 367}
          onClick={() => setRanges((prev) => [...prev, { start: "", end: "" }])}
        >
          Add date range
        </button>
      )}
      <Field label="Window notes">
        <textarea name="notes" maxLength={2000} rows={5} />
      </Field>
      <Field label="Submission deadline (your local time)">
        <input name="deadline" type="datetime-local" required />
      </Field>
    </ActionForm>
  );
}
function WindowRangePicker({
  range,
  onChange,
}: {
  range: { start: string; end: string };
  onChange: (range: { start: string; end: string }) => void;
}) {
  const [month, setMonth] = useState(() =>
    range.start ? new Date(`${range.start}T12:00:00`) : new Date(),
  );
  useEffect(() => {
    if (range.start) setMonth(new Date(`${range.start}T12:00:00`));
  }, [range.start]);
  return (
    <DayPicker
      mode="range"
      resetOnSelect
      weekStartsOn={1}
      month={month}
      onMonthChange={setMonth}
      selected={
        range.start
          ? {
              from: new Date(`${range.start}T12:00:00`),
              to: range.end ? new Date(`${range.end}T12:00:00`) : undefined,
            }
          : undefined
      }
      onSelect={(selection) =>
        onChange({
          start: localDate(selection?.from),
          end: localDate(selection?.to),
        })
      }
    />
  );
}
function localDate(date: Date | undefined) {
  if (!date) return "";
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
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
  const [selectedDates, setSelectedDates] = useState<string[]>([]);
  useEffect(() => {
    setDays(Object.fromEntries(data.days.map((d) => [d.date, d])));
    setMaximum(
      data.maximumAssignments === null ? "" : String(data.maximumAssignments),
    );
  }, [data]);
  const ranges = windowRanges(data.window);
  const dates = ranges.flatMap((range) => datesBetween(range.start, range.end));
  const closed =
    !data.window.isOpen ||
    new Date(data.window.submissionDeadlineUtc).getTime() < Date.now();
  const selected = selectedDates[0] ?? data.window.start;
  const editingGroup = selectedDates.length > 1;
  const targets = selectedDates;
  function common<K extends keyof Day>(key: K): Day[K] | undefined {
    const first = days[targets[0]]?.[key];
    return targets.every((date) => days[date]?.[key] === first)
      ? first
      : undefined;
  }
  const day = editingGroup
    ? {
        date: selected,
        status: common("status"),
        from: common("from"),
        until: common("until"),
        preferredRole: common("preferredRole"),
        note: common("note"),
      }
    : selectedDates.length
      ? days[selected]
      : undefined;
  function update(date: string, patch: Partial<Day>) {
    setDays((prev) => {
      const next = { ...prev };
      for (const target of editingGroup ? selectedDates : [date]) {
        next[target] = {
          ...(prev[target] ?? {
            date: target,
            status: "Available",
            from: null,
            until: null,
            preferredRole: null,
            note: null,
          }),
          ...patch,
        };
      }
      return next;
    });
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
                {ranges
                  .map(
                    (range) =>
                      `${prettyDate(range.start)} – ${prettyDate(range.end)}`,
                  )
                  .join("; ")}
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
          {data.window.notes && (
            <div className="notice window-notes">{data.window.notes}</div>
          )}
          <div className="availability-layout">
            <div>
              <fieldset className="selection-tools" disabled={closed}>
                <p>
                  Tap dates to select them for editing. Tap a selected date
                  again to deselect it.
                </p>
                <div className="selection-actions">
                  <button type="button" onClick={() => setSelectedDates(dates)}>
                    Select all dates
                  </button>
                  <button
                    type="button"
                    disabled={!selectedDates.length}
                    onClick={() => setSelectedDates([])}
                  >
                    Clear selection
                  </button>
                  <span role="status">
                    {selectedDates.length} dates selected
                  </span>
                </div>
              </fieldset>
              <AvailabilityCalendar
                data={data}
                days={days}
                selectedDates={selectedDates}
                onSelect={(date) =>
                  setSelectedDates((prev) =>
                    prev.includes(date)
                      ? prev.filter((d) => d !== date)
                      : [...prev, date],
                  )
                }
              />
              <div className="legend">
                <span>● Available</span>
                <span>○ No response</span>
                <span>— Unavailable</span>
              </div>
            </div>
            <fieldset className="day-editor" disabled={closed}>
              <fieldset disabled={!selectedDates.length}>
                <p className="eyebrow">
                  {editingGroup ? "SELECTED DATES" : "SELECTED DAY"}
                </p>
                <h3>
                  {editingGroup
                    ? `${selectedDates.length} dates selected`
                    : selectedDates.length
                      ? prettyDate(selected)
                      : "Select dates to edit"}
                </h3>
                {editingGroup && (
                  <p className="notice">
                    Changes apply to every selected date. Different values are
                    shown as mixed or blank; only fields you change are
                    replaced. Choose Available to edit times and roles for a
                    mixed group. Save below when finished.
                  </p>
                )}
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
                    {!day?.status && (
                      <option value="" disabled>
                        {editingGroup
                          ? "Mixed / not responded"
                          : "Not responded"}
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
                    {editingGroup && (
                      <button
                        type="button"
                        onClick={() =>
                          update(selected, { from: null, until: null })
                        }
                      >
                        Set selected dates to all day
                      </button>
                    )}
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
                              e.target.value === "any"
                                ? null
                                : (e.target.value as Day["preferredRole"]) ||
                                  null,
                          })
                        }
                      >
                        <option value="">
                          {editingGroup && common("preferredRole") === undefined
                            ? "Mixed roles (unchanged)"
                            : "Any qualified role"}
                        </option>
                        {editingGroup &&
                          common("preferredRole") === undefined && (
                            <option value="any">Any qualified role</option>
                          )}
                        {roles.map((r) => (
                          <option key={r} value={r}>
                            {roleName(r)}
                          </option>
                        ))}
                      </select>
                    </Field>
                  </>
                )}
                {day?.status && (
                  <Field label="Note for the planner">
                    <textarea
                      maxLength={500}
                      value={day.note ?? ""}
                      onChange={(e) =>
                        update(selected, { note: e.target.value })
                      }
                    />
                  </Field>
                )}
                {editingGroup && day?.status && (
                  <button
                    type="button"
                    onClick={() => update(selected, { note: null })}
                  >
                    Clear notes on selected dates
                  </button>
                )}
              </fieldset>
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
