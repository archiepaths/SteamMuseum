const base = (
  import.meta.env.VITE_API_URL ??
  (import.meta.env.DEV ? "https://localhost:7240" : "")
).replace(/\/$/, "");
let csrf: string | undefined;
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}
export async function refreshCsrf() {
  csrf = undefined;
  const result = await request<{ token: string }>("/auth/csrf");
  csrf = result.token;
}
export async function request<T>(
  path: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  if (method !== "GET" && !csrf) await refreshCsrf();
  let response: Response;
  try {
    response = await fetch(`${base}/api${path}`, {
      method,
      credentials: "include",
      headers: {
        ...(body === undefined ? {} : { "Content-Type": "application/json" }),
        ...(method === "GET" ? {} : { "X-CSRF-TOKEN": csrf! }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new Error(
      "Cannot reach the museum API. Check your connection and that the API is running.",
    );
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const fallback: Record<number, string> = {
      401: "Sign-in failed or your session has expired. Please sign in again.",
      403: "You do not have permission for this action.",
      429: "Too many sign-in attempts. Please wait a minute and try again.",
    };
    if (response.status === 401) {
      csrf = undefined;
      if (path !== "/auth/login")
        window.dispatchEvent(new Event("session-expired"));
    }
    if (response.status === 400 && problem?.title?.includes("CSRF"))
      csrf = undefined;
    throw new ApiError(
      response.status,
      problem?.errors
        ? Object.values(problem.errors).flat().join(" ")
        : (problem?.title ??
            fallback[response.status] ??
            "The request failed. Please try again."),
    );
  }
  if (["/auth/login", "/auth/logout", "/auth/password"].includes(path))
    await refreshCsrf();
  return response.status === 204 ? (undefined as T) : response.json();
}
