import { useEffect, useState } from "react";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import "./styles/global.css";
import { apiGet } from "./api";
import styles from "./App.module.css";
import { Nav } from "./components/Nav";
import { CharacterManagementPage } from "./pages/CharacterManagementPage";
import { DevToolsPage } from "./pages/DevToolsPage";
import { HomePage } from "./pages/HomePage";
import { routeBase } from "./routing";
import type { SessionResponse } from "./types";

export function App() {
  return (
    <BrowserRouter basename={routeBase()}>
      <AppLayout />
    </BrowserRouter>
  );
}

function AppLayout() {
  const [session, setSession] = useState<SessionResponse | null>(null);

  useEffect(() => {
    apiGet<SessionResponse>("/session")
      .then(setSession)
      .catch(() =>
        setSession({
          isAuthenticated: false,
          isDevelopment: false,
          homeUrl: "/",
          signInUrl: "/signin",
          signOutUrl: "/signout",
          manageUrl: "/follow/characters",
          devUrl: "/dev",
        }),
      );
  }, []);

  return (
    <>
      <Nav session={session} />
      <main className={styles.mainContent}>
        <Routes>
          <Route path="/" element={<HomePage session={session} />} />
          <Route
            path="/follow/characters"
            element={<CharacterManagementPage />}
          />
          <Route path="/dev" element={<DevToolsPage />} />
          <Route path="*" element={<HomePage session={session} />} />
        </Routes>
      </main>
      <footer className={styles.footer}>
        mplus-keybot · WoW Mythic+ run tracker
      </footer>
    </>
  );
}
