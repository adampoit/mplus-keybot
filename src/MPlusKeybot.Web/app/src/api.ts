import { withBasePath } from "./base-path";

export async function apiGet<T>(path: string): Promise<T> {
  const response = await fetch(apiUrl(path));
  return readJson<T>(response);
}

export async function apiPost<T>(
  path: string,
  body: unknown,
  headers: Record<string, string> = {},
): Promise<T> {
  const response = await fetch(apiUrl(path), {
    method: "POST",
    headers: { "Content-Type": "application/json", ...headers },
    body: JSON.stringify(body),
  });
  return readJson<T>(response);
}

export function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Unexpected error";
}

async function readJson<T>(response: Response): Promise<T> {
  const json = await response.json().catch(() => null);
  if (!response.ok) {
    // Only surface the API's curated `message` field (from ErrorResponse).
    // Never surface ProblemDetails `detail`/`title` — those can contain raw
    // exception text in Development and should stay server-side.
    const message =
      json !== null &&
      typeof json === "object" &&
      typeof (json as { message?: unknown }).message === "string"
        ? (json as { message: string }).message
        : undefined;
    throw new Error(message || `Request failed with HTTP ${response.status}`);
  }
  if (json === null) throw new Error("Expected a JSON response from the API.");
  return json as T;
}

function apiUrl(path: string) {
  return withBasePath(`/api${path}`);
}
