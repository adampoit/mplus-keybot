import { Link } from "react-router-dom";
import type { SessionResponse } from "../types";
import styles from "./Nav.module.css";

export function Nav({ session }: { session: SessionResponse | null }) {
  return (
    <nav className={styles.navbar}>
      <div className={styles.navbarInner}>
        <Link className={styles.navbarBrand} to="/">
          🔑 mplus-keybot
        </Link>
        <div className={styles.navbarNav}>
          <Link to="/">Home</Link>
          {session?.isAuthenticated ? (
            <>
              <Link to="/follow/characters">Manage Characters</Link>
              <a href={session.signOutUrl}>Sign Out</a>
            </>
          ) : (
            <a href={session?.signInUrl ?? "/signin"}>Sign In</a>
          )}
          {session?.isDevelopment ? <Link to="/dev">Dev Tools</Link> : null}
        </div>
      </div>
    </nav>
  );
}
