using SQLite;

public static class DatabaseMigrations
{
	public static async Task RunAsync(SQLiteConnection db, RaiderIOClient raiderIOClient)
	{
		EnsureDungeonShortNameColumn(db);

		const string addCharacterFollowStateMigration = "2026-06-13-add-character-follow-state";
		if (!db.Table<DatabaseMigration>().Any(x => x.Name == addCharacterFollowStateMigration))
		{
			EnsureCharacterFollowColumns(db);
			db.Insert(new DatabaseMigration { Name = addCharacterFollowStateMigration, AppliedAt = DateTime.UtcNow });
		}
		else
		{
			EnsureCharacterFollowColumns(db);
		}

		const string addDungeonShortNameMigration = "2026-06-17-add-dungeon-short-name";
		if (!db.Table<DatabaseMigration>().Any(x => x.Name == addDungeonShortNameMigration))
		{
			EnsureDungeonShortNameColumn(db);
			await SeedDungeonAchievementStateForExistingCharactersAsync(db, raiderIOClient).ConfigureAwait(false);
			db.Insert(new DatabaseMigration { Name = addDungeonShortNameMigration, AppliedAt = DateTime.UtcNow });
		}
		else
		{
			EnsureDungeonShortNameColumn(db);
		}

		const string seedExistingAchievementStateMigration = "2026-05-16-seed-existing-achievement-state";
		if (!db.Table<DatabaseMigration>().Any(x => x.Name == seedExistingAchievementStateMigration))
		{
			await SeedAchievementStateForExistingCharactersAsync(db, raiderIOClient).ConfigureAwait(false);
			db.Insert(new DatabaseMigration { Name = seedExistingAchievementStateMigration, AppliedAt = DateTime.UtcNow });
		}

		const string dropAffixInfoMigration = "2026-05-17-drop-affix-info";
		if (!db.Table<DatabaseMigration>().Any(x => x.Name == dropAffixInfoMigration))
		{
			db.Execute("DROP TABLE IF EXISTS AffixInfo");
			db.Insert(new DatabaseMigration { Name = dropAffixInfoMigration, AppliedAt = DateTime.UtcNow });
		}

		const string seedDungeonAchievementStateMigration = "2026-05-23-seed-dungeon-achievement-state";
		if (!db.Table<DatabaseMigration>().Any(x => x.Name == seedDungeonAchievementStateMigration))
		{
			await SeedDungeonAchievementStateForExistingCharactersAsync(db, raiderIOClient).ConfigureAwait(false);
			db.Insert(new DatabaseMigration { Name = seedDungeonAchievementStateMigration, AppliedAt = DateTime.UtcNow });
		}

		const string removeOverallKeyAchievementStateMigration = "2026-05-23-remove-overall-key-achievement-state";
		if (!db.Table<DatabaseMigration>().Any(x => x.Name == removeOverallKeyAchievementStateMigration))
		{
			RemoveOverallKeyAchievementState(db);
			db.Insert(new DatabaseMigration { Name = removeOverallKeyAchievementStateMigration, AppliedAt = DateTime.UtcNow });
		}

		const string repairDungeonAchievementStateMigration = "2026-05-25-repair-dungeon-achievement-state-from-best-runs";
		if (!db.Table<DatabaseMigration>().Any(x => x.Name == repairDungeonAchievementStateMigration))
		{
			await SeedDungeonAchievementStateForExistingCharactersAsync(db, raiderIOClient).ConfigureAwait(false);
			db.Insert(new DatabaseMigration { Name = repairDungeonAchievementStateMigration, AppliedAt = DateTime.UtcNow });
		}

		const string addCharacterTrackingMigration = "2026-06-13-add-character-tracking";
		if (!db.Table<DatabaseMigration>().Any(x => x.Name == addCharacterTrackingMigration))
		{
			AddColumnIfMissing(db, "Character", "LastCheckedAt", "TEXT NULL");
			AddColumnIfMissing(db, "Character", "CurrentScore", "REAL NOT NULL DEFAULT 0");
			AddColumnIfMissing(db, "Character", "CurrentSeason", "TEXT NULL");
			AddColumnIfMissing(db, "Character", "Class", "TEXT NULL");
			if (TableExists(db, "VerifiedCharacterSession"))
				AddColumnIfMissing(db, "VerifiedCharacterSession", "Class", "TEXT NULL");
			db.Insert(new DatabaseMigration { Name = addCharacterTrackingMigration, AppliedAt = DateTime.UtcNow });
		}
		else
		{
			AddColumnIfMissing(db, "Character", "LastCheckedAt", "TEXT NULL");
			AddColumnIfMissing(db, "Character", "CurrentScore", "REAL NOT NULL DEFAULT 0");
			AddColumnIfMissing(db, "Character", "CurrentSeason", "TEXT NULL");
			AddColumnIfMissing(db, "Character", "Class", "TEXT NULL");
			if (TableExists(db, "VerifiedCharacterSession"))
				AddColumnIfMissing(db, "VerifiedCharacterSession", "Class", "TEXT NULL");
		}
	}

