using System.Diagnostics;
using System.Globalization;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MPlusKeybot.Api.Database;
using MPlusKeybot.Api.Services;
using Quartz;
using SQLite;

namespace MPlusKeybot.Api.Jobs;

public sealed class CheckRunsJob(
	ILogger<CheckRunsJob> logger,
	DiscordSocketClient discordClient,
	RaiderIOClient raiderIOClient,
	SQLiteConnection db,
	CharacterRepository characters,
	IConfiguration config,
	WebUrlBuilder urls) : IJob
{
	public const string JobName = "CheckRunsJob";
	public const string RecurringTriggerName = "Every 5 Minutes";

	public async Task Execute(IJobExecutionContext context)
	{
		var stopwatch = Stopwatch.StartNew();
		LogJobStarting(m_logger);

		var channel = GetAnnouncementChannel();
		if (channel is null)
			LogNoAnnouncementChannel(m_logger);

		var characterProfiles = new Dictionary<int, CharacterDto>();
		var followedCharacters = m_characters.GetFollowedCharacters();
		var runs = new HashSet<MythicPlusRun>();
		foreach (var character in followedCharacters)
		{
			var profile = await m_raiderIOClient.GetCharacterAsync(character.Name, character.Realm, character.Region).ConfigureAwait(false);
			if (profile.IsFailure)
			{
				if (profile.Error == ErrorResult.CharacterNotFound)
				{
					character.ErroringSince ??= DateTime.UtcNow;
					m_db.Update(character);
					continue;
				}
				else
				{
					character.ErroringSince = null;
					m_db.Update(character);
				}

				continue;
			}

			character.ErroringSince = null;
			character.LastCheckedAt = DateTime.UtcNow;
			character.CurrentScore = profile.Result!.CurrentMythicPlusScore;
			character.CurrentSeason = profile.Result!.CurrentMythicPlusSeason;
			character.Class = profile.Result!.Class ?? character.Class;
			m_db.Update(character);
			characterProfiles[character.Id] = profile.Result!;

			if (channel is not null)
				await AnnounceCharacterAchievementsAsync(channel, character, profile.Result!).ConfigureAwait(false);

			runs.UnionWith(profile.Result!.Mythic_Plus_Recent_Runs
				.Select(run => new MythicPlusRun { Id = run.RunId, Date = DateTimeOffset.Parse(run.Completed_At, CultureInfo.InvariantCulture) })
				.Where(run => !m_db.Table<MythicPlusRun>().Any(x => x.Id == run.Id)));
		}

		foreach (var run in runs.OrderBy(x => x.Date))
		{
			var runInfo = await m_raiderIOClient.GetMythicPlusRunAsync(run.Id).ConfigureAwait(false);
			if (runInfo.IsFailure)
				continue;

			var keystoneRun = runInfo.Result!.KeystoneRun;
			var personalBestAchievements = GetRunAchievements(keystoneRun, followedCharacters, characterProfiles);
			var seasonHighAchievements = GetSeasonHighAchievements(keystoneRun, followedCharacters, characterProfiles);
			if (channel is not null)
			{
				var announcement = MythicPlusRunAnnouncement.From(
					run.Id,
					keystoneRun,
					personalBestAchievements.Select(x => x.CharacterName),
					seasonHighAchievements.Select(x => x.CharacterName));
				var embed = RunAnnouncementFormatter.BuildEmbed(announcement, m_urls.PublicBaseUrl);

				await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
			}

			foreach (var achievement in personalBestAchievements)
			{
				achievement.State.DungeonName = keystoneRun.Dungeon.Name;
				achievement.State.DungeonShortName = keystoneRun.Dungeon.Short_Name ?? achievement.State.DungeonShortName;
				achievement.State.HighestTimedKeyLevelSeen = keystoneRun.Mythic_Level;
				if (channel is not null)
					achievement.State.HighestTimedKeyLevelAnnounced = keystoneRun.Mythic_Level;
				m_db.Update(achievement.State);
			}

			m_db.Insert(run, "OR IGNORE");
		}

		LogJobFinished(m_logger, stopwatch.Elapsed);
	}

	private IMessageChannel? GetAnnouncementChannel()
	{
		if (string.IsNullOrWhiteSpace(m_discordChannel))
			return null;

		var guild = m_discordClient.Guilds.SingleOrDefault();
		return guild?.Channels.SingleOrDefault(c => c.Name == m_discordChannel) as IMessageChannel;
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
				.WithDefaultFooter(m_urls)
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
				.WithDefaultFooter(m_urls)
				.WithTitle($"👑 {AchievementRules.FormatLane(lane)} Ranking")
				.WithColor(Color.Gold)
				.WithDescription($"{character.Name} entered the **Top {band.Value} {AchievementRules.FormatLane(lane)} {AchievementRules.FormatRankCategory(category, categoryLabel)}** rankings.")
				.WithCurrentTimestamp();

			await channel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
			rankingState.BestBandAnnounced = band.Value;
			m_db.Update(rankingState);
		}
	}

	private List<PersonalBestRunAchievement> GetRunAchievements(MythicPlusKeystoneRunDto run, IReadOnlyList<Character> followedCharacters, Dictionary<int, CharacterDto> characterProfiles)
	{
		var achievements = new List<PersonalBestRunAchievement>();
		if (run.Clear_Time_Ms > run.Keystone_Time_Ms)
			return achievements;

		return [.. RunAchievementDetector.GetPersonalBestAchievements(run, followedCharacters, characterProfiles, GetOrCreateDungeonAchievementState).Select(x => new PersonalBestRunAchievement(x.CharacterName, x.DungeonName, x.KeyLevel, x.State))];
	}

	private List<SeasonHighRunAchievement> GetSeasonHighAchievements(MythicPlusKeystoneRunDto run, IReadOnlyList<Character> followedCharacters, Dictionary<int, CharacterDto> characterProfiles)
	{
		var achievements = new List<SeasonHighRunAchievement>();
		if (run.Clear_Time_Ms > run.Keystone_Time_Ms)
			return achievements;

		foreach (var character in followedCharacters)
		{
			if (!characterProfiles.TryGetValue(character.Id, out var profile) || profile.CurrentMythicPlusSeason is not { } season)
				continue;

			if (!run.Roster.Any(rosterMember => RunAchievementDetector.IsSameCharacter(rosterMember.Character, character)))
				continue;

			var highestTimedKeySeen = m_db.Table<CharacterDungeonAchievementState>()
				.Where(x => x.CharacterId == character.Id && x.Season == season)
				.Select(x => x.HighestTimedKeyLevelSeen)
				.DefaultIfEmpty()
				.Max();

			if (run.Mythic_Level > highestTimedKeySeen)
				achievements.Add(new SeasonHighRunAchievement(character.Name, run.Mythic_Level));
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

	private CharacterDungeonAchievementState GetOrCreateDungeonAchievementState(Character character, string season, DungeonDto dungeon)
	{
		var state = m_db.Table<CharacterDungeonAchievementState>().FirstOrDefault(x => x.CharacterId == character.Id && x.Season == season && x.DungeonSlug == dungeon.Slug);
		if (state is not null)
			return state;

		state = new CharacterDungeonAchievementState { CharacterId = character.Id, Season = season, DungeonSlug = dungeon.Slug, DungeonName = dungeon.Name, DungeonShortName = dungeon.Short_Name };
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

	private sealed record PersonalBestRunAchievement(string CharacterName, string DungeonName, int KeyLevel, CharacterDungeonAchievementState State);

	private sealed record SeasonHighRunAchievement(string CharacterName, int KeyLevel);

	private static readonly Action<ILogger, Exception?> s_logJobStarting = LoggerMessage.Define(
		LogLevel.Information,
		new EventId(1, nameof(LogJobStarting)),
		$"Starting {nameof(CheckRunsJob)} job.");

	private static readonly Action<ILogger, Exception?> s_logNoAnnouncementChannel = LoggerMessage.Define(
		LogLevel.Information,
		new EventId(2, nameof(LogNoAnnouncementChannel)),
		"No Discord announcement channel is available; syncing Raider.IO data without posting announcements.");

	private static readonly Action<ILogger, TimeSpan, Exception?> s_logJobFinished = LoggerMessage.Define<TimeSpan>(
		LogLevel.Information,
		new EventId(3, nameof(LogJobFinished)),
		$"Finished {nameof(CheckRunsJob)} job after {{Elapsed}}.");

	private static void LogJobStarting(ILogger logger) => s_logJobStarting(logger, null);
	private static void LogNoAnnouncementChannel(ILogger logger) => s_logNoAnnouncementChannel(logger, null);
	private static void LogJobFinished(ILogger logger, TimeSpan elapsed) => s_logJobFinished(logger, elapsed, null);

	private readonly ILogger<CheckRunsJob> m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	private readonly DiscordSocketClient m_discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
	private readonly RaiderIOClient m_raiderIOClient = raiderIOClient ?? throw new ArgumentNullException(nameof(raiderIOClient));
	private readonly SQLiteConnection m_db = db ?? throw new ArgumentNullException(nameof(db));
	private readonly CharacterRepository m_characters = characters ?? throw new ArgumentNullException(nameof(characters));
	private readonly WebUrlBuilder m_urls = urls ?? throw new ArgumentNullException(nameof(urls));
	private readonly string? m_discordChannel = config["Discord:Channel"];
}
