const hopByHopHeaders = new Set([
  "connection",
  "content-encoding",
  "content-length",
  "keep-alive",
  "proxy-authenticate",
  "proxy-authorization",
  "te",
  "trailer",
  "transfer-encoding",
  "upgrade",
]);

export async function loader({ request }: { request: Request }) {
  return proxyToApi(request);
}

export async function action({ request }: { request: Request }) {
  return proxyToApi(request);
}

async function proxyToApi(request: Request) {
  const url = new URL(request.url);
  const response = await fetch(apiUrl(`${url.pathname}${url.search}`), {
    method: request.method,
    headers: requestHeaders(request, url),
    body: await requestBody(request),
    redirect: "manual",
  });

  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers: responseHeaders(response),
  });
}

function apiUrl(pathAndQuery: string) {
  const base = process.env.API_BASE_URL;
  if (!base)
    throw new Error("API_BASE_URL is not configured for API proxying.");
  return new URL(
    pathAndQuery,
    base.endsWith("/") ? base : `${base}/`,
  ).toString();
}

function requestHeaders(request: Request, url: URL) {
  const headers = new Headers();
  request.headers.forEach((value, name) => {
    if (!hopByHopHeaders.has(name.toLowerCase())) headers.set(name, value);
  });
  // Caddy terminates TLS and sets x-forwarded-proto/host on the hop to this
  // Node server. Preserve those so the API's secure-cookie/antiforgery checks
  // see the public HTTPS request; fall back to the local URL only when no
  // upstream forwarded header is present (e.g. direct local testing).
  if (!headers.has("x-forwarded-proto"))
    headers.set("x-forwarded-proto", url.protocol.replace(/:$/, ""));
  if (!headers.has("x-forwarded-host"))
    headers.set("x-forwarded-host", url.host);
  return headers;
}

async function requestBody(request: Request) {
  if (request.method === "GET" || request.method === "HEAD") return undefined;
  return await request.arrayBuffer();
}

function responseHeaders(response: Response) {
  const headers = new Headers();
  response.headers.forEach((value, name) => {
    if (
      !hopByHopHeaders.has(name.toLowerCase()) &&
      name.toLowerCase() !== "set-cookie"
    )
      headers.set(name, value);
  });

  const getSetCookie = (
    response.headers as Headers & { getSetCookie?: () => string[] }
  ).getSetCookie;
  for (const cookie of getSetCookie?.call(response.headers) ?? [])
    headers.append("set-cookie", cookie);

  return headers;
}
