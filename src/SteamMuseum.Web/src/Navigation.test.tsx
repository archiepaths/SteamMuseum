// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { StaffApp } from "./App";
import { request } from "./api";
vi.mock("./api", () => ({
  request: vi.fn(),
  ApiError: class extends Error {},
}));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});
const windowData = {
  id: "week",
  name: "Operating week",
  kind: "SpecialEvent",
  start: "2099-01-05",
  end: "2099-01-06",
  isOpen: true,
  submissionDeadlineUtc: "2099-01-01T12:00:00Z",
  notes: "Please arrive early",
};
function setup(path: string, roles = ["Member"]) {
  vi.mocked(request).mockImplementation(async (url) => {
    if (url === "/auth/me")
      return {
        id: "member",
        displayName: "Member",
        roles,
        mustChangePassword: false,
      };
    if (url === "/windows")
      return path === "/availability/manage" ? [] : [windowData];
    if (url === "/me/availability/week")
      return {
        window: windowData,
        days: [],
        assigned: 2,
        maximumAssignments: 3,
        assignments: [
          {
            dutyId: "first",
            name: "Morning",
            date: "2099-01-05",
            start: "09:00:00",
            end: "12:00:00",
            roleName: "Steam driver",
            status: "Published",
          },
          {
            dutyId: "second",
            name: "Afternoon",
            date: "2099-01-05",
            start: "13:00:00",
            end: "16:00:00",
            roleName: "Guard",
            status: "Published",
          },
        ],
      };
    if (url === "/competence-roles")
      return [
        {
          id: "base",
          name: "Steam driver",
          active: true,
          requirements: [{ elementId: "safety" }],
        },
        {
          id: "variant",
          name: "Driver with loco B",
          active: true,
          baseRoleId: "base",
          requirements: [{ elementId: "familiarisation" }],
        },
      ];
    if (url === "/competence-elements")
      return [
        {
          id: "safety",
          name: "Track safety",
          active: true,
          learningType: "Theory",
          reassessmentMonths: 12,
        },
        {
          id: "familiarisation",
          name: "Loco B familiarisation",
          active: true,
          learningType: "Practical",
          reassessmentMonths: 24,
        },
      ];
    return [];
  });
  render(
    <MemoryRouter initialEntries={[path]}>
      <StaffApp />
    </MemoryRouter>,
  );
}
it("opens a read-only availability deep link, shows both shifts and navigates to editing and back", async () => {
  setup("/availability/week");
  await screen.findByText(/Steam driver/, { selector: ".shift-label" });
  expect(screen.getByText(/Guard/, { selector: ".shift-label" })).toBeTruthy();
  expect(
    screen.getByText(/09:00–12:00/, { selector: ".shift-label" }),
  ).toBeTruthy();
  expect(screen.getByText("Please arrive early")).toBeTruthy();
  expect(
    screen.queryByRole("button", { name: "Save my availability" }),
  ).toBeNull();
  expect(
    within(
      screen.getByRole("group", { name: /Availability calendar/ }),
    ).queryAllByRole("button"),
  ).toHaveLength(0);
  fireEvent.click(screen.getByRole("link", { name: "Edit availability" }));
  await screen.findByRole("button", { name: "Save my availability" });
  expect(
    screen.getByRole("button", { name: /5 Jan 2099.*Steam driver.*Guard/ }),
  ).toBeTruthy();
  fireEvent.click(screen.getByRole("link", { name: "Back to availability" }));
  await screen.findByRole("link", { name: "Edit availability" });
  expect(
    screen.queryByRole("button", { name: "Save my availability" }),
  ).toBeNull();
});
it("opens role variants directly and drills back through the role list", async () => {
  setup("/roles/variant");
  await screen.findByRole("heading", { name: "Loco B familiarisation" });
  expect(screen.getByRole("heading", { name: "Track safety" })).toBeTruthy();
  expect(screen.getByText("Inherited")).toBeTruthy();
  expect(screen.queryByText("Edit role requirements")).toBeNull();
  fireEvent.click(screen.getByRole("link", { name: /All roles/ }));
  await screen.findByRole("link", { name: "Driver with loco B" });
  fireEvent.click(screen.getByRole("link", { name: "Steam driver" }));
  await screen.findByRole("heading", { name: "Track safety" });
  expect(
    screen.queryByRole("heading", { name: "Loco B familiarisation" }),
  ).toBeNull();
});
it("redirects members away from privileged direct URLs without fetching privileged data", async () => {
  setup("/admin");
  await screen.findByText("Operating week", { selector: "h2" });
  expect(
    vi.mocked(request).mock.calls.some(([path]) => path.startsWith("/admin")),
  ).toBe(false);
});
it("allows planners to create the first availability window from its own page", async () => {
  setup("/availability/manage", ["Planner"]);
  await screen.findByRole("heading", { name: "Open a new window" });
  expect(
    screen.queryByRole("button", { name: "Save my availability" }),
  ).toBeNull();
  await waitFor(() =>
    expect(
      screen.getByRole("link", { name: "Back to availability" }),
    ).toBeTruthy(),
  );
});
