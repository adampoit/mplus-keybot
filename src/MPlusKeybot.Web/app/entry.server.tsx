import { renderToReadableStream } from "react-dom/server";
import { ServerRouter } from "react-router";
import type { EntryContext } from "react-router";

export default async function handleRequest(
  request: Request,
  responseStatusCode: number,
  responseHeaders: Headers,
  routerContext: EntryContext,
) {
  const stream = await renderToReadableStream(
    <ServerRouter context={routerContext} url={request.url} />,
  );
  await stream.allReady;

  responseHeaders.set("Content-Type", "text/html; charset=utf-8");
  return new Response(stream, {
    headers: responseHeaders,
    status: responseStatusCode,
  });
}
