// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import RosterPage from "./Roster";
import { request } from "./api";
vi.mock("./api", () => ({ request: vi.fn() }));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});
const row = {
  duty: {
    id: "duty",
    name: "Passenger service",
    date: "2026-09-26",
    start: "09:00:00",
    end: "16:00:00",
    role: "Guard",
    railwayId: "railway",
    locomotiveId: null,
  },
  assignment: { id: "assignment", memberId: "member", status: "Draft" },
  issues: ["Member is not available for the full duty."],
  preferredRole: null,
};
function reads() {
  vi.mocked(request).mockImplementation(async (path, method) => {
    if (method === "POST")
      throw new Error("Member is not available for the full duty.");
    if (path.startsWith("/roster?")) return [row];
    if (path === "/members")
      return [{ id: "member", displayName: "Staff member", active: true }];
    return [];
  });
}
it("shows live conflicts and preserves the draft when publication is rejected", async () => {
  reads();
  render(<RosterPage planner />);
  await screen.findByRole("heading", { name: "Passenger service" });
  expect(screen.getByText("Needs attention")).toBeTruthy();
  fireEvent.change(screen.getByLabelText("Assignment action"), {
    target: { value: "publish" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Update assignment" }));
  await waitFor(() =>
    expect(request).toHaveBeenCalledWith(
      "/assignments/assignment/publish",
      "POST",
    ),
  );
  expect(await screen.findByRole("alert")).toHaveProperty(
    "textContent",
    "Member is not available for the full duty.",
  );
  expect(screen.getByText("Draft", { selector: ".badge" })).toBeTruthy();
});
it("rejects roster ranges longer than the API maximum without loading that range", async () => {
  reads();
  render(<RosterPage planner />);
  await screen.findByRole("heading", { name: "Passenger service" });
  fireEvent.change(screen.getByLabelText("From", { exact: true }), {
    target: { value: "2026-01-01" },
  });
  fireEvent.change(screen.getByLabelText("Until", { exact: true }), {
    target: { value: "2026-12-31" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Show dates" }));
  expect(await screen.findByRole("alert")).toHaveProperty(
    "textContent",
    "Choose an increasing date range of no more than 93 days.",
  );
  expect(
    vi
      .mocked(request)
      .mock.calls.some(([path]) => path.includes("until=2026-12-31")),
  ).toBe(false);
});
