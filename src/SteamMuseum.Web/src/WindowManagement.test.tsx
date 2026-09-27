// @vitest-environment jsdom
import { afterEach, beforeAll, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import WindowManagement, { WindowAvailabilityGrid } from "./WindowManagement";
import { request } from "./api";
vi.mock("./api", () => ({ request: vi.fn() }));
beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () {
    this.open = true;
  };
  HTMLDialogElement.prototype.close = function () {
    this.open = false;
  };
});
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});
const windowData = {
  id: "week",
  name: "Two weekends",
  kind: "SpecialEvent",
  start: "2099-10-01",
  end: "2099-10-05",
  dateRanges: [
    { start: "2099-10-01", end: "2099-10-02" },
    { start: "2099-10-05", end: "2099-10-05" },
  ],
  isOpen: true,
  submissionDeadlineUtc: "2099-09-30T12:00:00Z",
};
it("selects rows separately from editing and sends the selected bulk action", async () => {
  vi.mocked(request).mockResolvedValue([windowData]);
  render(
    <MemoryRouter>
      <WindowManagement />
    </MemoryRouter>,
  );
  fireEvent.click(
    await screen.findByRole("checkbox", { name: "Select Two weekends" }),
  );
  expect(screen.queryByRole("dialog")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Close the window" }));
  await waitFor(() =>
    expect(request).toHaveBeenCalledWith("/windows/bulk", "POST", {
      ids: ["week"],
      action: "close",
    }),
  );
  await screen.findByText("1 window closed.");
  fireEvent.click(
    await screen.findByRole("checkbox", { name: "Select Two weekends" }),
  );
  fireEvent.click(screen.getByRole("button", { name: "Archive the window" }));
  await waitFor(() =>
    expect(request).toHaveBeenCalledWith("/windows/bulk", "POST", {
      ids: ["week"],
      action: "archive",
    }),
  );
});
it("opens an editing panel, saves the window and closes the panel", async () => {
  vi.mocked(request).mockImplementation(async (_path, method) =>
    method ? {} : [windowData],
  );
  render(
    <MemoryRouter>
      <WindowManagement />
    </MemoryRouter>,
  );
  fireEvent.click(await screen.findByRole("button", { name: "Two weekends" }));
  expect(screen.getByRole("dialog", { name: "Two weekends" })).toBeTruthy();
  fireEvent.change(screen.getByLabelText("Window name"), {
    target: { value: "Gala weekends" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Save changes" }));
  await waitFor(() =>
    expect(request).toHaveBeenCalledWith(
      "/windows/week",
      "PUT",
      expect.objectContaining({ name: "Gala weekends", open: true }),
    ),
  );
  await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
});
it("shows every included date with missing, unavailable and partial responses, excluding gaps", async () => {
  vi.mocked(request).mockResolvedValue({
    window: windowData,
    members: [
      {
        memberId: "m",
        displayName: "Alex",
        days: [
          {
            date: "2099-10-01",
            status: "Available",
            from: "10:00:00",
            until: "14:00:00",
            note: "Morning",
          },
          { date: "2099-10-02", status: "Unavailable" },
        ],
      },
    ],
  });
  render(
    <MemoryRouter initialEntries={["/manage/windows/week"]}>
      <Routes>
        <Route
          path="/manage/windows/:windowId"
          element={<WindowAvailabilityGrid />}
        />
      </Routes>
    </MemoryRouter>,
  );
  const partial = await screen.findByRole("button", {
    name: /Alex, 1 Oct 2099: Available, 10:00–14:00/,
  });
  expect(partial.textContent).toBe("◷");
  expect(partial.closest("td")?.className).toBe("matrix-available");
  expect(
    screen
      .getByRole("button", { name: /2 Oct 2099: Unavailable/ })
      .closest("td")?.className,
  ).toBe("matrix-unavailable");
  expect(
    screen
      .getByRole("button", { name: /5 Oct 2099: No response/ })
      .closest("td")?.className,
  ).toBe("matrix-unset");
  expect(screen.queryByRole("columnheader", { name: "3 Oct 2099" })).toBeNull();
  fireEvent.click(partial);
  expect(screen.getByRole("status").textContent).toContain("10:00–14:00");
});
