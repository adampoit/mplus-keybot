using System.Diagnostics;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;
using SQLite;

public sealed class CheckRunsJob : IJob
{
	public CheckRunsJob(ILogger<CheckRunsJob> logger, DiscordSocketClient discordClient, RaiderIOClient raiderIOClient, SQLiteConnection db, IConfiguration config)
	{
		m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		m_discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
		m_raiderIOClient = raiderIOClient ?? throw new ArgumentNullException(nameof(raiderIOClient));
		m_db = db ?? throw new ArgumentNullException(nameof(db));
		m_discordChannel = config["Discord:Channel"]!;
	}

	public async Task Execute(IJobExecutionContext context)
	{
		var stopwatch = Stopwatch.StartNew();
		m_logger.LogInformation($"Starting {nameof(CheckRunsJob)} job.");

		var guild = m_discordClient.Guilds.Single();
		var channel = guild.Channels.Single(c => c.Name == m_discordChannel) as IMessageChannel;

		var characterProfiles = new Dictionary<int, CharacterDto>();
		var runs = new HashSet<MythicPlusRun>();
		foreach (var character in m_db.Table<Character>())
		{
			var profile = await m_raiderIOClient.GetCharacterAsync(character.Name, character.Realm, character.Region).ConfigureAwait(false);
			if (profile.IsFailure)
			{
				if (profile.Error == ErrorResult.CharacterNotFound)
				{
					character.ErroringSince ??= DateTime.UtcNow;
					if (character.ErroringSince < DateTime.UtcNow - TimeSpan.FromHours(24))
					{
						await channel!.SendMessageAsync($"Unfollowing {character.Name} on {character.Realm}-{character.Region}! Could not access profile for over 24 hours.").ConfigureAwait(false);
						m_db.Delete(character);
					}
					else
					{
						m_db.Update(character);
					}

					continue;
				}
				else
				{
					character.ErroringSince = null;
				}

				continue;
			}

			character.ErroringSince = null;
			m_db.Update(character);
			characterProfiles[character.Id] = profile.Result!;
			await AnnounceCharacterAchievementsAsync(channel!, character, profile.Result!).ConfigureAwait(false);

			runs.UnionWith(profile.Result!.Mythic_Plus_Recent_Runs
				.Select(run => new MythicPlusRun { Id = run.RunId, Date = DateTimeOffset.Parse(run.Completed_At) })
				.Where(run => !m_db.Table<MythicPlusRun>().Any(x => x.Id == run.Id)));
		}

		foreach (var run in runs.OrderBy(x => x.Date))
		{
			var runInfo = await m_raiderIOClient.GetMythicPlusRunAsync(run.Id).ConfigureAwait(false);
			if (runInfo.IsFailure)
				continue;

			var keystoneRun = runInfo.Result!.KeystoneRun;
			var percentage = (double)keystoneRun.Clear_Time_Ms / (double)keystoneRun.Keystone_Time_Ms;
			var percentageString = percentage < 1 ? $"{1 - percentage:P1} remaining" : $"{percentage - 1:P1} over";

			var rosterString = string.Join(Environment.NewLine, keystoneRun.Roster
				.OrderBy(r => r.Role)
				.Select(r => $"{GetRoleEmoji(r.Role)} [{r.Character.Name.Split('-')[0]}](https://raider.io{r.Character.Path}) - **{r.Role}** ({r.Character.Spec.Name} {r.Character.Class.Name}) - {r.Ranks.Score:0} Score"));

			var embed = new EmbedBuilder()
				.WithFooter(footer => footer.Text = "Data provided by Raider.IO")
				.WithTitle($"+{keystoneRun.Mythic_Level} {keystoneRun.Dungeon.Name}")
				.WithColor(Color.Gold)
				.WithDescription($@"Cleared in {TimeSpan.FromMilliseconds(keystoneRun.Clear_Time_Ms):mm':'ss} of {TimeSpan.FromMilliseconds(keystoneRun.Keystone_Time_Ms):mm':'ss} ({percentageString}).{Environment.NewLine}{Environment.NewLine}{rosterString}")
				.WithUrl($"https://raider.io/mythic-plus-runs/{run.Id}")
				.WithImageUrl($"https://cdnassets.raider.io/images/dungeons/expansion{keystoneRun.Dungeon.Expansion_Id}/base/{keystoneRun.Dungeon.Slug}.jpg")
				.WithTimestamp(run.Date);

			var runAchievements = GetRunAchievements(keystoneRun, characterProfiles);
			if (runAchievements.Count > 0)
				embed.AddField("Achievements", string.Join(Environment.NewLine, runAchievements));

			await channel!.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);

			foreach (var achievement in runAchievements.OfType<PersonalBestRunAchievement>())
			{
				achievement.State.HighestKeyLevelSeen = keystoneRun.Mythic_Level;
				achievement.State.HighestKeyLevelAnnounced = keystoneRun.Mythic_Level;
				m_db.Update(achievement.State);
			}

			m_db.Insert(run, "OR IGNORE");
		}

