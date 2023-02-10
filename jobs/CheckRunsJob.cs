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

		var charactersToUpdate = new List<Character>();
		var runIds = new HashSet<string>();
		foreach (var character in m_db.Table<Character>())
		{
			var profile = await m_raiderIOClient.GetCharacterAsync(character.Name, character.Realm, character.Region).ConfigureAwait(false);
			if (profile is null)
				continue;

			runIds.UnionWith(profile.Mythic_Plus_Recent_Runs
				.Select(run => run.RunId)
				.TakeWhile(runId => runId != character.MostRecentRunId));
			character.MostRecentRunId = profile.Mythic_Plus_Recent_Runs.FirstOrDefault()?.RunId;

			charactersToUpdate.Add(character);
		}

		var embeds = new List<Embed>();
		foreach (var runId in runIds)
		{
			var runInfo = (await m_raiderIOClient.GetMythicPlusRunAsync(runId).ConfigureAwait(false))?.KeystoneRun;
			if (runInfo is null)
				continue;

			var percentage = (double)runInfo.Clear_Time_Ms / (double)runInfo.Keystone_Time_Ms;
			var percentageString = percentage < 1 ? $"{1 - percentage:P1} remaining" : $"{percentage - 1:P1} over";

			var rosterString = string.Join(Environment.NewLine, runInfo.Roster
				.OrderBy(r => r.Role)
				.Select(r => $"{GetRoleEmoji(r.Role)} [{r.Character.Name.Split('-')[0]}](https://raider.io{r.Character.Path}) - **{r.Role}** ({r.Character.Spec.Name} {r.Character.Class.Name}) - {r.Ranks.Score:0} Score"));

			var embed = new EmbedBuilder()
				.WithFooter(footer => footer.Text = "Data provided by Raider.IO")
				.WithTitle($"+{runInfo.Mythic_Level} {runInfo.Dungeon.Name}")
				.WithColor(Color.Gold)
				.WithDescription($@"Cleared in {TimeSpan.FromMilliseconds(runInfo.Clear_Time_Ms):mm':'ss} of {TimeSpan.FromMilliseconds(runInfo.Keystone_Time_Ms):mm':'ss} ({percentageString}).{Environment.NewLine}{Environment.NewLine}{rosterString}")
				.WithUrl($"https://raider.io/mythic-plus-runs/{runId}")
				.WithImageUrl($"https://cdnassets.raider.io/images/dungeons/expansion{runInfo.Dungeon.Expansion_Id}/base/{runInfo.Dungeon.Slug}.jpg")
				.WithTimestamp(DateTimeOffset.Parse(runInfo.Completed_At));

			embeds.Add(embed.Build());
		}

		foreach (var embed in embeds.OrderBy(x => x.Timestamp))
			await channel!.SendMessageAsync(embed: embed).ConfigureAwait(false);

		m_db.UpdateAll(charactersToUpdate);
		m_logger.LogInformation($"Finished {nameof(CheckRunsJob)} job after {stopwatch.Elapsed}.");
	}

	private static string GetRoleEmoji(Role role) => role switch
	{
		Role.Tank => "🛡️",
		Role.Healer => "💉",
		Role.Dps => "⚔️",
		_ => throw new InvalidOperationException($"No emoji found for role {role}!"),
	};

	private readonly ILogger<CheckRunsJob> m_logger;
	private readonly DiscordSocketClient m_discordClient;
	private readonly RaiderIOClient m_raiderIOClient;
	private readonly SQLiteConnection m_db;
	private readonly string m_discordChannel;
}
