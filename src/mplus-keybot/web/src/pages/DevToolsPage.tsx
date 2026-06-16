import { useEffect, useState } from "react";
import { apiGet, apiPost, getErrorMessage } from "../api";
import { CharacterGrid, ReadonlyCharacterCard } from "../components/character";
import { Alert, Loading } from "../components/ui";
import { cx } from "../css";
import type { DevToolsResponse } from "../types";
import shared from "../styles/shared.module.css";

export function DevToolsPage() {
  const [data, setData] = useState<DevToolsResponse | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiGet<DevToolsResponse>("/dev")
      .then(setData)
      .catch((err: unknown) => setError(getErrorMessage(err)));
  }, []);

  async function sync() {
    setError(null);
    try {
      const response = await apiPost<{ message: string }>(
        "/dev/raiderio/sync",
        {},
      );
      setMessage(response.message);
    } catch (err) {
      setError(getErrorMessage(err));
    }
  }

  if (error)
    return (
      <Alert
        kind="error"
        title="Development tools unavailable"
        message={error}
      />
    );
  if (!data) return <Loading />;

  return (
    <>
      <div className={shared.pageHeader}>
        <h1>Development Tools</h1>
        <p>
          Local-only helpers for exercising the follow workflow and refreshing
          Raider.IO character data without a Discord bot connection.
        </p>
      </div>
      {message ? (
        <Alert
          kind="success"
          title="Raider.IO sync scheduled."
          message={message}
        />
      ) : null}
      <div className={shared.card}>
        <div className={shared.cardTitle}>Follow management flow</div>
        <p>
          Create a short-lived dev follow link for a test Discord user, then
          continue through the normal Battle.net authorization flow.
        </p>
        <p>
          <a
            className={cx(shared.button, shared.primaryButton)}
            href="/dev/follow?discordUserId=test-user"
          >
            Start Dev Flow
          </a>
        </p>
      </div>
      <div className={shared.card}>
        <div className={shared.cardTitle}>Raider.IO sync</div>
        <p>
          Schedule the Raider.IO check job immediately for{" "}
          <strong>{data.followedCharacters.length}</strong> followed character
          {data.followedCharacters.length === 1 ? "" : "s"}. In local
          development, the job refreshes data without posting to Discord when no
          bot token is configured.
        </p>
        <button
          type="button"
          className={cx(shared.button, shared.primaryButton)}
          onClick={sync}
        >
          Force Raider.IO Sync
        </button>
      </div>
      {data.followedCharacters.length > 0 ? (
        <>
          <h2 className={shared.sectionHeading}>Currently Followed</h2>
          <CharacterGrid>
            {data.followedCharacters.map((character) => (
              <ReadonlyCharacterCard
                key={`${character.region}|${character.realmDisplayName}|${character.name}`}
                character={character}
                metaSuffix={<>checked {character.lastCheckedText}</>}
              />
            ))}
          </CharacterGrid>
        </>
      ) : null}
    </>
  );
}
