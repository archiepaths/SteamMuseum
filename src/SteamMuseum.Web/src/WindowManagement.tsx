import { Plus, LockKeyhole, Archive } from "lucide-react";
import { useState } from "react";
import { Link, useParams } from "react-router";
import { request } from "./api";
import { CreateWindowForm } from "./Availability";
import { windowRanges } from "./AvailabilityCalendar";
import { deadlineUtc, deadlineDate, datesBetween, prettyDate } from "./dates";
import type { Window, Day } from "./types";
import { ActionForm, Field, Status, Empty, useResource, value } from "./ui";
import SidePanel from "./SidePanel";

export default function WindowManagement() {
  const windows = useResource<Window[]>("/management/windows");
  const [selected, setSelected] = useState<string[]>([]);
  const [archived, setArchived] = useState(false);
  const [panel, setPanel] = useState<Window | "new" | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const rows = windows.data?.filter((w) => archived || !w.isArchived) ?? [];
  const ids = selected.filter((id) => rows.some((w) => w.id === id));
  function refresh() {
    windows.reload();
    setSelected([]);
  }
  async function bulk(action: string, targets = ids) {
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await request("/windows/bulk", "POST", { ids: targets, action });
      refresh();
      setPanel(null);
      setMessage(
        `${targets.length} window${targets.length === 1 ? "" : "s"} ${action === "close" ? "closed" : action === "archive" ? "archived" : "restored"}.`,
      );
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <div className="window-commandbar">
        <button
          aria-label="Add a new window"
          title="Add a new window"
          className="primary"
          disabled={busy}
          onClick={() => setPanel("new")}
        >
          <Plus size={18} aria-hidden="true" />
        </button>
        <button
          disabled={busy || !ids.length}
          aria-label="Close the window"
          title="Close selected windows"
          onClick={() => void bulk("close")}
        >
          <LockKeyhole size={18} aria-hidden="true" />
        </button>
        <button
          disabled={busy || !ids.length}
          aria-label="Archive the window"
          title="Archive selected windows"
          onClick={() => void bulk("archive")}
        >
          <Archive size={18} aria-hidden="true" />
        </button>
        <span>{ids.length} selected</span>
        <label>
          <input
            type="checkbox"
            checked={archived}
            onChange={(e) => {
              setArchived(e.target.checked);
              setSelected([]);
            }}
          />{" "}
          Show archived
        </label>
      </div>
      <p className="muted">
        Select a window name to edit. Archiving closes and hides a window; its
        responses are retained.
      </p>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {message && <p role="status">{message}</p>}
      <Status resource={windows} />
      {!windows.loading && !rows.length && (
        <Empty>No availability windows to display.</Empty>
      )}
      <div className="table-scroll">
        <table className="window-table">
          <thead>
            <tr>
              <th>
                <input
                  aria-label="Select all windows"
                  type="checkbox"
                  disabled={busy || !rows.length}
                  checked={!!rows.length && ids.length === rows.length}
                  onChange={(e) =>
                    setSelected(e.target.checked ? rows.map((w) => w.id) : [])
                  }
                />
              </th>
              <th>Window</th>
              <th>Dates</th>
              <th>Type</th>
              <th>Submission deadline</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((w) => (
              <tr
                key={w.id}
                className={`${!w.isOpen || w.isArchived || new Date(w.submissionDeadlineUtc) <= new Date() ? "row-closed" : ""} ${ids.includes(w.id) ? "row-selected" : ""}`}
                onClick={() => !busy && setPanel(w)}
              >
                <td onClick={(e) => e.stopPropagation()}>
                  <input
                    type="checkbox"
                    aria-label={`Select ${w.name}`}
                    disabled={busy}
                    checked={ids.includes(w.id)}
                    onChange={(e) =>
                      setSelected((previous) =>
                        e.target.checked
                          ? [...previous, w.id]
                          : previous.filter((id) => id !== w.id),
                      )
                    }
                  />
                </td>
                <td>
                  <button
                    className="text-button"
                    disabled={busy}
                    onClick={(e) => {
                      e.stopPropagation();
                      setPanel(w);
                    }}
                  >
                    {w.name}
                  </button>
                </td>
                <td>
                  {prettyDate(w.start)} – {prettyDate(w.end)}
                </td>
                <td>{w.kind === "Monthly" ? "Monthly" : "Special event"}</td>
                <td>{prettyDate(deadlineDate(w.submissionDeadlineUtc))}</td>
                <td onClick={(e) => e.stopPropagation()}>
                  <Link to={`/manage/windows/${w.id}`}>View window</Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {panel && (
        <SidePanel
          title={panel === "new" ? "Add a new window" : panel.name}
          onClose={() => {
            if (!busy) setPanel(null);
          }}
        >
          {panel === "new" ? (
            <CreateWindowForm
              refresh={() => {
                refresh();
                setPanel(null);
              }}
            />
          ) : (
            <>
              <p>
                {windowRanges(panel)
                  .map((r) => `${prettyDate(r.start)} – ${prettyDate(r.end)}`)
                  .join(", ")}
              </p>
              <p className="muted">
                Dates are fixed after creation so existing responses and shift
                limits keep their meaning.
              </p>
              {panel.isArchived ? (
                <>
                  <p>
                    This window is archived. Restore it to edit; it will remain
                    closed.
                  </p>
                  <button
                    disabled={busy}
                    onClick={() => void bulk("restore", [panel.id])}
                  >
                    Restore window
                  </button>
                  {error && <p role="alert">{error}</p>}
                </>
              ) : (
                <ActionForm
                  submit="Save changes"
                  onSubmit={async (f) => {
                    await request(`/windows/${panel.id}`, "PUT", {
                      name: value(f, "name"),
                      open: value(f, "open") === "true",
                      deadlineUtc: deadlineUtc(value(f, "deadline")),
                      notes: value(f, "notes") || null,
                    });
                    refresh();
                    setPanel(null);
                    setMessage("Window updated.");
                  }}
                >
                  <Field label="Window name">
                    <input
                      name="name"
                      defaultValue={panel.name}
                      required
                      maxLength={150}
                    />
                  </Field>
                  <Field label="State">
                    <select name="open" defaultValue={String(panel.isOpen)}>
                      <option value="true">Open</option>
                      <option value="false">Closed</option>
                    </select>
                  </Field>
                  <Field label="Submission deadline (end of local day)">
                    <input
                      name="deadline"
                      type="date"
                      defaultValue={deadlineDate(panel.submissionDeadlineUtc)}
                      required
                    />
                  </Field>
                  <Field label="Window notes">
                    <textarea
                      name="notes"
                      defaultValue={panel.notes ?? ""}
                      maxLength={2000}
                      rows={6}
                    />
                  </Field>
                </ActionForm>
              )}
            </>
          )}
        </SidePanel>
      )}
    </>
  );
}
export interface WindowMatrix {
  window: Window;
  members: { memberId: string; displayName: string; days: Day[] }[];
}
export function WindowAvailabilityGrid() {
  const { windowId } = useParams();
  const resource = useResource<WindowMatrix>(
    windowId ? `/windows/${windowId}/availability` : null,
  );
  const [detail, setDetail] = useState<string | null>(null);
  const data = resource.data;
  const dates = data
    ? windowRanges(data.window).flatMap((r) => datesBetween(r.start, r.end))
    : [];
  return (
    <>
      <Link to="/manage/windows">← All availability windows</Link>
      <Status resource={resource} />
      {data && (
        <>
          <h2 className="matrix-title">{data.window.name}</h2>
          {data.window.notes && (
            <p className="window-notes">{data.window.notes}</p>
          )}
          <div className="matrix-legend" aria-label="Availability legend">
            <span>
              <i className="matrix-unset" /> No response
            </span>
            <span>
              <i className="matrix-available" /> ✓ Available
            </span>
            <span>
              <i className="matrix-unavailable" /> × Unavailable
            </span>
            <span>◷ Partial day — hover, focus or select for hours</span>
          </div>
          {!data.members.length ? (
            <Empty>No staff have entered availability for these dates.</Empty>
          ) : (
            <div
              className="table-scroll availability-matrix"
              tabIndex={0}
              aria-label="Staff availability grid"
            >
              <table>
                <caption>
                  Staff with responses in this window · {data.members.length}{" "}
                  people
                </caption>
                <thead>
                  <tr>
                    <th scope="col">Staff member</th>
                    {dates.map((date) => (
                      <th
                        scope="col"
                        key={date}
                        title={prettyDate(date)}
                        aria-label={prettyDate(date)}
                      >
                        {Number(date.slice(-2))}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {data.members.map((member) => {
                    const days = new Map(member.days.map((d) => [d.date, d]));
                    return (
                      <tr key={member.memberId}>
                        <th scope="row">{member.displayName}</th>
                        {dates.map((date) => {
                          const day = days.get(date);
                          const available = day?.status === "Available";
                          const partial =
                            available && !!(day.from || day.until);
                          const label = `${member.displayName}, ${prettyDate(date)}: ${day ? day.status : "No response"}${partial ? `, ${day?.from?.slice(0, 5) ?? "Start of day"}–${day?.until?.slice(0, 5) ?? "End of day"}` : ""}${day?.note ? ` · ${day.note}` : ""}`;
                          return (
                            <td
                              key={date}
                              className={
                                day
                                  ? available
                                    ? "matrix-available"
                                    : "matrix-unavailable"
                                  : "matrix-unset"
                              }
                            >
                              <button
                                title={label}
                                aria-label={label}
                                onClick={() => setDetail(label)}
                              >
                                {day ? (
                                  partial ? (
                                    "◷"
                                  ) : available ? (
                                    "✓"
                                  ) : (
                                    "×"
                                  )
                                ) : (
                                  <span className="sr-only">No response</span>
                                )}
                              </button>
                            </td>
                          );
                        })}
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
          {detail && (
            <p role="status" className="matrix-detail">
              {detail} <button onClick={() => setDetail(null)}>Dismiss</button>
            </p>
          )}
        </>
      )}
    </>
  );
}