		m_logger.LogInformation($"Finished {nameof(CheckRunsJob)} job after {stopwatch.Elapsed}.");
	}

	private async Task AnnounceCharacterAchievementsAsync(IMessageChannel channel, Character character, CharacterDto profile)
	{
		var season = profile.CurrentMythicPlusSeason;
		if (season is null)
			return;

		var state = GetOrCreateAchievementState(character, season);
		var scoreMilestone = AchievementRules.GetHighestNewScoreMilestone(profile.CurrentMythicPlusScore, state.HighestScoreMilestoneAnnounced);
		if (scoreMilestone is not null)
		{
			var embed = new EmbedBuilder()
				.WithFooter(footer => footer.Text = "Data provided by Raider.IO")
				.WithTitle($"🌟 {scoreMilestone.Name}")
				.WithColor(Color.Gold)
				.WithDescription($"{character.Name} crossed **{scoreMilestone.Score} Mythic+ rating**.")
				.WithCurrentTimestamp();

			await channel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
			state.HighestScoreMilestoneAnnounced = scoreMilestone.Score;
			m_db.Update(state);
		}

		foreach (var (category, categoryLabel, lane, rank) in AchievementRules.GetSupportedRanks(profile))
		{
			var band = AchievementRules.GetRankBand(rank);
			if (band is null)
				continue;

			var rankingState = GetOrCreateRankingAchievementState(character, season, lane, category);
			if (rankingState.BestBandAnnounced != 0 && rankingState.BestBandAnnounced <= band.Value)
				continue;

			var embed = new EmbedBuilder()
				.WithFooter(footer => footer.Text = "Data provided by Raider.IO")
				.WithTitle($"👑 {AchievementRules.FormatLane(lane)} Ranking")
				.WithColor(Color.Gold)
				.WithDescription($"{character.Name} entered the **Top {band.Value} {AchievementRules.FormatLane(lane)} {AchievementRules.FormatRankCategory(category, categoryLabel)}** rankings.")
				.WithCurrentTimestamp();

			await channel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
			rankingState.BestBandAnnounced = band.Value;
			m_db.Update(rankingState);
		}
	}

	private List<PersonalBestRunAchievement> GetRunAchievements(MythicPlusKeystoneRunDto run, IReadOnlyDictionary<int, CharacterDto> characterProfiles)
	{
		var achievements = new List<PersonalBestRunAchievement>();
		if (run.Mythic_Level < AchievementRules.MinimumPersonalBestAnnouncementLevel)
			return achievements;

		foreach (var character in m_db.Table<Character>())
		{
			if (!characterProfiles.TryGetValue(character.Id, out var profile))
				continue;

			var season = profile.CurrentMythicPlusSeason;
			if (season is null)
				continue;

			if (!run.Roster.Any(rosterMember => IsSameCharacter(rosterMember.Character, character)))
				continue;

			var state = GetOrCreateAchievementState(character, season);
			if (run.Mythic_Level <= state.HighestKeyLevelSeen)
				continue;

			achievements.Add(new PersonalBestRunAchievement(character.Name, run.Mythic_Level, state));
		}

		return achievements;
	}

	private CharacterAchievementState GetOrCreateAchievementState(Character character, string season)
	{
		var state = m_db.Table<CharacterAchievementState>().FirstOrDefault(x => x.CharacterId == character.Id && x.Season == season);
		if (state is not null)
			return state;

		state = new CharacterAchievementState { CharacterId = character.Id, Season = season };
		m_db.Insert(state);
		return state;
	}

	private CharacterRankingAchievementState GetOrCreateRankingAchievementState(Character character, string season, string lane, string category)
	{
		var state = m_db.Table<CharacterRankingAchievementState>().FirstOrDefault(x => x.CharacterId == character.Id && x.Season == season && x.Lane == lane && x.Category == category);
		if (state is not null)
			return state;

		state = new CharacterRankingAchievementState { CharacterId = character.Id, Season = season, Lane = lane, Category = category };
		m_db.Insert(state);
		return state;
	}

	private static bool IsSameCharacter(RosterCharacterDto rosterCharacter, Character followedCharacter)
	{
		var characterName = rosterCharacter.Name.Split('-')[0];
		var pathParts = rosterCharacter.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
		var region = pathParts.Length >= 2 ? pathParts[1] : null;
		var realm = pathParts.Length >= 3 ? pathParts[2] : null;

		return string.Equals(characterName, followedCharacter.Name, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(region, followedCharacter.Region, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(NormalizeRealmSlug(realm), NormalizeRealmSlug(followedCharacter.Realm), StringComparison.OrdinalIgnoreCase);
	}

	private static string? NormalizeRealmSlug(string? realm) => realm?.Replace(' ', '-');

	private static string GetRoleEmoji(Role role) => role switch
	{
		Role.Tank => "🛡️",
		Role.Healer => "💉",
		Role.Dps => "⚔️",
		_ => throw new InvalidOperationException($"No emoji found for role {role}!"),
	};

	private sealed record PersonalBestRunAchievement(string CharacterName, int KeyLevel, CharacterAchievementState State)
	{
		public override string ToString() => $"🏆 {CharacterName} set a new personal best: **+{KeyLevel}**";
	}

	private readonly ILogger<CheckRunsJob> m_logger;
	private readonly DiscordSocketClient m_discordClient;
	private readonly RaiderIOClient m_raiderIOClient;
	private readonly SQLiteConnection m_db;
	private readonly string m_discordChannel;
}
