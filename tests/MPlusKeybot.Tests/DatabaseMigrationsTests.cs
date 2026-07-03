using MPlusKeybot.Api;
using MPlusKeybot.Api.Database;
using SQLite;

namespace MPlusKeybot.Tests;

public sealed class DatabaseMigrationsTests : IDisposable
{
	public DatabaseMigrationsTests()
	{
		m_databasePath = Path.Combine(Path.GetTempPath(), $"mplus-keybot-tests-{Guid.NewGuid():N}.db");
		m_db = new SQLiteConnection(m_databasePath);
		m_db.CreateTable<Character>();
		m_db.CreateTable<CharacterAchievementState>();
		m_db.CreateTable<CharacterDungeonAchievementState>();
		m_db.CreateTable<CharacterRankingAchievementState>();
	}

	[Fact]
	public void ExistingCharactersBecomeFollowedWhenFollowStateIsAdded()
	{
		var databasePath = Path.Combine(Path.GetTempPath(), $"mplus-keybot-old-schema-{Guid.NewGuid():N}.db");
		try
		{
			using var db = new SQLiteConnection(databasePath);
			db.Execute("CREATE TABLE Character (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Realm TEXT NOT NULL, Region TEXT NOT NULL, ErroringSince TEXT NULL)");
			db.Execute("INSERT INTO Character (Name, Realm, Region) VALUES (?, ?, ?)", "Aedrastorm", "hyjal", "us");

			DatabaseMigrations.EnsureCharacterFollowColumns(db);

			var character = db.Table<Character>().Single();
			Assert.True(character.IsFollowed);
		}
		finally
		{
			File.Delete(databasePath);
		}
	}

	[Fact]
	public void AddsClassColumnToOldSchema()
	{
		var databasePath = Path.Combine(Path.GetTempPath(), $"mplus-keybot-old-schema-{Guid.NewGuid():N}.db");
		try
		{
			using var db = new SQLiteConnection(databasePath);
			db.Execute("CREATE TABLE Character (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Realm TEXT NOT NULL, Region TEXT NOT NULL, ErroringSince TEXT NULL)");
			db.Execute("INSERT INTO Character (Name, Realm, Region) VALUES (?, ?, ?)", "Aedrastorm", "hyjal", "us");

			DatabaseMigrations.EnsureCharacterFollowColumns(db);

			var character = db.Table<Character>().Single();
			Assert.Null(character.Class);
		}
		finally
		{
			File.Delete(databasePath);
		}
	}

	[Fact]
	public void NormalizesLegacyCharacterIdentityAndMergesDuplicates()
	{
		var legacy = new Character { Name = "Keela", Realm = "Area 52", Region = "US", IsFollowed = true, LastCheckedAt = DateTime.UtcNow.AddHours(-1), CurrentScore = 2800, CurrentSeason = "season-a" };
		var canonical = new Character { Name = "Keela", Realm = "area-52", Region = "us", IsFollowed = false, BlizzardCharacterId = 123, RealmDisplayName = "Area 52" };
		m_db.Insert(legacy);
		m_db.Insert(canonical);
		legacy = m_db.Table<Character>().Single(x => x.Realm == "Area 52");
		canonical = m_db.Table<Character>().Single(x => x.Realm == "area-52");

		m_db.Insert(new CharacterAchievementState { CharacterId = legacy.Id, Season = "season-a", HighestScoreMilestoneAnnounced = 2500 });
		m_db.Insert(new CharacterAchievementState { CharacterId = canonical.Id, Season = "season-a", HighestScoreMilestoneAnnounced = 2000 });
		m_db.Insert(new CharacterDungeonAchievementState { CharacterId = legacy.Id, Season = "season-a", DungeonSlug = "windrunner-spire", DungeonName = "Windrunner Spire", HighestTimedKeyLevelSeen = 12, HighestTimedKeyLevelAnnounced = 10 });
		m_db.Insert(new CharacterDungeonAchievementState { CharacterId = canonical.Id, Season = "season-a", DungeonSlug = "windrunner-spire", DungeonName = "Windrunner Spire", HighestTimedKeyLevelSeen = 15, HighestTimedKeyLevelAnnounced = 14 });
		m_db.Insert(new CharacterRankingAchievementState { CharacterId = legacy.Id, Season = "season-a", Lane = "all", Category = "world", BestBandAnnounced = 100 });
		m_db.Insert(new CharacterRankingAchievementState { CharacterId = canonical.Id, Season = "season-a", Lane = "all", Category = "world", BestBandAnnounced = 1000 });

		DatabaseMigrations.EnsureCharacterFollowColumns(m_db);

		var character = m_db.Table<Character>().Single();
		Assert.Equal("Keela", character.Name);
		Assert.Equal("area-52", character.Realm);
		Assert.Equal("us", character.Region);
		Assert.True(character.IsFollowed);
		Assert.Equal(123, character.BlizzardCharacterId);
		Assert.Equal("Area 52", character.RealmDisplayName);
		Assert.Equal(2800, character.CurrentScore);

		var scoreState = m_db.Table<CharacterAchievementState>().Single();
		Assert.Equal(character.Id, scoreState.CharacterId);
		Assert.Equal(2500, scoreState.HighestScoreMilestoneAnnounced);

		var dungeonState = m_db.Table<CharacterDungeonAchievementState>().Single();
		Assert.Equal(character.Id, dungeonState.CharacterId);
		Assert.Equal(15, dungeonState.HighestTimedKeyLevelSeen);
		Assert.Equal(14, dungeonState.HighestTimedKeyLevelAnnounced);

		var rankingState = m_db.Table<CharacterRankingAchievementState>().Single();
		Assert.Equal(character.Id, rankingState.CharacterId);
		Assert.Equal(100, rankingState.BestBandAnnounced);
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
		Assert.Equal("WS", state.DungeonShortName);
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
		Short_Name = "WS",
		Mythic_Level = level,
		Clear_Time_Ms = 1,
		Par_Time_Ms = 2,
		Url = url,
		Completed_At = "2026-05-23T03:09:47.000Z",
	};

	private readonly string m_databasePath;
	private readonly SQLiteConnection m_db;
}
