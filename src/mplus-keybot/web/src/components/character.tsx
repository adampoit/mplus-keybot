import type { ReactNode } from "react";
import { cx } from "../css";
import type { Character } from "../types";
import {
  formatLastChecked,
  getAvatarColor,
  getClassColor,
  getClassIcon,
  getDungeonDisplayName,
  getRaiderIoUrl,
  getWowArmoryUrl,
} from "../wow";
import styles from "./character.module.css";

type CharacterCardData = {
  name: string;
  realmDisplayName?: string | null;
  realm?: string | null;
  region: string;
  renderUrl?: string | null;
  level?: number | null;
  maxLevel?: number | null;
  className?: string | null;
};

export function CharacterGrid({ children }: { children: ReactNode }) {
  return <div className={styles.characterGrid}>{children}</div>;
}

export function SelectableCharacterCard({
  character,
  checked,
  hidden,
  onChange,
}: {
  character: Character;
  checked: boolean;
  hidden: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <label
      className={cx(
        "character-card",
        styles.characterCard,
        checked && "checked",
        checked && styles.checked,
        hidden && "hidden",
        hidden && styles.hidden,
      )}
      data-name={character.name}
      data-realm={character.realmDisplayName}
      data-level={character.level ?? 0}
      data-region={character.region}
    >
      <input
        type="checkbox"
        name="characters"
        value={character.key}
        checked={checked}
        onChange={(event) => onChange(event.currentTarget.checked)}
      />
      <Avatar
        name={character.name}
        renderUrl={character.renderUrl}
        classNameValue={character.className}
      />
      <div className={styles.characterInfo}>
        <div className={styles.characterName}>
          <ClassName
            name={character.name}
            classNameValue={character.className}
          />{" "}
          <ClassIcon classNameValue={character.className} />{" "}
          <LevelBadge character={character} />
        </div>
        <div className={styles.characterMeta}>
          {character.realmDisplayName} · {character.region.toUpperCase()}
        </div>
      </div>
      <div className={styles.checkIndicator}>✓</div>
    </label>
  );
}

export function ReadonlyCharacterCard({
  character,
  metaSuffix,
}: {
  character: CharacterCardData;
  metaSuffix?: ReactNode;
}) {
  return (
    <div
      className={cx(
        "character-card",
        "readonly",
        styles.characterCard,
        styles.readonly,
      )}
      data-name={character.name}
      data-realm={getCharacterRealm(character)}
      data-level={character.level ?? 0}
      data-region={character.region}
    >
      <Avatar
        name={character.name}
        renderUrl={character.renderUrl}
        classNameValue={character.className}
      />
      <div className={styles.characterInfo}>
        <div className={styles.characterName}>
          <ClassName
            name={character.name}
            classNameValue={character.className}
          />{" "}
          <ClassIcon classNameValue={character.className} />{" "}
          <LevelBadge character={character} />
        </div>
        <div className={styles.characterMeta}>
          {getCharacterRealm(character)} · {character.region.toUpperCase()}
          {metaSuffix ? <> · {metaSuffix}</> : null}
        </div>
      </div>
    </div>
  );
}

