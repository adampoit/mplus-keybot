using SQLite;

namespace mplus_keybot.Tests;

public sealed class DatabaseMigrationsTests : IDisposable
{
	public DatabaseMigrationsTests()
	{
		m_databasePath = Path.Combine(Path.GetTempPath(), $"mplus-keybot-tests-{Guid.NewGuid():N}.db");
		m_db = new SQLiteConnection(m_databasePath);
		m_db.CreateTable<Character>();
		m_db.CreateTable<CharacterAchievementState>();
		m_db.CreateTable<CharacterDungeonAchievementState>();
	}

	[Fact]
	public void SeedsDungeonAchievementStateFromBestRunsNotRecentRuns()
	{
		var character = new Character { Name = "Aedrastorm", Realm = "Hyjal", Region = "US" };
		m_db.Insert(character);

		DatabaseMigrations.SeedAchievementState(m_db, character, new CharacterDto
		{
			Name = "Aedrastorm",
			Id = 123,
			Mythic_Plus_Recent_Runs =
			[
				CreateProfileRun("Windrunner Spire", 12, "https://raider.io/mythic-plus-runs/season-mn-1/30000000-12-windrunner-spire"),
			],
			Mythic_Plus_Best_Runs =
			[
				CreateProfileRun("Windrunner Spire", 15, "https://raider.io/mythic-plus-runs/season-mn-1/23357938-15-windrunner-spire"),
			],
			Mythic_Plus_Scores_By_Season =
			[
				new MythicPlusSeasonScoreDto
				{
					Season = "season-mn-1",
					Scores = new MythicPlusSeasonScoresDto { All = 3000 },
				},
			],
		});

		var state = m_db.Table<CharacterDungeonAchievementState>().Single(x => x.DungeonSlug == "windrunner-spire");
		Assert.Equal(15, state.HighestTimedKeyLevelSeen);
		Assert.Equal(15, state.HighestTimedKeyLevelAnnounced);
	}

	public void Dispose()
	{
		m_db.Dispose();
		File.Delete(m_databasePath);
	}

	private static MythicPlusProfileRunDto CreateProfileRun(string dungeon, int level, string url) => new()
	{
		Dungeon = dungeon,
		Mythic_Level = level,
		Clear_Time_Ms = 1,
		Par_Time_Ms = 2,
		Url = url,
		Completed_At = "2026-05-23T03:09:47.000Z",
	};

	private readonly string m_databasePath;
	private readonly SQLiteConnection m_db;
}