	public static void EnsureCharacterFollowColumns(SQLiteConnection db)
	{
		EnsureDungeonShortNameColumn(db);
		AddColumnIfMissing(db, "Character", "IsFollowed", "INTEGER NOT NULL DEFAULT 1");
		AddColumnIfMissing(db, "Character", "LastVerifiedAt", "TEXT NULL");
		AddColumnIfMissing(db, "Character", "LastManagedByDiscordUserId", "TEXT NULL");
		AddColumnIfMissing(db, "Character", "BlizzardCharacterId", "INTEGER NULL");
		AddColumnIfMissing(db, "Character", "RealmDisplayName", "TEXT NULL");
		AddColumnIfMissing(db, "Character", "LastCheckedAt", "TEXT NULL");
		AddColumnIfMissing(db, "Character", "CurrentScore", "REAL NOT NULL DEFAULT 0");
		AddColumnIfMissing(db, "Character", "CurrentSeason", "TEXT NULL");
		AddColumnIfMissing(db, "Character", "Class", "TEXT NULL");
		if (TableExists(db, "VerifiedCharacterSession"))
			AddColumnIfMissing(db, "VerifiedCharacterSession", "Class", "TEXT NULL");
		db.Execute("UPDATE Character SET IsFollowed = 1 WHERE IsFollowed IS NULL");
		NormalizeCharacterIdentities(db);
	}

	public static void SeedAchievementState(SQLiteConnection db, Character character, CharacterDto profile)
	{
		var insertedCharacter = db.Table<Character>().Single(x => x.Name == character.Name && x.Realm == character.Realm && x.Region == character.Region);
		var season = profile.CurrentMythicPlusSeason;
		if (season is null)
			return;

		var scoreMilestone = AchievementRules.GetHighestScoreMilestone(profile.CurrentMythicPlusScore);

		var state = db.Table<CharacterAchievementState>().FirstOrDefault(x => x.CharacterId == insertedCharacter.Id && x.Season == season);
		if (state is null)
		{
			state = new CharacterAchievementState
			{
				CharacterId = insertedCharacter.Id,
				Season = season,
				HighestScoreMilestoneAnnounced = scoreMilestone,
			};
			db.Insert(state);
		}
		else
		{
			state.HighestScoreMilestoneAnnounced = Math.Max(state.HighestScoreMilestoneAnnounced, scoreMilestone);
			db.Update(state);
		}

		SeedDungeonAchievementState(db, insertedCharacter, profile);

		foreach (var (category, _, lane, rank) in AchievementRules.GetSupportedRanks(profile))
		{
			var band = AchievementRules.GetRankBand(rank);
			if (band is null)
				continue;

			var rankingState = db.Table<CharacterRankingAchievementState>().FirstOrDefault(x => x.CharacterId == insertedCharacter.Id && x.Season == season && x.Category == category && x.Lane == lane);
			if (rankingState is null)
			{
				db.Insert(new CharacterRankingAchievementState
				{
					CharacterId = insertedCharacter.Id,
					Season = season,
					Category = category,
					Lane = lane,
					BestBandAnnounced = band.Value,
				});
			}
			else if (rankingState.BestBandAnnounced == 0 || band.Value < rankingState.BestBandAnnounced)
			{
				rankingState.BestBandAnnounced = band.Value;
				db.Update(rankingState);
			}
		}
	}

