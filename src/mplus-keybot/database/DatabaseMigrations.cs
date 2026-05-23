using SQLite;

public static class DatabaseMigrations
{
	public static async Task RunAsync(SQLiteConnection db, RaiderIOClient raiderIOClient)
	{
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

		foreach (var dungeonBest in profile.Mythic_Plus_Recent_Runs
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
					HighestTimedKeyLevelSeen = dungeonBest.Mythic_Level,
					HighestTimedKeyLevelAnnounced = dungeonBest.Mythic_Level,
				});
			}
			else
			{
				state.DungeonName = dungeonBest.Dungeon;
				state.HighestTimedKeyLevelSeen = Math.Max(state.HighestTimedKeyLevelSeen, dungeonBest.Mythic_Level);
				state.HighestTimedKeyLevelAnnounced = Math.Max(state.HighestTimedKeyLevelAnnounced, dungeonBest.Mythic_Level);
				db.Update(state);
			}
		}
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
		foreach (var character in db.Table<Character>())
		{
			var profile = await raiderIOClient.GetCharacterAsync(character.Name, character.Realm, character.Region).ConfigureAwait(false);
			if (profile.IsFailure)
				continue;

			SeedAchievementState(db, character, profile.Result!);
		}
	}

	private static async Task SeedDungeonAchievementStateForExistingCharactersAsync(SQLiteConnection db, RaiderIOClient raiderIOClient)
	{
		foreach (var character in db.Table<Character>())
		{
			var profile = await raiderIOClient.GetCharacterAsync(character.Name, character.Realm, character.Region).ConfigureAwait(false);
			if (profile.IsFailure)
				continue;

			SeedDungeonAchievementState(db, character, profile.Result!);
		}
	}
}
