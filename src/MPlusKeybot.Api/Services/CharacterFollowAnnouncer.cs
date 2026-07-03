using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MPlusKeybot.Api.Services;

public interface ICharacterFollowAnnouncer
{
	Task AnnounceCharactersFollowedAsync(string discordUserId, IReadOnlyList<VerifiedCharacter> characters, CancellationToken cancellationToken = default);
}

public sealed class DiscordCharacterFollowAnnouncer(DiscordSocketClient discordClient, IConfiguration config, ILogger<DiscordCharacterFollowAnnouncer> logger, WebUrlBuilder urls) : ICharacterFollowAnnouncer
{
	public async Task AnnounceCharactersFollowedAsync(string discordUserId, IReadOnlyList<VerifiedCharacter> characters, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(characters);

		if (characters.Count == 0)
			return;

		var channel = GetAnnouncementChannel();
		if (channel is null)
		{
			LogNoDiscordChannel(m_logger);
			return;
		}

		try
		{
			foreach (var character in characters)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var embed = BuildFollowEmbed(discordUserId, character);
				await channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			LogUnableToSendDiscordAnnouncement(m_logger, ex);
		}
	}

	private IMessageChannel? GetAnnouncementChannel()
	{
		var configuredChannelName = m_config["Discord:Channel"];
		if (string.IsNullOrWhiteSpace(configuredChannelName))
			return null;

		var guild = m_discordClient.Guilds.SingleOrDefault();
		return guild?.Channels.SingleOrDefault(c => c.Name == configuredChannelName) as IMessageChannel;
	}

	private Embed BuildFollowEmbed(string discordUserId, VerifiedCharacter character) =>
		CharacterFollowAnnouncementFormatter.BuildEmbed(discordUserId, character, m_urls.PublicBaseUrl);

	private static readonly Action<ILogger, Exception?> s_logNoDiscordChannel = LoggerMessage.Define(
		LogLevel.Information,
		new EventId(1, nameof(LogNoDiscordChannel)),
		"No Discord channel is available for character follow announcements.");

	private static readonly Action<ILogger, Exception?> s_logUnableToSendDiscordAnnouncement = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(2, nameof(LogUnableToSendDiscordAnnouncement)),
		"Unable to send character follow announcement to Discord.");

	private static void LogNoDiscordChannel(ILogger logger) => s_logNoDiscordChannel(logger, null);
	private static void LogUnableToSendDiscordAnnouncement(ILogger logger, Exception exception) => s_logUnableToSendDiscordAnnouncement(logger, exception);

	private readonly DiscordSocketClient m_discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
	private readonly IConfiguration m_config = config ?? throw new ArgumentNullException(nameof(config));
	private readonly ILogger<DiscordCharacterFollowAnnouncer> m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	private readonly WebUrlBuilder m_urls = urls ?? throw new ArgumentNullException(nameof(urls));
}
