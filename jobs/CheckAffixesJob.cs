using System.Diagnostics;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;
using SQLite;

public sealed class CheckAffixesJob : IJob
{
	public CheckAffixesJob(ILogger<CheckAffixesJob> logger, DiscordSocketClient discordClient, RaiderIOClient raiderIOClient, SQLiteConnection db, IConfiguration config)
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
		m_logger.LogInformation($"Starting {nameof(CheckAffixesJob)} job.");

		var guild = m_discordClient.Guilds.Single();
		var channel = guild.Channels.Single(c => c.Name == m_discordChannel) as IMessageChannel;

		var affixInfo = m_db.Table<AffixInfo>().FirstOrDefault();
		var affixes = await m_raiderIOClient.GetAffixes().ConfigureAwait(false);
		if (affixes.IsFailure)
			return;

		if (affixes.Result!.Title != affixInfo?.Affixes)
		{
			var embed = new EmbedBuilder()
				.WithFooter(footer => footer.Text = "Data provided by Raider.IO")
				.WithTitle(affixes.Result.Title)
				.WithColor(Color.Gold)
				.WithDescription("Weekly Mythic Plus affixes updated.")
				.WithCurrentTimestamp();

			await channel!.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);

			if (affixInfo is null)
			{
				var newInfo = new AffixInfo { Affixes = affixes.Result.Title };
				m_db.Insert(newInfo);
			}
			else
			{
				affixInfo.Affixes = affixes.Result.Title;
				m_db.Update(affixInfo);
			}
		}

		m_logger.LogInformation($"Finished {nameof(CheckAffixesJob)} job after {stopwatch.Elapsed}.");
	}

	private readonly ILogger<CheckAffixesJob> m_logger;
	private readonly DiscordSocketClient m_discordClient;
	private readonly RaiderIOClient m_raiderIOClient;
	private readonly SQLiteConnection m_db;
	private readonly string m_discordChannel;
}