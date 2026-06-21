const forwardedHeaders = [
  "cookie",
  "traceparent",
  "tracestate",
  "x-request-id",
  "x-correlation-id",
] as const;

export async function apiGet<T>(request: Request, path: string): Promise<T> {
  const response = await fetch(apiUrl(path), {
    headers: headersForApi(request),
  });
  const json = await response.json().catch(() => null);
  if (!response.ok)
    throw new Error(
      json?.message ?? `Request failed with HTTP ${response.status}`,
    );
  if (json === null) throw new Error("Expected a JSON response from the API.");
  return json as T;
}

export function defaultSession() {
  return {
    isAuthenticated: false,
    isDevelopment: false,
    homeUrl: "/",
    signInUrl: "/api/signin",
    signOutUrl: "/api/signout",
    manageUrl: "/follow/characters",
    devUrl: "/dev",
  };
}

function apiUrl(path: string) {
  const base = process.env.API_BASE_URL;
  if (!base)
    throw new Error("API_BASE_URL is not configured for SSR API calls.");
  return new URL(path, base.endsWith("/") ? base : `${base}/`).toString();
}

function headersForApi(request: Request) {
  const headers = new Headers();
  for (const name of forwardedHeaders) {
    const value = request.headers.get(name);
    if (value) headers.set(name, value);
  }
  // Preserve the upstream forwarded headers set by Caddy so the API sees the
  // public HTTPS request; fall back to the local URL only when absent.
  const url = new URL(request.url);
  if (!headers.has("x-forwarded-proto"))
    headers.set("x-forwarded-proto", url.protocol.replace(/:$/, ""));
  if (!headers.has("x-forwarded-host"))
    headers.set("x-forwarded-host", url.host);
  return headers;
}
