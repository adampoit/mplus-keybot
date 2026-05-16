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
	}

	public static void SeedAchievementState(SQLiteConnection db, Character character, CharacterDto profile)
	{
		var insertedCharacter = db.Table<Character>().Single(x => x.Name == character.Name && x.Realm == character.Realm && x.Region == character.Region);
		var season = profile.CurrentMythicPlusSeason;
		if (season is null)
			return;

		var highestRecentKey = profile.Mythic_Plus_Recent_Runs.Count == 0 ? 0 : profile.Mythic_Plus_Recent_Runs.Max(x => x.Mythic_Level);
		var scoreMilestone = AchievementRules.GetHighestScoreMilestone(profile.CurrentMythicPlusScore);

		var state = db.Table<CharacterAchievementState>().FirstOrDefault(x => x.CharacterId == insertedCharacter.Id && x.Season == season);
		if (state is null)
		{
			state = new CharacterAchievementState
			{
				CharacterId = insertedCharacter.Id,
				Season = season,
				HighestKeyLevelSeen = highestRecentKey,
				HighestKeyLevelAnnounced = highestRecentKey,
				HighestScoreMilestoneAnnounced = scoreMilestone,
			};
			db.Insert(state);
		}
		else
		{
			state.HighestKeyLevelSeen = Math.Max(state.HighestKeyLevelSeen, highestRecentKey);
			state.HighestKeyLevelAnnounced = Math.Max(state.HighestKeyLevelAnnounced, highestRecentKey);
			state.HighestScoreMilestoneAnnounced = Math.Max(state.HighestScoreMilestoneAnnounced, scoreMilestone);
			db.Update(state);
		}

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
}
