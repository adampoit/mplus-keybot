import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type FormEvent,
} from "react";
import { Link, useNavigate } from "react-router-dom";
import { apiGet, apiPost, getErrorMessage } from "../api";
import {
  CharacterGrid,
  ReadonlyCharacterCard,
  SelectableCharacterCard,
} from "../components/character";
import { Alert, EmptyState, Loading } from "../components/ui";
import { cx } from "../css";
import type {
  CharacterManagementResponse,
  SaveCharactersResponse,
  SavedCharacter,
} from "../types";
import { compareCardNames } from "../wow";
import shared from "../styles/shared.module.css";
import styles from "./CharacterManagementPage.module.css";

export function CharacterManagementPage() {
  const [data, setData] = useState<CharacterManagementResponse | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [initialSelected, setInitialSelected] = useState<Set<string>>(
    new Set(),
  );
  const [filter, setFilter] = useState("");
  const [sort, setSort] = useState("level");
  const [saveResult, setSaveResult] = useState<SaveCharactersResponse | null>(
    null,
  );
  const [error, setError] = useState<string | null>(null);
  const [pendingNavigation, setPendingNavigation] = useState<string | null>(
    null,
  );
  const navigate = useNavigate();

  const loadCharacters = useCallback(async () => {
    setError(null);
    try {
      const response =
        await apiGet<CharacterManagementResponse>("/follow/characters");
      setData(response);
      const followed = new Set(
        response.characters
          .filter((character) => character.followed)
          .map((character) => character.key),
      );
      setSelected(followed);
      setInitialSelected(followed);
      setPendingNavigation(null);
    } catch (err: unknown) {
      setError(getErrorMessage(err));
    }
  }, []);

  useEffect(() => {
    loadCharacters();
  }, [loadCharacters]);

  const visibleCharacters = useMemo(() => {
    const term = filter.toLowerCase().trim();
    return (
      data?.characters.filter(
        (character) =>
          !term ||
          character.name.toLowerCase().includes(term) ||
          character.realmDisplayName.toLowerCase().includes(term),
      ) ?? []
    );
  }, [data, filter]);

  const displayGroups = useMemo(() => {
    if (!data) return [];

    if (sort === "name") {
      return [
        {
          realm: "All Characters",
          characters: [...data.characters].sort(compareCardNames),
        },
      ];
    }

    const realms = Array.from(
      new Set(data.characters.map((character) => character.realmDisplayName)),
    ).sort();
    return realms.map((realm) => ({
      realm,
      characters: data.characters
        .filter((character) => character.realmDisplayName === realm)
        .sort(
          (a, b) =>
            (b.level ?? 0) - (a.level ?? 0) || a.name.localeCompare(b.name),
        ),
    }));
  }, [data, sort]);

  const hasChanges = useMemo(() => {
    if (selected.size !== initialSelected.size) return true;
    for (const key of selected) {
      if (!initialSelected.has(key)) return true;
    }
    return false;
  }, [selected, initialSelected]);

  const handleSave = useCallback(async () => {
    if (!data?.form) return;

    setError(null);
    try {
      const result = await apiPost<SaveCharactersResponse>(
        "/follow/characters",
        {
          verificationSetId: data.form.verificationSetId,
          characters: Array.from(selected),
        },
        { RequestVerificationToken: data.form.requestToken },
      );
      setInitialSelected(new Set(selected));
      setPendingNavigation(null);
      setSaveResult(result);
    } catch (err) {
      setError(getErrorMessage(err));
    }
  }, [data, selected]);

  function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    handleSave();
  }

  function continueManagingCharacters() {
    setSaveResult(null);
    setData(null);
    loadCharacters();
  }

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key === "s") {
        event.preventDefault();
        if (hasChanges) {
          handleSave();
        }
      }
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [hasChanges, handleSave]);

  useEffect(() => {
    function onBeforeUnload(event: BeforeUnloadEvent) {
      if (hasChanges) {
        event.preventDefault();
        event.returnValue = "";
      }
    }
    window.addEventListener("beforeunload", onBeforeUnload);
    return () => window.removeEventListener("beforeunload", onBeforeUnload);
  }, [hasChanges]);

  useEffect(() => {
    function onClick(event: MouseEvent) {
      const anchor = (event.target as HTMLElement).closest("a");
      if (!anchor) return;

      const href = anchor.getAttribute("href");
      if (!href) return;
      if (
        href.startsWith("http") ||
        href.startsWith("#") ||
        href.startsWith("javascript:")
      )
        return;
      if (event.button !== 0) return;
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey)
        return;

      if (hasChanges) {
        event.preventDefault();
        event.stopPropagation();
        setPendingNavigation(href);
      }
    }
    document.addEventListener("click", onClick, true);
    return () => document.removeEventListener("click", onClick, true);
  }, [hasChanges]);

  if (error)
    return (
      <Alert kind="error" title="Unable to manage characters" message={error} />
    );
  if (!data) return <Loading />;
  if (saveResult)
    return (
      <SaveResult result={saveResult} onContinue={continueManagingCharacters} />
    );
  if (data.status === "instructions")
    return <CharacterManagementInstructions />;
  if (data.message)
    return (
      <Alert
        kind="error"
        title="Unable to manage characters"
        message={data.message}
      />
    );

  return (
    <>
      <Link className={shared.backLink} to="/">
        ← Home
      </Link>
      <div className={shared.pageHeader}>
        <h1>Manage Characters</h1>
        <p>
          Select the characters this bot should follow. Only characters returned
          by this Battle.net sign-in can be changed.
        </p>
      </div>

      {pendingNavigation && (
        <div className={styles.dialogOverlay}>
          <div className={styles.dialog} role="dialog" aria-modal="true">
            <div className={styles.dialogTitle}>Unsaved changes</div>
            <p className={styles.dialogMessage}>
              You have unsaved follow settings. If you leave, your changes will
              be lost.
            </p>
            <div className={styles.dialogActions}>
              <button
                type="button"
                className={cx(shared.button, shared.secondaryButton)}
                onClick={() => setPendingNavigation(null)}
              >
                Stay on page
              </button>
              <button
                type="button"
                className={cx(shared.button, shared.primaryButton)}
                onClick={() => {
                  const target = pendingNavigation;
                  setPendingNavigation(null);
                  navigate(target);
                }}
              >
                Leave without saving
              </button>
            </div>
          </div>
        </div>
      )}

      {data.characters.length === 0 ? (
        <EmptyState
          icon="🏳️"
          message="No retail WoW characters were returned by Battle.net for this account."
        />
      ) : (
        <form onSubmit={save}>
          <div className={styles.toolbar}>
            <div className={styles.toolbarGroup}>
              <span className={styles.toolbarLabel}>Search</span>
              <input
                type="text"
                id="char-filter"
                className={styles.toolbarInput}
                placeholder="Filter by name or realm..."
                value={filter}
                onChange={(event) => setFilter(event.currentTarget.value)}
              />
            </div>
            <div className={styles.toolbarGroup}>
              <span className={styles.toolbarLabel}>Sort</span>
              <select
                id="char-sort"
                className={styles.toolbarSelect}
                value={sort}
                onChange={(event) => setSort(event.currentTarget.value)}
              >
                <option value="level">Level (high → low)</option>
                <option value="name">Name (A → Z)</option>
              </select>
            </div>
            <span className={styles.toolbarCount} id="char-count">
              {visibleCharacters.length} character
              {visibleCharacters.length === 1 ? "" : "s"}
            </span>
          </div>

          {displayGroups.map((group, index) => {
            const anyVisible = group.characters.some((character) =>
              visibleCharacters.includes(character),
            );
            return (
              <div
                className={cx(
                  "realm-group",
                  styles.realmGroup,
                  !anyVisible && sort !== "name" && "hidden",
                  !anyVisible && sort !== "name" && styles.hidden,
                )}
                data-realm={group.realm}
                key={`${group.realm}-${index}`}
              >
                <h3>{group.realm}</h3>
                <CharacterGrid>
                  {group.characters.map((character) => (
                    <SelectableCharacterCard
                      key={character.key}
                      character={character}
                      checked={selected.has(character.key)}
                      hidden={!visibleCharacters.includes(character)}
                      onChange={(checked) => {
                        setSelected((current) => {
                          const next = new Set(current);
                          if (checked) next.add(character.key);
                          else next.delete(character.key);
                          return next;
                        });
                      }}
                    />
                  ))}
                </CharacterGrid>
              </div>
            );
          })}

          {hasChanges && (
            <div className={styles.stickyBar}>
              <span className={styles.stickyBarInfo}>
                {selected.size} character
                {selected.size === 1 ? "" : "s"} selected
                <span className={styles.stickyBarChanges}>
                  {" "}
                  · Unsaved changes
                </span>
              </span>
              <div className={styles.stickyBarActions}>
                <button
                  type="button"
                  className={cx(
                    shared.button,
                    shared.secondaryButton,
                    styles.stickyBarButton,
                  )}
                  onClick={() => setSelected(new Set(initialSelected))}
                >
                  Reset
                </button>
                <button
                  type="submit"
                  className={cx(
                    shared.button,
                    shared.primaryButton,
                    styles.stickyBarButton,
                  )}
                >
                  Save Follow Settings
                </button>
              </div>
            </div>
          )}
        </form>
      )}
    </>
  );
}

