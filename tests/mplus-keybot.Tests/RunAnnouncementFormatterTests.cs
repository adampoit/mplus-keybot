using Discord;

namespace mplus_keybot.Tests;

public sealed class RunAnnouncementFormatterTests
{
	[Fact]
	public void BuildsRunEmbedWithPersonalBestAchievements()
	{
		var announcement = MythicPlusRunAnnouncement.From(
			"season-mn-1/12345-16-magisters-terrace",
			CreateRun(),
			["Soryan", "Aedrastorm"],
			[]);

		var embed = RunAnnouncementFormatter.BuildEmbed(announcement);

		Assert.EndsWith($"{Environment.NewLine}{Environment.NewLine}🏆 New personal best: Aedrastorm, Soryan", embed.Description);
	}

	[Fact]
	public void BuildsRunEmbedWithSeasonHighAchievements()
	{
		var announcement = MythicPlusRunAnnouncement.From(
			"season-mn-1/12345-16-magisters-terrace",
			CreateRun(),
			[],
			["Soryan"]);

		var embed = RunAnnouncementFormatter.BuildEmbed(announcement);

		Assert.EndsWith($"{Environment.NewLine}{Environment.NewLine}🔥 First +16 this season: Soryan", embed.Description);
	}

	[Fact]
	public void BuildsRunEmbedWithPersonalBestBeforeSeasonHigh()
	{
		var announcement = MythicPlusRunAnnouncement.From(
			"season-mn-1/12345-16-magisters-terrace",
			CreateRun(),
			["Soryan", "Aedrastorm"],
			["Soryan"]);

		var embed = RunAnnouncementFormatter.BuildEmbed(announcement);

		Assert.EndsWith($"{Environment.NewLine}{Environment.NewLine}🏆 New personal best: Aedrastorm, Soryan{Environment.NewLine}🔥 First +16 this season: Soryan", embed.Description);
	}

	[Fact]
	public void BuildsRunEmbedProperties()
	{
		var announcement = MythicPlusRunAnnouncement.From(
			"season-mn-1/12345-16-magisters-terrace",
			CreateRun(),
			["Soryan", "Aedrastorm"],
			["Soryan"]);

		var embed = RunAnnouncementFormatter.BuildEmbed(announcement);

		Assert.Equal("+16 Magisters' Terrace", embed.Title);
		Assert.Equal("https://raider.io/mythic-plus-runs/season-mn-1/12345-16-magisters-terrace", embed.Url);
		Assert.Equal("https://cdnassets.raider.io/images/dungeons/expansion11/base/magisters-terrace.jpg", embed.Image?.Url);
		Assert.Equal("Data provided by Raider.IO", embed.Footer?.Text);
		Assert.Equal(Color.Gold, embed.Color);
		Assert.Equal(new DateTimeOffset(2026, 6, 10, 21, 37, 0, TimeSpan.Zero), embed.Timestamp);
		Assert.Equal($"Cleared in 29:10 of 34:00 (14.2% remaining).{Environment.NewLine}{Environment.NewLine}🛡️ [Soryan](https://raider.io/characters/us/hyjal/Soryan) - **Tank** (Brewmaster Monk) - 3214 Score{Environment.NewLine}💉 [Aedrastorm](https://raider.io/characters/us/hyjal/Aedrastorm) - **Healer** (Restoration Shaman) - 3382 Score{Environment.NewLine}{Environment.NewLine}🏆 New personal best: Aedrastorm, Soryan{Environment.NewLine}🔥 First +16 this season: Soryan", embed.Description);
	}

	[Fact]
	public void BuildsDepletedRunEmbedWithRedColor()
	{
		var depletedRun = CreateRun();
		depletedRun.Clear_Time_Ms = 2_050_000;
		var announcement = MythicPlusRunAnnouncement.From(
			"season-mn-1/12345-16-magisters-terrace",
			depletedRun,
			[],
			[]);

		var embed = RunAnnouncementFormatter.BuildEmbed(announcement);

		Assert.Equal(Color.Red, embed.Color);
		Assert.StartsWith("Cleared in 34:10 of 34:00 (0.5% over).", embed.Description);
	}

	private static MythicPlusKeystoneRunDto CreateRun() => new()
	{
		Mythic_Level = 16,
		Clear_Time_Ms = 1_750_000,
		Keystone_Time_Ms = 2_040_000,
		Completed_At = "2026-06-10T21:37:00.000Z",
		Dungeon = new DungeonDto
		{
			Name = "Magisters' Terrace",
			Slug = "magisters-terrace",
			Expansion_Id = 11,
		},
		Roster =
		[
			new RosterMemberDto
			{
				Role = Role.Healer,
				Ranks = new MythicPlusScoreDto { Score = 3382 },
				Character = new RosterCharacterDto
				{
					Name = "Aedrastorm",
					Path = "/characters/us/hyjal/Aedrastorm",
					Class = new ClassDto { Name = "Shaman" },
					Spec = new SpecDto { Name = "Restoration" },
				},
			},
			new RosterMemberDto
			{
				Role = Role.Tank,
				Ranks = new MythicPlusScoreDto { Score = 3214 },
				Character = new RosterCharacterDto
				{
					Name = "Soryan",
					Path = "/characters/us/hyjal/Soryan",
					Class = new ClassDto { Name = "Monk" },
					Spec = new SpecDto { Name = "Brewmaster" },
				},
			},
		],
	};
}
