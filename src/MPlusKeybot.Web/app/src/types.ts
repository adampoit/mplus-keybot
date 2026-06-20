export type SessionResponse = {
  isAuthenticated: boolean;
  isDevelopment: boolean;
  homeUrl: string;
  signInUrl: string;
  signOutUrl: string;
  manageUrl: string;
  devUrl: string;
};

export type Character = {
  key: string;
  name: string;
  realmDisplayName: string;
  realm?: string | null;
  region: string;
  renderUrl?: string | null;
  level?: number | null;
  maxLevel: number;
  className?: string | null;
  followed: boolean;
  isErroring: boolean;
  currentScore: number;
  lastCheckedAt?: string | null;
  dungeonAchievements: DungeonAchievement[];
};

export type DungeonAchievement = {
  dungeonName: string;
  dungeonShortName?: string | null;
  dungeonSlug: string;
  keyLevel: number;
};

export type HomeResponse = {
  status: string;
  followedCharacters: Character[];
  otherCharacters: Character[];
  message?: string | null;
};

export type CharacterManagementResponse = {
  status: string;
  isAuthenticated: boolean;
  characters: Character[];
  form?: { verificationSetId: string; requestToken: string } | null;
  message?: string | null;
};

export type SaveCharactersResponse = {
  followed: SavedCharacter[];
  unfollowed: SavedCharacter[];
};

export type SavedCharacter = {
  key: string;
  name: string;
  realmDisplayName: string;
  realm?: string | null;
  region: string;
  renderUrl?: string | null;
  level?: number | null;
  maxLevel: number;
  className?: string | null;
};

export type DevToolsResponse = {
  followedCharacters: DevCharacter[];
};

export type DevCharacter = {
  name: string;
  realmDisplayName: string;
  region: string;
  renderUrl?: string | null;
  className?: string | null;
  lastCheckedText: string;
};
