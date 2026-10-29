const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public errorCode?: string,
    public traceId?: string,
  ) {
    super(message);
  }
}

function getToken(): string | null {
  if (typeof window === "undefined") return null;
  return window.localStorage.getItem("bms.accessToken");
}

export function setToken(token: string | null) {
  if (typeof window === "undefined") return;
  if (token) window.localStorage.setItem("bms.accessToken", token);
  else window.localStorage.removeItem("bms.accessToken");
}

function getRefreshToken(): string | null {
  if (typeof window === "undefined") return null;
  return window.localStorage.getItem("bms.refreshToken");
}

export function setRefreshToken(token: string | null) {
  if (typeof window === "undefined") return;
  if (token) window.localStorage.setItem("bms.refreshToken", token);
  else window.localStorage.removeItem("bms.refreshToken");
}

/** Clears every stored auth artifact and sends the browser to /login — the one place "the session is over" is decided. */
function hardLogout() {
  if (typeof window === "undefined") return;
  setToken(null);
  setRefreshToken(null);
  window.localStorage.removeItem("bms.user");
  if (!window.location.pathname.startsWith("/login")) {
    window.location.href = "/login";
  }
}

// A 401 used to be treated as "session over" immediately. Access tokens
// are short-lived (15 min) on purpose, so that alone made every session
// die mid-use — this makes one 401 attempt a silent refresh-and-retry
// first, and only hard-logs-out if the refresh token itself is no longer
// valid. refreshPromise is shared across concurrent requests so five
// components all getting a 401 at once triggers exactly one refresh call,
// not five racing ones.
let refreshPromise: Promise<boolean> | null = null;

async function refreshAccessToken(): Promise<boolean> {
  const refreshToken = getRefreshToken();
  if (!refreshToken) return false;

  try {
    const response = await fetch(`${API_URL}/api/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
    });
    if (!response.ok) return false;

    const body = await response.json();
    setToken(body.accessToken);
    setRefreshToken(body.refreshToken);
    if (typeof window !== "undefined") window.localStorage.setItem("bms.user", JSON.stringify(body.user));
    return true;
  } catch {
    return false;
  }
}

/** Margin before `exp` at which a token already counts as expired, so a connection isn't opened with one that lapses mid-negotiation. */
const EXPIRY_MARGIN_MS = 30_000;

function isExpired(token: string): boolean {
  try {
    const payload = JSON.parse(atob(token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/")));
    return typeof payload.exp !== "number" || payload.exp * 1000 <= Date.now() + EXPIRY_MARGIN_MS;
  } catch {
    return true;
  }
}

/**
 * For callers that can't recover from a 401 the way request() does. SignalR
 * negotiates with exactly the token it's handed and simply fails on 401, so a
 * React-state snapshot of the token goes stale 15 minutes into a session:
 * request() silently refreshes the stored token, but the snapshot never
 * changes. This always reads the current stored token, and refreshes it first
 * (through the same shared refreshPromise) when it has expired.
 */
export async function getValidAccessToken(): Promise<string> {
  const token = getToken();
  if (token && !isExpired(token)) return token;

  refreshPromise ??= refreshAccessToken().finally(() => {
    refreshPromise = null;
  });
  if (await refreshPromise) return getToken() ?? "";

  hardLogout();
  return "";
}

async function request<T>(path: string, init?: RequestInit, isRetry = false): Promise<T> {
  const token = getToken();
  const headers: Record<string, string> = {
    // FormData sets its own multipart Content-Type (with the boundary) — only JSON bodies get one here.
    ...(init?.body && !(init.body instanceof FormData) ? { "Content-Type": "application/json" } : {}),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(init?.headers as Record<string, string> | undefined),
  };

  const response = await fetch(`${API_URL}${path}`, { ...init, headers });

  const isAuthEndpoint = path.startsWith("/api/auth/");
  if (response.status === 401 && typeof window !== "undefined" && !isAuthEndpoint) {
    if (!isRetry) {
      refreshPromise ??= refreshAccessToken().finally(() => {
        refreshPromise = null;
      });
      const refreshed = await refreshPromise;
      if (refreshed) return request<T>(path, init, true);
    }
    hardLogout();
  }

  if (!response.ok) {
    let message = response.statusText;
    let errorCode: string | undefined;
    let traceId: string | undefined;
    try {
      const body = await response.json();
      message = body.message ?? body.title ?? message;
      // ASP.NET model validation (400 ValidationProblemDetails): its title is
      // the generic "One or more validation errors occurred.", the useful part
      // is the per-field messages.
      if (!body.message && body.errors && typeof body.errors === "object") {
        const details = Object.values(body.errors as Record<string, string[]>).flat().filter(Boolean);
        if (details.length > 0) message = details.join(" ");
      }
      errorCode = body.errorCode;
      traceId = body.traceId;
    } catch {
      // non-JSON error body — fall back to statusText
    }
    throw new ApiError(message, response.status, errorCode, traceId);
  }

  if (response.status === 204) return undefined as T;

  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("application/json")) return response.json() as Promise<T>;
  return response.text() as unknown as Promise<T>;
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) => request<T>(path, { method: "POST", body: body !== undefined ? JSON.stringify(body) : undefined }),
  put: <T>(path: string, body?: unknown) => request<T>(path, { method: "PUT", body: body !== undefined ? JSON.stringify(body) : undefined }),
  delete: <T>(path: string) => request<T>(path, { method: "DELETE" }),
  /** multipart/form-data upload, e.g. a firmware binary. */
  upload: <T>(path: string, form: FormData) => request<T>(path, { method: "POST", body: form }),
};

/**
 * Downloads a protected file (Authorization header required, so a plain link
 * won't do) and hands it to the browser as a save.
 */
export async function downloadFile(path: string, fileName: string): Promise<void> {
  const token = await getValidAccessToken();
  const response = await fetch(`${API_URL}${path}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
  if (!response.ok) {
    let message = response.statusText;
    try {
      message = (await response.json()).message ?? message;
    } catch {
      // non-JSON error body
    }
    throw new ApiError(message, response.status);
  }
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}

export function apiFileUrl(path: string): string {
  return `${API_URL}${path}`;
}

export { API_URL, getRefreshToken };
