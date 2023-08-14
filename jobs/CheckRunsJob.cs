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

			await channel!.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);

			m_db.Insert(run, "OR IGNORE");
		}

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