	public static void SeedDungeonAchievementState(SQLiteConnection db, Character character, CharacterDto profile)
	{
		var season = profile.CurrentMythicPlusSeason;
		if (season is null)
			return;

		foreach (var dungeonBest in profile.Mythic_Plus_Best_Runs
			.Where(x => x.Clear_Time_Ms <= x.Par_Time_Ms)
			.GroupBy(x => x.DungeonSlug)
			.Select(group => group.OrderByDescending(x => x.Mythic_Level).First()))
		{
			var state = db.Table<CharacterDungeonAchievementState>().FirstOrDefault(x => x.CharacterId == character.Id && x.Season == season && x.DungeonSlug == dungeonBest.DungeonSlug);
			if (state is null)
			{
				db.Insert(new CharacterDungeonAchievementState
				{
					CharacterId = character.Id,
					Season = season,
					DungeonSlug = dungeonBest.DungeonSlug,
					DungeonName = dungeonBest.Dungeon,
					DungeonShortName = dungeonBest.Short_Name,
					HighestTimedKeyLevelSeen = dungeonBest.Mythic_Level,
					HighestTimedKeyLevelAnnounced = dungeonBest.Mythic_Level,
				});
			}
			else
			{
				state.DungeonName = dungeonBest.Dungeon;
				state.DungeonShortName = dungeonBest.Short_Name ?? state.DungeonShortName;
				state.HighestTimedKeyLevelSeen = Math.Max(state.HighestTimedKeyLevelSeen, dungeonBest.Mythic_Level);
				state.HighestTimedKeyLevelAnnounced = Math.Max(state.HighestTimedKeyLevelAnnounced, dungeonBest.Mythic_Level);
				db.Update(state);
			}
		}
	}

	private static void NormalizeCharacterIdentities(SQLiteConnection db)
	{
		var groups = db.Table<Character>()
			.ToList()
			.GroupBy(GetNormalizedIdentity, NormalizedCharacterIdentityComparer.Instance);

		foreach (var group in groups)
		{
			var characters = group.OrderBy(x => x.Id).ToList();
			var survivor = characters.FirstOrDefault(IsAlreadyNormalized) ?? characters.First();

			foreach (var duplicate in characters.Where(x => x.Id != survivor.Id))
			{
				MergeAchievementState(db, survivor.Id, duplicate.Id);
				MergeDungeonAchievementState(db, survivor.Id, duplicate.Id);
				MergeRankingAchievementState(db, survivor.Id, duplicate.Id);
				MergeCharacterMetadata(survivor, duplicate);
				db.Delete(duplicate);
			}

			var identity = GetNormalizedIdentity(survivor);
			survivor.Name = identity.Name;
			survivor.Realm = identity.Realm;
			survivor.Region = identity.Region;
			db.Update(survivor);
		}
	}

	private static NormalizedCharacterIdentity GetNormalizedIdentity(Character character)
	{
		var key = CharacterKey.From(character.Region, character.Realm, character.Name);
		return new NormalizedCharacterIdentity(key.Region, key.Realm, key.Name, key.Name.ToLowerInvariant());
	}

	private static bool IsAlreadyNormalized(Character character)
	{
		var identity = GetNormalizedIdentity(character);
		return character.Name == identity.Name && character.Realm == identity.Realm && character.Region == identity.Region;
	}

