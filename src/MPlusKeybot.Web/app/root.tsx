import {
  Links,
  Meta,
  Outlet,
  Scripts,
  ScrollRestoration,
  useLoaderData,
} from "react-router";
import type { LoaderFunctionArgs } from "react-router";
import { Nav } from "./src/components/Nav";
import styles from "./src/App.module.css";
import "./src/styles/global.css";
import type { SessionResponse } from "./src/types";
import { apiGet, defaultSession } from "./server-api";

export type AppOutletContext = {
  session: SessionResponse;
};

export async function loader({ request }: LoaderFunctionArgs) {
  try {
    return { session: await apiGet<SessionResponse>(request, "/api/session") };
  } catch {
    return { session: defaultSession() };
  }
}

export function Layout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <Meta />
        <Links />
      </head>
      <body>{children}</body>
    </html>
  );
}

export default function Root() {
  const { session } = useLoaderData<typeof loader>();

  return (
    <>
      <Nav session={session} />
      <main className={styles.mainContent}>
        <Outlet context={{ session } satisfies AppOutletContext} />
      </main>
      <footer className={styles.footer}>
        mplus-keybot · WoW Mythic+ run tracker
      </footer>
      <ScrollRestoration />
      <Scripts />
    </>
  );
}
