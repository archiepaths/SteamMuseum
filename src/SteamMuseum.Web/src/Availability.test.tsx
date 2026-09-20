// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { AvailabilityEditor } from "./Availability";
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
