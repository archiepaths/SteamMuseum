// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { AvailabilityEditor, CreateWindowForm } from "./Availability";
import type { Availability } from "./types";
import { request } from "./api";
import { datesBetween } from "./dates";
vi.mock("./api", () => ({ request: vi.fn().mockResolvedValue({}) }));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});
const data: Availability = {
  window: {
    id: "window",
    name: "Operating week",
    kind: "SpecialEvent",
    start: "2099-01-05",
    end: "2099-01-11",
    isOpen: true,
    submissionDeadlineUtc: "2099-01-01T12:00:00Z",
  },
  maximumAssignments: 3,
  assigned: 1,
  days: [
    {
      date: "2099-01-05",
      status: "Available",
      from: "09:00:00",
      until: "12:00:00",
      preferredRole: "Guard",
      note: "Morning only",
    },
  ],
};
describe("availability workflow", () => {
  it("preserves limits and untouched responses while leaving missing dates unanswered", async () => {
    render(<AvailabilityEditor data={data} refresh={vi.fn()} />);
    fireEvent.click(
      screen.getByRole("button", { name: /6 Jan 2099, Not responded/ }),
    );
    fireEvent.change(screen.getByLabelText("Your response"), {
      target: { value: "Unavailable" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Save my availability" }),
    );
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith("/me/availability/window", "PUT", {
        maximumAssignments: 3,
        days: [
          data.days[0],
          {
            date: "2099-01-06",
            status: "Unavailable",
            from: null,
            until: null,
            preferredRole: null,
            note: null,
          },
        ],
      }),
    );
  });
  it("clears times and role for an unavailable response", async () => {
    render(<AvailabilityEditor data={data} refresh={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /5 Jan 2099,/ }));
    fireEvent.change(screen.getByLabelText("Your response"), {
      target: { value: "Unavailable" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Save my availability" }),
    );
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith(
        "/me/availability/window",
        "PUT",
        expect.objectContaining({
          days: [
            expect.objectContaining({
              status: "Unavailable",
              from: null,
              until: null,
              preferredRole: null,
            }),
          ],
        }),
      ),
    );
  });
  it("blocks incomplete time ranges before submitting", async () => {
    render(<AvailabilityEditor data={data} refresh={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /5 Jan 2099,/ }));
    fireEvent.change(screen.getByLabelText("Until"), { target: { value: "" } });
    fireEvent.click(
      screen.getByRole("button", { name: "Save my availability" }),
    );
    expect(await screen.findByRole("alert")).toHaveProperty(
      "textContent",
      "Choose a valid start and end time for 5 Jan 2099.",
    );
    expect(request).not.toHaveBeenCalled();
  });
  it("disables saving when the deadline has passed", () => {
    render(
      <AvailabilityEditor
        data={{
          ...data,
          window: {
            ...data.window,
            submissionDeadlineUtc: "2020-01-01T00:00:00Z",
          },
        }}
        refresh={vi.fn()}
      />,
    );
    expect(
      screen
        .getByRole("button", { name: "Save my availability" })
        .hasAttribute("disabled"),
    ).toBe(true);
  });
  it("edits selected dates as a group without replacing untouched fields or other dates", async () => {
    render(<AvailabilityEditor data={data} refresh={vi.fn()} />);
    expect(screen.queryByText("Usually free on the same days?")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /5 Jan 2099,/ }));
    fireEvent.click(screen.getByRole("button", { name: /7 Jan 2099,/ }));
    fireEvent.change(screen.getByLabelText("Your response"), {
      target: { value: "Available" },
    });
    fireEvent.change(screen.getByLabelText("Note for the planner"), {
      target: { value: "Can help" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Save my availability" }),
    );
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith("/me/availability/window", "PUT", {
        maximumAssignments: 3,
        days: [
          { ...data.days[0], note: "Can help" },
          {
            date: "2099-01-07",
            status: "Available",
            from: null,
            until: null,
            preferredRole: null,
            note: "Can help",
          },
        ],
      }),
    );
  });
  it("selects all, excludes deselected dates and clears incompatible fields for the group", async () => {
    render(<AvailabilityEditor data={data} refresh={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Select all dates" }));
    fireEvent.click(screen.getByRole("button", { name: /6 Jan 2099,/ }));
    fireEvent.change(screen.getByLabelText("Your response"), {
      target: { value: "Unavailable" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Save my availability" }),
    );
    await waitFor(() => expect(request).toHaveBeenCalled());
    const body = vi.mocked(request).mock.calls[0][2] as {
      days: Availability["days"];
    };
    expect(body.days).toHaveLength(6);
    expect(body.days.some((d) => d.date === "2099-01-06")).toBe(false);
    expect(
      body.days.every(
        (d) =>
          d.status === "Unavailable" &&
          d.from === null &&
          d.until === null &&
          d.preferredRole === null,
      ),
    ).toBe(true);
    expect(body.days[0].note).toBe("Morning only");
  });
  it("clears group selection without changing responses and leaves no tiles selected", async () => {
    render(<AvailabilityEditor data={data} refresh={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Select all dates" }));
    fireEvent.click(screen.getByRole("button", { name: "Clear selection" }));
    expect(
      screen.queryAllByRole("button", { pressed: true }).length === 0,
    ).toBe(true);
    fireEvent.click(
      screen.getByRole("button", { name: "Save my availability" }),
    );
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith("/me/availability/window", "PUT", {
        maximumAssignments: 3,
        days: data.days,
      }),
    );
  });
  it("disables group selection for closed windows", () => {
    render(
      <AvailabilityEditor
        data={{ ...data, window: { ...data.window, isOpen: false } }}
        refresh={vi.fn()}
      />,
    );
    expect(screen.queryAllByRole("checkbox").length === 0).toBe(true);
    expect(
      screen
        .getByRole("button", { name: "Select all dates" })
        .closest("fieldset")?.disabled,
    ).toBe(true);
  });
  it("shows window notes and only selects dates in the event ranges", async () => {
    render(
      <AvailabilityEditor
        data={{
          ...data,
          window: {
            ...data.window,
            kind: "SpecialEvent",
            notes: "Meet at the station.\nBring lunch.",
            dateRanges: [
              { start: "2099-01-05", end: "2099-01-06" },
              { start: "2099-01-10", end: "2099-01-11" },
            ],
          },
        }}
        refresh={vi.fn()}
      />,
    );
    expect(screen.getByText(/Meet at the station/).textContent).toBe(
      "Meet at the station.\nBring lunch.",
    );
    expect(screen.queryByRole("button", { name: /7 Jan 2099,/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Select all dates" }));
    expect(screen.getAllByRole("button", { pressed: true })).toHaveLength(4);
    fireEvent.change(screen.getByLabelText("Your response"), {
      target: { value: "Unavailable" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Save my availability" }),
    );
    await waitFor(() => expect(request).toHaveBeenCalled());
    const body = vi.mocked(request).mock.calls[0][2] as {
      days: Availability["days"];
    };
    expect(body.days.map((d) => d.date)).toEqual([
      "2099-01-05",
      "2099-01-06",
      "2099-01-10",
      "2099-01-11",
    ]);
  });
  it("creates a special event with multiple date ranges and notes", async () => {
    render(<CreateWindowForm refresh={vi.fn()} />);
    fireEvent.change(screen.getByLabelText("Window name"), {
      target: { value: "Gala" },
    });
    fireEvent.change(screen.getByLabelText("Type"), {
      target: { value: "SpecialEvent" },
    });
    fireEvent.change(screen.getByLabelText("First date 1"), {
      target: { value: "2099-01-05" },
    });
    fireEvent.change(screen.getByLabelText("Last date 1"), {
      target: { value: "2099-01-06" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add date range" }));
    fireEvent.change(screen.getByLabelText("First date 2"), {
      target: { value: "2099-01-10" },
    });
    fireEvent.change(screen.getByLabelText("Last date 2"), {
      target: { value: "2099-01-11" },
    });
    fireEvent.change(screen.getByLabelText("Window notes"), {
      target: { value: "Bring lunch" },
    });
    fireEvent.change(
      screen.getByLabelText("Submission deadline (your local time)"),
      { target: { value: "2099-01-01T12:00" } },
    );
    fireEvent.click(screen.getByRole("button", { name: "Create window" }));
    await waitFor(() =>
      expect(request).toHaveBeenCalledWith(
        "/windows",
        "POST",
        expect.objectContaining({
          name: "Gala",
          kind: "SpecialEvent",
          notes: "Bring lunch",
          start: "2099-01-05",
          end: "2099-01-11",
          dateRanges: [
            { start: "2099-01-05", end: "2099-01-06" },
            { start: "2099-01-10", end: "2099-01-11" },
          ],
        }),
      ),
    );
  });
  it("generates operating dates across leap days and daylight-saving changes", () => {
    expect(datesBetween("2028-02-28", "2028-03-01")).toEqual([
      "2028-02-28",
      "2028-02-29",
      "2028-03-01",
    ]);
    expect(datesBetween("2026-03-28", "2026-03-30")).toEqual([
      "2026-03-28",
      "2026-03-29",
      "2026-03-30",
    ]);
  });
});
