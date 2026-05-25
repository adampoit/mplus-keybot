namespace mplus_keybot.Tests;

public sealed class RunAchievementDetectorTests
{
	[Fact]
	public void DoesNotAnnounceDepletedRuns()
	{
		var run = CreateRun(level: 16, dungeonSlug: "seat-of-the-triumvirate", clearTimeMs: 2_100_000, timerMs: 2_040_999);

		var achievements = Detect(run, existingBest: 15);

		Assert.Empty(achievements);
	}

	[Fact]
	public void AnnouncesTimedRunAboveDungeonBest()
	{
		var run = CreateRun(level: 16, dungeonSlug: "seat-of-the-triumvirate", clearTimeMs: 1_980_000, timerMs: 2_040_999);

		var achievement = Assert.Single(Detect(run, existingBest: 15));

		Assert.Equal("Aedrastorm", achievement.CharacterName);
		Assert.Equal("Seat Of The Triumvirate", achievement.DungeonName);
		Assert.Equal(16, achievement.KeyLevel);
	}

	[Fact]
	public void DoesNotAnnounceTimedRunAtOrBelowDungeonBest()
	{
		var run = CreateRun(level: 16, dungeonSlug: "seat-of-the-triumvirate", clearTimeMs: 1_980_000, timerMs: 2_040_999);

		var achievements = Detect(run, existingBest: 16);

		Assert.Empty(achievements);
	}

	[Fact]
	public void TracksPersonalBestsPerDungeon()
	{
		var run = CreateRun(level: 16, dungeonSlug: "seat-of-the-triumvirate", clearTimeMs: 1_980_000, timerMs: 2_040_999);
		var states = new Dictionary<string, CharacterDungeonAchievementState>
		{
			["seat-of-the-triumvirate"] = new() { DungeonSlug = "seat-of-the-triumvirate", HighestTimedKeyLevelSeen = 15 },
			["magisters-terrace"] = new() { DungeonSlug = "magisters-terrace", HighestTimedKeyLevelSeen = 16 },
		};

		var achievement = Assert.Single(RunAchievementDetector.GetPersonalBestAchievements(
			run,
			[CreateCharacter()],
			CreateProfiles(),
			(_, _, dungeon) => states[dungeon.Slug]));

		Assert.Equal("Seat Of The Triumvirate", achievement.DungeonName);
	}

	[Theory]
	[InlineData("https://raider.io/mythic-plus-runs/season-mn-1/28913174-16-seat-of-the-triumvirate", "seat-of-the-triumvirate")]
	[InlineData("https://raider.io/mythic-plus-runs/season-mn-1/28919612-15-magisters-terrace", "magisters-terrace")]
	public void ParsesDungeonSlugFromRecentRunUrl(string url, string expectedSlug)
	{
		var recentRun = new MythicPlusProfileRunDto
		{
			Dungeon = "ignored",
			Mythic_Level = 16,
			Clear_Time_Ms = 1,
			Par_Time_Ms = 2,
			Url = url,
			Completed_At = "2026-05-23T03:09:47.000Z",
		};

		Assert.Equal(expectedSlug, recentRun.DungeonSlug);
	}

	private static IReadOnlyList<RunPersonalBestAchievement> Detect(MythicPlusKeystoneRunDto run, int existingBest) => RunAchievementDetector.GetPersonalBestAchievements(
		run,
		[CreateCharacter()],
		CreateProfiles(),
		(_, _, dungeon) => new CharacterDungeonAchievementState { DungeonSlug = dungeon.Slug, HighestTimedKeyLevelSeen = existingBest });

	private static Character CreateCharacter() => new()
	{
		Id = 1,
		Name = "Aedrastorm",
		Realm = "Hyjal",
		Region = "US",
	};

	private static IReadOnlyDictionary<int, CharacterDto> CreateProfiles() => new Dictionary<int, CharacterDto>
	{
		[1] = new CharacterDto
		{
			Name = "Aedrastorm",
			Id = 123,
			Mythic_Plus_Recent_Runs = [],
			Mythic_Plus_Scores_By_Season =
			[
				new MythicPlusSeasonScoreDto
				{
					Season = "season-mn-1",
					Scores = new MythicPlusSeasonScoresDto { All = 3000 },
				},
			],
		},
	};

	private static MythicPlusKeystoneRunDto CreateRun(int level, string dungeonSlug, int clearTimeMs, int timerMs) => new()
	{
		Mythic_Level = level,
		Clear_Time_Ms = clearTimeMs,
		Keystone_Time_Ms = timerMs,
		Completed_At = "2026-05-23T03:09:47.000Z",
		Dungeon = new DungeonDto
		{
			Name = string.Join(' ', dungeonSlug.Split('-').Select(x => char.ToUpperInvariant(x[0]) + x[1..])),
			Slug = dungeonSlug,
			Expansion_Id = 11,
		},
		Roster =
		[
			new RosterMemberDto
			{
				Role = Role.Tank,
				Ranks = new MythicPlusScoreDto { Score = 3000 },
				Character = new RosterCharacterDto
				{
					Name = "Aedrastorm",
					Path = "/characters/us/hyjal/Aedrastorm",
					Class = new ClassDto { Name = "Demon Hunter" },
					Spec = new SpecDto { Name = "Vengeance" },
				},
			},
		],
	};
}