export function CharacterHomeRow({ character }: { character: Character }) {
  const lastChecked = formatLastChecked(character.lastCheckedAt);
  return (
    <div className={cx("character-row", styles.characterRow)}>
      <div className={styles.characterRowAvatar}>
        <Avatar
          name={character.name}
          renderUrl={character.renderUrl}
          classNameValue={character.className}
        />
      </div>
      <div className={styles.characterRowBody}>
        <div className={styles.characterRowIdentity}>
          <div className={styles.characterRowIdentityMain}>
            <div className={styles.characterRowName}>
              <ClassName
                name={character.name}
                classNameValue={character.className}
              />{" "}
              <ClassIcon classNameValue={character.className} />{" "}
              <LevelBadge character={character} />{" "}
              {character.isErroring ? (
                <span
                  className={styles.errorBadge}
                  title="Character not found on Raider.IO"
                >
                  ⚠
                </span>
              ) : null}{" "}
              {character.currentScore > 0 ? (
                <span className={styles.characterScorePlain}>
                  🏆 {character.currentScore.toFixed(0)}
                </span>
              ) : null}
            </div>
            <div className={styles.characterMeta}>
              {character.realmDisplayName} · {character.region.toUpperCase()}
            </div>
            {character.isErroring ? (
              <div className={cx(styles.characterStatus, styles.error)}>
                Not found on Raider.IO
              </div>
            ) : null}
          </div>
          {character.realm ? (
            <div className={styles.externalLinks}>
              <a
                className={styles.externalLink}
                href={getRaiderIoUrl(
                  character.name,
                  character.realm,
                  character.region,
                )}
                target="_blank"
                rel="noopener noreferrer"
                title="Open Raider.IO profile"
              >
                <img
                  src="https://cdn.raiderio.net/images/favicon-32x32.png"
                  alt="Raider.IO"
                  width={20}
                  height={20}
                  loading="lazy"
                />
              </a>
              <a
                className={styles.externalLink}
                href={getWowArmoryUrl(
                  character.name,
                  character.realm,
                  character.region,
                )}
                target="_blank"
                rel="noopener noreferrer"
                title="Open WoW Armory profile"
              >
                <img
                  src="https://assets-bwa.worldofwarcraft.blizzard.com/static/wow-icon-32x32.1a38d7c1c3d8df560d53f5c2ad5442c0401edf83.png"
                  alt="WoW Armory"
                  width={20}
                  height={20}
                  loading="lazy"
                />
              </a>
            </div>
          ) : null}
        </div>
        <div className={styles.dungeonSection}>
          <div className={styles.dungeonSectionLabel}>Best timed keys</div>
          {character.dungeonAchievements.length > 0 ? (
            <div className={styles.dungeonBadges}>
              {character.dungeonAchievements.map((dungeon) => (
                <div
                  className={styles.dungeonBadge}
                  title={`${dungeon.dungeonName} — +${dungeon.keyLevel}`}
                  key={dungeon.dungeonName}
                >
                  <span
                    className={cx(
                      styles.level,
                      styles[
                        dungeon.keyLevel >= 10
                          ? "high"
                          : dungeon.keyLevel >= 5
                            ? "med"
                            : "low"
                      ],
                    )}
                  >
                    +{dungeon.keyLevel}
                  </span>
                  <span className={styles.dungeonName}>
                    {getDungeonDisplayName(
                      dungeon.dungeonShortName,
                      dungeon.dungeonName,
                    )}
                  </span>
                </div>
              ))}
            </div>
          ) : (
            <div className={styles.dungeonEmpty}>
              No timed key achievements recorded yet.
            </div>
          )}
        </div>
        <div className={styles.characterRowFooter}>
          <span className={styles.lastChecked}>
            <span className={cx(styles.dot, styles[lastChecked.dotClass])} />{" "}
            {lastChecked.text}
          </span>
        </div>
      </div>
    </div>
  );
}

export function Avatar({
  name,
  renderUrl,
  classNameValue,
}: {
  name: string;
  renderUrl?: string | null;
  classNameValue?: string | null;
}) {
  const color = getClassColor(classNameValue) ?? getAvatarColor(name);
  const initials = name.length <= 2 ? name : name.slice(0, 2);

  if (!renderUrl) {
    return (
      <div
        className={styles.avatar}
        style={{ backgroundColor: color }}
        aria-label={name}
      >
        {initials}
      </div>
    );
  }

  return (
    <div className={styles.avatarWrapper}>
      <img
        src={renderUrl}
        alt=""
        className={styles.avatarImg}
        loading="lazy"
        onError={(event) => {
          event.currentTarget.style.display = "none";
          const fallback = event.currentTarget.nextElementSibling;
          if (fallback instanceof HTMLElement) fallback.style.display = "flex";
        }}
      />
      <div
        className={styles.avatarFallback}
        style={{ backgroundColor: color, display: "none" }}
      >
        {initials}
      </div>
    </div>
  );
}

export function ClassName({
  name,
  classNameValue,
}: {
  name: string;
  classNameValue?: string | null;
}) {
  const color = getClassColor(classNameValue);
  if (!color) return <>{name}</>;
  return (
    <span className={styles.className} style={{ color }}>
      {name}
    </span>
  );
}

export function ClassIcon({
  classNameValue,
}: {
  classNameValue?: string | null;
}) {
  const icon = getClassIcon(classNameValue);
  return icon ? (
    <img
      className={styles.classIcon}
      src={icon}
      alt={classNameValue ?? ""}
      title={classNameValue ?? ""}
      loading="lazy"
    />
  ) : null;
}

export function LevelBadge({ character }: { character: CharacterCardData }) {
  if (character.level == null) return null;
  return (
    <span
      className={cx(
        styles.levelBadge,
        character.maxLevel != null &&
          character.level >= character.maxLevel &&
          styles.levelMax,
      )}
    >
      {character.level}
    </span>
  );
}

function getCharacterRealm(character: CharacterCardData) {
  return character.realmDisplayName ?? character.realm ?? "Unknown realm";
}