	private static void MergeCharacterMetadata(Character target, Character source)
	{
		target.IsFollowed |= source.IsFollowed;
		target.ErroringSince = MergeErroringSince(target.ErroringSince, source.ErroringSince);
		target.LastVerifiedAt = MaxDate(target.LastVerifiedAt, source.LastVerifiedAt);
		if (source.LastVerifiedAt is not null && (target.LastVerifiedAt is null || source.LastVerifiedAt >= target.LastVerifiedAt))
			target.LastManagedByDiscordUserId = source.LastManagedByDiscordUserId ?? target.LastManagedByDiscordUserId;
		target.BlizzardCharacterId ??= source.BlizzardCharacterId;
		target.RealmDisplayName ??= source.RealmDisplayName;
		target.LastCheckedAt = MaxDate(target.LastCheckedAt, source.LastCheckedAt);
		if (source.LastCheckedAt is not null && (target.LastCheckedAt is null || source.LastCheckedAt >= target.LastCheckedAt))
		{
			target.CurrentScore = source.CurrentScore;
			target.CurrentSeason = source.CurrentSeason ?? target.CurrentSeason;
			target.Class = source.Class ?? target.Class;
		}
	}

	private static DateTime? MergeErroringSince(DateTime? target, DateTime? source)
	{
		if (target is null || source is null)
			return null;

		return target < source ? target : source;
	}

	private static DateTime? MaxDate(DateTime? target, DateTime? source)
	{
		if (target is null)
			return source;
		if (source is null)
			return target;
		return target > source ? target : source;
	}

	private static void MergeAchievementState(SQLiteConnection db, int targetCharacterId, int sourceCharacterId)
	{
		if (!TableExists(db, "CharacterAchievementState"))
			return;

		foreach (var source in db.Table<CharacterAchievementState>().Where(x => x.CharacterId == sourceCharacterId).ToList())
		{
			var target = db.Table<CharacterAchievementState>().FirstOrDefault(x => x.CharacterId == targetCharacterId && x.Season == source.Season);
			if (target is null)
			{
				source.CharacterId = targetCharacterId;
				db.Update(source);
				continue;
			}

			target.HighestScoreMilestoneAnnounced = Math.Max(target.HighestScoreMilestoneAnnounced, source.HighestScoreMilestoneAnnounced);
			db.Update(target);
			db.Delete(source);
		}
	}

	private static void MergeDungeonAchievementState(SQLiteConnection db, int targetCharacterId, int sourceCharacterId)
	{
		if (!TableExists(db, "CharacterDungeonAchievementState"))
			return;

		foreach (var source in db.Table<CharacterDungeonAchievementState>().Where(x => x.CharacterId == sourceCharacterId).ToList())
		{
			var target = db.Table<CharacterDungeonAchievementState>().FirstOrDefault(x => x.CharacterId == targetCharacterId && x.Season == source.Season && x.DungeonSlug == source.DungeonSlug);
			if (target is null)
			{
				source.CharacterId = targetCharacterId;
				db.Update(source);
				continue;
			}

			target.DungeonName = string.IsNullOrWhiteSpace(target.DungeonName) ? source.DungeonName : target.DungeonName;
			target.DungeonShortName = string.IsNullOrWhiteSpace(target.DungeonShortName) ? source.DungeonShortName : target.DungeonShortName;
			target.HighestTimedKeyLevelSeen = Math.Max(target.HighestTimedKeyLevelSeen, source.HighestTimedKeyLevelSeen);
			target.HighestTimedKeyLevelAnnounced = Math.Max(target.HighestTimedKeyLevelAnnounced, source.HighestTimedKeyLevelAnnounced);
			db.Update(target);
			db.Delete(source);
		}
	}

	private static void MergeRankingAchievementState(SQLiteConnection db, int targetCharacterId, int sourceCharacterId)
	{
		if (!TableExists(db, "CharacterRankingAchievementState"))
			return;

		foreach (var source in db.Table<CharacterRankingAchievementState>().Where(x => x.CharacterId == sourceCharacterId).ToList())
		{
			var target = db.Table<CharacterRankingAchievementState>().FirstOrDefault(x => x.CharacterId == targetCharacterId && x.Season == source.Season && x.Lane == source.Lane && x.Category == source.Category);
			if (target is null)
			{
				source.CharacterId = targetCharacterId;
				db.Update(source);
				continue;
			}

			target.BestBandAnnounced = MergeBestBand(target.BestBandAnnounced, source.BestBandAnnounced);
			db.Update(target);
			db.Delete(source);
		}
	}

