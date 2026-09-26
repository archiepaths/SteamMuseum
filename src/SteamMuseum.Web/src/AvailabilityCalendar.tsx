import { datesBetween, prettyDate } from "./dates";
import type { Availability, Day } from "./types";

export function windowRanges(window: Availability["window"]) {
  return window.dateRanges?.length
    ? [...window.dateRanges].sort((a, b) => a.start.localeCompare(b.start))
    : [{ start: window.start, end: window.end }];
}
export default function AvailabilityCalendar({
  data,
  days,
  selectedDates = [],
  onSelect,
}: {
  data: Availability;
  days?: Record<string, Day>;
  selectedDates?: string[];
  onSelect?: (date: string) => void;
}) {
  const responses =
    days ?? Object.fromEntries(data.days.map((d) => [d.date, d]));
  const ranges = windowRanges(data.window);
  return (
    <>
      {ranges.map((range) => (
        <section className="calendar-range" key={range.start}>
          {ranges.length > 1 && (
            <h3>
              {prettyDate(range.start)} – {prettyDate(range.end)}
            </h3>
          )}
          <div
            className="calendar"
            role="group"
            aria-label={`Availability calendar ${prettyDate(range.start)} to ${prettyDate(range.end)}`}
          >
            {["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"].map((d) => (
              <div className="day-label" key={d}>
                {d}
              </div>
            ))}
            {Array.from(
              {
                length: (new Date(`${range.start}T12:00:00`).getDay() + 6) % 7,
              },
              (_, i) => (
                <span key={`pad${i}`} />
              ),
            )}
            {datesBetween(range.start, range.end).map((date) => {
              const day = responses[date];
              const shifts =
                data.assignments?.filter((a) => a.date === date) ?? [];
              const label = `${prettyDate(date)}, ${day?.status ?? "Not responded"}${shifts.map((a) => `, ${a.roleName} ${a.start.slice(0, 5)}–${a.end.slice(0, 5)}${a.status === "Draft" ? " (draft)" : ""}`).join("")}`;
              const content = (
                <>
                  <strong>{Number(date.slice(-2))}</strong>
                  <small>
                    {new Date(`${date}T12:00:00`).toLocaleDateString("en-GB", {
                      month: "short",
                    })}
                  </small>
                  <span>{day?.status ?? "No response"}</span>
                  {shifts.map((a) => (
                    <span className="shift-label" key={a.dutyId}>
                      {a.roleName}
                      <br />
                      {a.start.slice(0, 5)}–{a.end.slice(0, 5)}
                      {a.status === "Draft" && " · Draft"}
                    </span>
                  ))}
                </>
              );
              const className = `calendar-day ${day?.status.toLowerCase() ?? ""} ${selectedDates.includes(date) ? "selected" : ""}`;
              return onSelect ? (
                <button
                  key={date}
                  type="button"
                  className={className}
                  aria-pressed={selectedDates.includes(date)}
                  aria-label={label}
                  onClick={() => onSelect(date)}
                >
                  {content}
                </button>
              ) : (
                <div key={date} className={className} aria-label={label}>
                  {content}
                </div>
              );
            })}
          </div>
        </section>
      ))}
    </>
  );
}
export function AvailabilityView({ data }: { data: Availability }) {
  return (
    <section className="card">
      <h2>{data.window.name}</h2>
      <p>
        {data.assigned} assigned shifts · Maximum:{" "}
        {data.maximumAssignments ?? "No limit"}
      </p>
      {data.window.notes && (
        <div className="notice window-notes">{data.window.notes}</div>
      )}
      <AvailabilityCalendar data={data} />
      <h3>Saved responses</h3>
      {data.days.length === 0 && <p>No responses recorded.</p>}
      <ul>
        {data.days.map((d) => (
          <li key={d.date}>
            {prettyDate(d.date)}: {d.status}
            {d.from && ` · ${d.from.slice(0, 5)}–${d.until?.slice(0, 5)}`}
            {d.preferredRole && ` · Prefers ${d.preferredRole}`}{" "}
            {d.note && <span className="preserve"> · {d.note}</span>}
          </li>
        ))}
      </ul>
    </section>
  );
}
