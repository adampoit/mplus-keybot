import type { Character } from "./types";

type ClassMetadata = {
  color: string;
  iconSlug: string;
};

const classes: Record<string, ClassMetadata> = {
  "Death Knight": { color: "#C41F3B", iconSlug: "deathknight" },
  "Demon Hunter": { color: "#A330C9", iconSlug: "demonhunter" },
  Druid: { color: "#FF7D0A", iconSlug: "druid" },
  Evoker: { color: "#33937F", iconSlug: "evoker" },
  Hunter: { color: "#A9D271", iconSlug: "hunter" },
  Mage: { color: "#40C7EB", iconSlug: "mage" },
  Monk: { color: "#00FF96", iconSlug: "monk" },
  Paladin: { color: "#F58CBA", iconSlug: "paladin" },
  Priest: { color: "#FFFFFF", iconSlug: "priest" },
  Rogue: { color: "#FFF569", iconSlug: "rogue" },
  Shaman: { color: "#0070DE", iconSlug: "shaman" },
  Warlock: { color: "#8787ED", iconSlug: "warlock" },
  Warrior: { color: "#C79C6E", iconSlug: "warrior" },
};

const avatarColors = [
  "#e74c3c",
  "#e67e22",
  "#f1c40f",
  "#2ecc71",
  "#1abc9c",
  "#3498db",
  "#9b59b6",
  "#34495e",
  "#16a085",
  "#27ae60",
  "#2980b9",
  "#8e44ad",
  "#2c3e50",
  "#f39c12",
  "#d35400",
  "#c0392b",
  "#7f8c8d",
  "#2ecc71",
];

export function compareCardNames(a: Character, b: Character) {
  return a.name.localeCompare(b.name);
}

export function getClassColor(classNameValue?: string | null) {
  return getClassMetadata(classNameValue)?.color ?? null;
}

export function getClassIcon(classNameValue?: string | null) {
  const iconSlug = getClassMetadata(classNameValue)?.iconSlug;
  return iconSlug
    ? `https://wow.zamimg.com/images/wow/icons/small/class_${iconSlug}.jpg`
    : null;
}

export function getAvatarColor(name: string) {
  let hash = 0;
  for (const character of name.toLowerCase())
    hash = (hash * 31 + character.charCodeAt(0)) | 0;
  return avatarColors[Math.abs(hash) % avatarColors.length];
}

export function formatLastChecked(value?: string | null) {
  if (!value) return { text: "never", dotClass: "stale" };
  const elapsedMinutes = (Date.now() - new Date(value).getTime()) / 60000;
  if (elapsedMinutes < 10) return { text: "just now", dotClass: "ok" };
  if (elapsedMinutes < 60)
    return { text: `${Math.floor(elapsedMinutes)}m ago`, dotClass: "ok" };
  if (elapsedMinutes < 1440)
    return {
      text: `${Math.floor(elapsedMinutes / 60)}h ago`,
      dotClass: "warn",
    };
  return {
    text: `${Math.floor(elapsedMinutes / 1440)}d ago`,
    dotClass: "stale",
  };
}

export function getDungeonDisplayName(
  dungeonShortName: string | null | undefined,
  dungeonName: string,
) {
  return dungeonShortName?.trim() || dungeonName;
}

export function getRaiderIoUrl(name: string, realm: string, region: string) {
  return `https://raider.io/characters/${region.toLowerCase()}/${realm}/${name}`;
}

export function getWowArmoryUrl(name: string, realm: string, region: string) {
  return `https://worldofwarcraft.com/en-us/character/${region.toLowerCase()}/${realm}/${name}`;
}

function getClassMetadata(classNameValue?: string | null) {
  return classNameValue ? classes[classNameValue] : null;
}
