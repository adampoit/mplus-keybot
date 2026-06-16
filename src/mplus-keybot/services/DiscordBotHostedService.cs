using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

public sealed class DiscordBotHostedService : IHostedService
{
	public DiscordBotHostedService(
		DiscordSocketClient discordClient,
		IConfiguration config,
		ILogger<DiscordBotHostedService> logger,
		FollowFlowStateService followFlowStates,
		WebUrlBuilder urls)
	{
		m_discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
		m_config = config ?? throw new ArgumentNullException(nameof(config));
		m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		m_followFlowStates = followFlowStates ?? throw new ArgumentNullException(nameof(followFlowStates));
		m_urls = urls ?? throw new ArgumentNullException(nameof(urls));
	}

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
		m_discordClient.Ready -= ReadyAsync;
		m_discordClient.SlashCommandExecuted -= SlashCommandExecutedAsync;
		m_discordClient.Log -= LogAsync;

		await m_discordClient.StopAsync().ConfigureAwait(false);
		await m_discordClient.LogoutAsync().ConfigureAwait(false);
	}

	private Task LogAsync(LogMessage msg)
	{
		m_logger.LogInformation("{Message}", msg.ToString());
		return Task.CompletedTask;
	}

	private async Task ReadyAsync()
	{
		var guild = m_discordClient.Guilds.Single();
		var followCommand = new SlashCommandBuilder()
			.WithName("follow")
			.WithDescription("Follow or unfollow your Battle.net-verified World of Warcraft characters.")
			.Build();

		try
		{
			await guild.BulkOverwriteApplicationCommandAsync([followCommand]).ConfigureAwait(false);
		}
		catch (HttpException exception)
		{
			var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
			m_logger.LogError("{DiscordCommandError}", json);
		}
	}

	private async Task SlashCommandExecutedAsync(SocketSlashCommand command)
	{
		if (command.Data.Name != "follow")
			throw new InvalidOperationException($"Unknown slash command {command.Data.Name}!");

		var state = m_followFlowStates.Create(command.User.Id.ToString(), TimeSpan.FromMinutes(10));
		var url = m_urls.BuildPublicUrl("/follow/start", ("state", state.State));
		var components = new ComponentBuilder()
			.WithButton("Follow/unfollow characters", style: ButtonStyle.Link, url: url)
			.Build();

		await command.RespondAsync(
			"Use this short-lived Battle.net sign-in link to manage followed characters.",
			components: components,
			ephemeral: true).ConfigureAwait(false);
	}

	private readonly DiscordSocketClient m_discordClient;
	private readonly IConfiguration m_config;
	private readonly ILogger<DiscordBotHostedService> m_logger;
	private readonly FollowFlowStateService m_followFlowStates;
	private readonly WebUrlBuilder m_urls;
}
