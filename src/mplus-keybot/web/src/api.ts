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
  if (!response.ok) throw new Error(json?.message ?? response.statusText);
  if (json === null) throw new Error("Expected a JSON response from the API.");
  return json as T;
}

function apiUrl(path: string) {
  const root = document.getElementById("root");
  const base = root?.dataset.apiBase ?? "/api";
  return `${base}${path}`;
}
