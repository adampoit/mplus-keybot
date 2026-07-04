using System.Globalization;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MPlusKeybot.Api.Services;

public sealed class DiscordBotHostedService(
	DiscordSocketClient discordClient,
	IConfiguration config,
	ILogger<DiscordBotHostedService> logger,
	FollowFlowStateService followFlowStates,
	WebUrlBuilder urls,
	BotStatusRotator statusRotator) : IHostedService
{
	private readonly BotStatusRotator m_statusRotator = statusRotator ?? throw new ArgumentNullException(nameof(statusRotator));

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		m_discordClient.Log += LogAsync;
		m_discordClient.Ready += ReadyAsync;
		m_discordClient.SlashCommandExecuted += SlashCommandExecutedAsync;

		await m_discordClient.LoginAsync(TokenType.Bot, m_config["Discord:Token"]).ConfigureAwait(false);
		await m_discordClient.StartAsync().ConfigureAwait(false);
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		await m_statusRotator.StopAsync().ConfigureAwait(false);

		m_discordClient.Ready -= ReadyAsync;
		m_discordClient.SlashCommandExecuted -= SlashCommandExecutedAsync;
		m_discordClient.Log -= LogAsync;

		await m_discordClient.StopAsync().ConfigureAwait(false);
		await m_discordClient.LogoutAsync().ConfigureAwait(false);
	}

	private Task LogAsync(LogMessage msg)
	{
		LogDiscordMessage(m_logger, msg.ToString());
		return Task.CompletedTask;
	}

	private async Task ReadyAsync()
	{
		var guild = m_discordClient.Guilds.Single();
		var helpCommand = new SlashCommandBuilder()
			.WithName("help")
			.WithDescription("Show M+ Keybot commands and links.")
			.Build();
		var followCommand = new SlashCommandBuilder()
			.WithName("follow")
			.WithDescription("Follow or unfollow your Battle.net-verified World of Warcraft characters.")
			.Build();

		try
		{
			await guild.BulkOverwriteApplicationCommandAsync([helpCommand, followCommand]).ConfigureAwait(false);
		}
		catch (HttpException exception)
		{
			var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
			LogDiscordCommandError(m_logger, json);
		}

		await m_statusRotator.StartAsync().ConfigureAwait(false);
	}

	private Task SlashCommandExecutedAsync(SocketSlashCommand command) => command.Data.Name switch
	{
		"help" => HandleHelpCommandAsync(command),
		"follow" => HandleFollowCommandAsync(command),
		_ => throw new InvalidOperationException($"Unknown slash command {command.Data.Name}!")
	};

	private async Task HandleFollowCommandAsync(SocketSlashCommand command)
	{
		var state = m_followFlowStates.Create(command.User.Id.ToString(CultureInfo.InvariantCulture), TimeSpan.FromMinutes(10));
		var url = m_urls.BuildPublicUrl("/api/follow/start", ("state", state.State));
		var components = new ComponentBuilder()
			.WithButton("Follow/unfollow characters", style: ButtonStyle.Link, url: url)
			.Build();

		await command.RespondAsync(
			"Use this short-lived Battle.net sign-in link to manage followed characters.",
			components: components,
			ephemeral: true).ConfigureAwait(false);
	}

	private async Task HandleHelpCommandAsync(SocketSlashCommand command)
	{
		var embed = new EmbedBuilder()
			.WithDefaultFooter(m_urls)
			.WithTitle("M+ Keybot help")
			.WithColor(Color.Blue)
			.WithDescription("Follow Battle.net-verified World of Warcraft characters and announce their Mythic+ runs in Discord.")
			.AddField("/follow", "Sign in with Battle.net to choose which of your characters this server follows.")
			.AddField("Run announcements", "Followed Mythic+ runs are announced with Raider.IO data, party details, and achievement callouts.")
			.Build();
		var components = new ComponentBuilder()
			.WithButton("Open website", style: ButtonStyle.Link, url: m_urls.PublicBaseUrl)
			.Build();

		await command.RespondAsync(embed: embed, components: components, ephemeral: true).ConfigureAwait(false);
	}

	private static readonly Action<ILogger, string, Exception?> s_logDiscordMessage = LoggerMessage.Define<string>(
		LogLevel.Information,
		new EventId(1, nameof(LogDiscordMessage)),
		"{Message}");

	private static readonly Action<ILogger, string, Exception?> s_logDiscordCommandError = LoggerMessage.Define<string>(
		LogLevel.Error,
		new EventId(2, nameof(LogDiscordCommandError)),
		"{DiscordCommandError}");

	private static void LogDiscordMessage(ILogger logger, string message) => s_logDiscordMessage(logger, message, null);
	private static void LogDiscordCommandError(ILogger logger, string error) => s_logDiscordCommandError(logger, error, null);

	private readonly DiscordSocketClient m_discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
	private readonly IConfiguration m_config = config ?? throw new ArgumentNullException(nameof(config));
	private readonly ILogger<DiscordBotHostedService> m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	private readonly FollowFlowStateService m_followFlowStates = followFlowStates ?? throw new ArgumentNullException(nameof(followFlowStates));
	private readonly WebUrlBuilder m_urls = urls ?? throw new ArgumentNullException(nameof(urls));
}
