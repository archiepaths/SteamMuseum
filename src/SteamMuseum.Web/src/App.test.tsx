// @vitest-environment jsdom
import { afterEach, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import App from "./App";
import { request } from "./api";
vi.mock("./api", () => ({
  request: vi.fn(),
  ApiError: class extends Error {},
}));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});
it("hides privileged navigation for members", async () => {
  vi.mocked(request).mockImplementation(async (path) =>
    path === "/auth/me"
      ? {
          id: "1",
          displayName: "Member",
          roles: ["Member"],
          mustChangePassword: false,
        }
      : [],
  );
  render(<App />);
  await screen.findByRole("heading", { name: "My availability" });
  expect(screen.queryByRole("button", { name: "Administration" })).toBeNull();
  expect(screen.queryByRole("button", { name: "Roster planner" })).toBeNull();
  expect(screen.queryByRole("button", { name: "Staff records" })).toBeNull();
});
it("requires a temporary password change before loading business data", async () => {
  vi.mocked(request).mockResolvedValue({
    id: "1",
    displayName: "Admin",
    roles: ["Administrator"],
    mustChangePassword: true,
  });
  render(<App />);
  await screen.findByRole("heading", { name: "Choose your own password" });
  expect(vi.mocked(request).mock.calls.every((c) => c[0] === "/auth/me")).toBe(
    true,
  );
  expect(
    (
      screen.getByRole("button", {
        name: "Administration",
      }) as HTMLButtonElement
    ).disabled,
  ).toBe(true);
});