function CharacterManagementInstructions() {
  return (
    <>
      <Link className={shared.backLink} to="/">
        ← Home
      </Link>
      <div className={shared.pageHeader}>
        <h1>Manage Characters</h1>
        <p>
          Character management starts from Discord so the bot can connect your
          Battle.net characters to your Discord user.
        </p>
      </div>
      <div className={shared.card}>
        <div className={shared.cardTitle}>Start from Discord</div>
        <p>
          Run <code>/follow</code> in Discord. The bot will send you a private,
          short-lived link to sign in with Battle.net and choose which
          characters to follow.
        </p>
      </div>
    </>
  );
}

function SaveResult({
  result,
  onContinue,
}: {
  result: SaveCharactersResponse;
  onContinue: () => void;
}) {
  return (
    <>
      <Link className={shared.backLink} to="/">
        ← Home
      </Link>
      <Alert
        kind="success"
        title="Settings saved!"
        message="Your follow settings have been updated."
      />
      <p>
        <button
          type="button"
          className={cx(shared.button, shared.secondaryButton)}
          onClick={onContinue}
        >
          Continue managing characters
        </button>
      </p>
      <CharacterUpdateSection
        title="Now Followed"
        characters={result.followed}
      />
      <CharacterUpdateSection
        title="Now Unfollowed"
        characters={result.unfollowed}
      />
      {result.followed.length === 0 && result.unfollowed.length === 0 ? (
        <EmptyState message="No follow states changed." />
      ) : null}
    </>
  );
}

function CharacterUpdateSection({
  title,
  characters,
}: {
  title: string;
  characters: SavedCharacter[];
}) {
  if (characters.length === 0) return null;

  return (
    <>
      <h2 className={shared.sectionHeading}>{title}</h2>
      <CharacterGrid>
        {characters
          .slice()
          .sort(
            (a, b) =>
              a.realmDisplayName.localeCompare(b.realmDisplayName) ||
              a.name.localeCompare(b.name),
          )
          .map((character) => (
            <ReadonlyCharacterCard key={character.key} character={character} />
          ))}
      </CharacterGrid>
    </>
  );
}
