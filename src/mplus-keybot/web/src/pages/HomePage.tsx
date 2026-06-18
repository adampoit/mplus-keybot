import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { apiGet, getErrorMessage } from "../api";
import { CharacterHomeRow } from "../components/character";
import { Alert, EmptyState, Loading } from "../components/ui";
import { cx } from "../css";
import type { HomeResponse, SessionResponse } from "../types";
import shared from "../styles/shared.module.css";
import styles from "./HomePage.module.css";

export function HomePage({ session }: { session: SessionResponse | null }) {
  const [home, setHome] = useState<HomeResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiGet<HomeResponse>("/home")
      .then(setHome)
      .catch((err: unknown) => setError(getErrorMessage(err)));
  }, []);

  return (
    <>
      <HomeHero />
      {error ? (
        <Alert kind="error" title="Unable to load home" message={error} />
      ) : !home ? (
        <Loading />
      ) : (
        <HomeCharacterContent home={home} session={session} />
      )}
    </>
  );
}

function HomeHero() {
  return (
    <div className={styles.hero}>
      <h1>🔑 mplus-keybot</h1>
      <p>
        Track Mythic+ runs for your World of Warcraft characters and get
        announcements in Discord. To manage follows, run <code>/follow</code> in
        Discord.
      </p>
      <div className={styles.heroActions}>
        <Link
          className={cx(shared.button, shared.primaryButton)}
          to="/follow/characters"
        >
          Manage Characters
        </Link>
      </div>
    </div>
  );
}

function HomeCharacterContent({
  home,
  session,
}: {
  home: HomeResponse;
  session: SessionResponse | null;
}) {
  return (
    <>
      {home.status === "unauthenticated" ? (
        <div className={shared.card}>
          <div className={shared.cardTitle}>View Your Characters</div>
          <p>
            Sign in with Battle.net to see which of your verified characters are
            currently followed by this bot.
          </p>
          <p>
            <a
              className={cx(shared.button, shared.primaryButton)}
              href={session?.signInUrl ?? "/signin"}
            >
              Sign in with Battle.net
            </a>
          </p>
        </div>
      ) : null}

      {home.message ? (
        <Alert
          kind="error"
          title="Unable to load characters"
          message={home.message}
        />
      ) : null}

      {home.followedCharacters.length > 0 ? (
        <>
          <h2 className={shared.sectionHeading}>Character Progress</h2>
          <div className={styles.homeGrid}>
            {home.followedCharacters.map((character) => (
              <CharacterHomeRow key={character.key} character={character} />
            ))}
          </div>
        </>
      ) : null}

      {home.status === "ok" && home.followedCharacters.length === 0 ? (
        home.otherCharacters.length === 0 ? (
          <EmptyState
            icon="🏳️"
            message="No retail WoW characters were returned by Battle.net for this account."
          />
        ) : (
          <div className={shared.card}>
            <div className={shared.cardTitle}>No followed characters yet</div>
            <p>
              Choose which verified characters this bot should follow to see
              their Mythic+ progress here.
            </p>
            <Link
              className={cx(shared.button, shared.primaryButton)}
              to="/follow/characters"
            >
              Manage Characters
            </Link>
          </div>
        )
      ) : null}
    </>
  );
}