	private static int MergeBestBand(int target, int source)
	{
		if (target == 0)
			return source;
		if (source == 0)
			return target;
		return Math.Min(target, source);
	}

	private static bool TableExists(SQLiteConnection db, string tableName) => db.ExecuteScalar<int>(
		"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?",
		tableName) > 0;

	private sealed record NormalizedCharacterIdentity(string Region, string Realm, string Name, string NameKey);

	private sealed class NormalizedCharacterIdentityComparer : IEqualityComparer<NormalizedCharacterIdentity>
	{
		public static readonly NormalizedCharacterIdentityComparer Instance = new();

		public bool Equals(NormalizedCharacterIdentity? x, NormalizedCharacterIdentity? y) =>
			x is not null && y is not null &&
			x.Region == y.Region &&
			x.Realm == y.Realm &&
			x.NameKey == y.NameKey;

		public int GetHashCode(NormalizedCharacterIdentity obj) => HashCode.Combine(obj.Region, obj.Realm, obj.NameKey);
	}

	private static void EnsureDungeonShortNameColumn(SQLiteConnection db)
	{
		if (TableExists(db, "CharacterDungeonAchievementState"))
			AddColumnIfMissing(db, "CharacterDungeonAchievementState", "DungeonShortName", "TEXT NULL");
	}

	private static void AddColumnIfMissing(SQLiteConnection db, string tableName, string columnName, string columnDefinition)
	{
		if (db.GetTableInfo(tableName).Any(x => string.Equals(x.Name, columnName, StringComparison.OrdinalIgnoreCase)))
			return;

		db.Execute($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}");
	}

	private static void RemoveOverallKeyAchievementState(SQLiteConnection db)
	{
		db.Execute("DROP INDEX IF EXISTS IX_CharacterAchievementState_Character_Season");
		db.Execute(@"
CREATE TABLE IF NOT EXISTS CharacterAchievementState_new (
	Id INTEGER PRIMARY KEY AUTOINCREMENT,
	CharacterId INTEGER NOT NULL,
	Season TEXT NOT NULL,
	HighestScoreMilestoneAnnounced INTEGER NOT NULL
)");
		db.Execute(@"
INSERT INTO CharacterAchievementState_new (Id, CharacterId, Season, HighestScoreMilestoneAnnounced)
SELECT Id, CharacterId, Season, HighestScoreMilestoneAnnounced
FROM CharacterAchievementState");
		db.Execute("DROP TABLE CharacterAchievementState");
		db.Execute("ALTER TABLE CharacterAchievementState_new RENAME TO CharacterAchievementState");
		db.Execute("CREATE UNIQUE INDEX IF NOT EXISTS IX_CharacterAchievementState_Character_Season ON CharacterAchievementState (CharacterId, Season)");
	}

	private static async Task SeedAchievementStateForExistingCharactersAsync(SQLiteConnection db, RaiderIOClient raiderIOClient)
	{
		foreach (var character in db.Table<Character>().Where(x => x.IsFollowed))
		{
			var profile = await raiderIOClient.GetCharacterAsync(character.Name, character.Realm, character.Region).ConfigureAwait(false);
			if (profile.IsFailure)
				continue;

			SeedAchievementState(db, character, profile.Result!);
		}
	}

	private static async Task SeedDungeonAchievementStateForExistingCharactersAsync(SQLiteConnection db, RaiderIOClient raiderIOClient)
	{
		foreach (var character in db.Table<Character>().Where(x => x.IsFollowed))
		{
			var profile = await raiderIOClient.GetCharacterAsync(character.Name, character.Realm, character.Region).ConfigureAwait(false);
			if (profile.IsFailure)
				continue;

			SeedDungeonAchievementState(db, character, profile.Result!);
		}
	}
}
