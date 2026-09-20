import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

describe("API session and request contract", () => {
  beforeEach(() => {
    vi.resetModules();
    vi.stubEnv("VITE_API_URL", "");
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.unstubAllEnvs();
  });
  it("sends cookies and CSRF for login, then refreshes the token before subsequent writes", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ token: "anonymous" }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(Response.json({ token: "signed-in" }))
      .mockResolvedValueOnce(Response.json({ id: "created" }));
    vi.stubGlobal("fetch", fetcher);
    const { request } = await import("./api");
    await request("/auth/login", "POST", {
      email: "member@example.org",
      password: "test",
    });
    await request("/duties", "POST", { name: "Morning" });
    expect(fetcher.mock.calls.map((c) => c[0])).toEqual([
      "/api/auth/csrf",
      "/api/auth/login",
      "/api/auth/csrf",
      "/api/duties",
    ]);
    expect(fetcher.mock.calls[1][1]).toMatchObject({
      credentials: "include",
      headers: { "X-CSRF-TOKEN": "anonymous" },
    });
    expect(fetcher.mock.calls[3][1].headers["X-CSRF-TOKEN"]).toBe("signed-in");
  });
  it("surfaces business conflicts and validation errors without retrying writes", async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ token: "token" }))
      .mockResolvedValueOnce(
        Response.json(
          { title: "Maximum assignments reached." },
          { status: 409 },
        ),
      );
    vi.stubGlobal("fetch", fetcher);
    const { request } = await import("./api");
    await expect(
      request("/duties/1/assignment", "POST", { memberId: "member" }),
    ).rejects.toThrow("Maximum assignments reached.");
    expect(fetcher).toHaveBeenCalledTimes(2);
    fetcher.mockResolvedValueOnce(
      Response.json(
        { errors: { Name: ["Name is required."] } },
        { status: 400 },
      ),
    );
    await expect(request("/duties", "POST", {})).rejects.toThrow(
      "Name is required.",
    );
  });
  it("reports throttling for an empty error response", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(Response.json({ token: "token" }))
        .mockResolvedValueOnce(new Response(null, { status: 429 })),
    );
    const { request } = await import("./api");
    await expect(request("/auth/login", "POST", {})).rejects.toThrow(
      "Too many sign-in attempts",
    );
  });
  it("discards the previous session token before signing in again", async () => {
    const dispatchEvent = vi.fn();
    vi.stubGlobal("window", { dispatchEvent });
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce(Response.json({ token: "old-session" }))
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(Response.json({ token: "anonymous" }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(Response.json({ token: "new-session" }));
    vi.stubGlobal("fetch", fetcher);
    const { request } = await import("./api");
    await expect(request("/duties", "POST", {})).rejects.toThrow(
      "session has expired",
    );
    expect(dispatchEvent).toHaveBeenCalledOnce();
    await request("/auth/login", "POST", {
      email: "member@example.org",
      password: "test",
    });
    expect(fetcher.mock.calls[3][1].headers["X-CSRF-TOKEN"]).toBe("anonymous");
  });
});
