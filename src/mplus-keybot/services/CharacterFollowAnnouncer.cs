using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public interface ICharacterFollowAnnouncer
{
	Task AnnounceCharactersFollowedAsync(IReadOnlyList<VerifiedCharacter> characters, CancellationToken cancellationToken = default);
}

public sealed class DiscordCharacterFollowAnnouncer : ICharacterFollowAnnouncer
{
	public DiscordCharacterFollowAnnouncer(DiscordSocketClient discordClient, IConfiguration config, ILogger<DiscordCharacterFollowAnnouncer> logger)
	{
		m_discordClient = discordClient ?? throw new ArgumentNullException(nameof(discordClient));
		m_config = config ?? throw new ArgumentNullException(nameof(config));
		m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task AnnounceCharactersFollowedAsync(IReadOnlyList<VerifiedCharacter> characters, CancellationToken cancellationToken = default)
	{
		if (characters.Count == 0)
			return;

		var channel = GetAnnouncementChannel();
		if (channel is null)
		{
			m_logger.LogInformation("No Discord channel is available for character follow announcements.");
			return;
		}

		try
		{
			foreach (var character in characters)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await channel.SendMessageAsync($"Now following {character.Name} on {FormatRealmRegion(character)}!").ConfigureAwait(false);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			m_logger.LogWarning(ex, "Unable to send character follow announcement to Discord.");
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

	private static string FormatRealmRegion(VerifiedCharacter character)
	{
		var realm = character.RealmDisplayName ?? character.Key.Realm;
		return $"{realm}-{character.Key.Region}";
	}

	private readonly DiscordSocketClient m_discordClient;
	private readonly IConfiguration m_config;
	private readonly ILogger<DiscordCharacterFollowAnnouncer> m_logger;
}
