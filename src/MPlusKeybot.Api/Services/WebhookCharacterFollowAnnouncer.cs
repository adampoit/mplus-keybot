using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MPlusKeybot.Api.Services;

// Announces followed characters by POSTing them as JSON to a configured
// webhook URL. Used by the e2e test harness, which points this at a collector
// resource; production uses DiscordCharacterFollowAnnouncer.
public sealed class WebhookCharacterFollowAnnouncer(HttpClient client, IConfiguration configuration, ILogger<WebhookCharacterFollowAnnouncer> logger) : ICharacterFollowAnnouncer
{
	public async Task AnnounceCharactersFollowedAsync(string discordUserId, IReadOnlyList<VerifiedCharacter> characters, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(characters);

		if (characters.Count == 0)
			return;

		var webhookUrl = m_configuration["Follow:WebhookUrl"];
		if (string.IsNullOrWhiteSpace(webhookUrl))
		{
			LogWebhookUrlNotConfigured(m_logger);
			return;
		}

		var payload = new WebhookAnnouncement(discordUserId, [.. characters]);
		using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
		{
			Content = new StringContent(JsonSerializer.Serialize(payload, s_jsonOptions), null, "application/json"),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "e2e");

		try
		{
			using var response = await m_client.SendAsync(request, cancellationToken).ConfigureAwait(false);
			response.EnsureSuccessStatusCode();
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			LogUnableToPostWebhookAnnouncement(m_logger, ex);
		}
	}

	private static readonly Action<ILogger, Exception?> s_logWebhookUrlNotConfigured = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(1, nameof(LogWebhookUrlNotConfigured)),
		"Follow:WebhookUrl is not configured; skipping character follow announcements.");

	private static readonly Action<ILogger, Exception?> s_logUnableToPostWebhookAnnouncement = LoggerMessage.Define(
		LogLevel.Warning,
		new EventId(2, nameof(LogUnableToPostWebhookAnnouncement)),
		"Unable to post character follow announcement to webhook.");

	private static void LogWebhookUrlNotConfigured(ILogger logger) => s_logWebhookUrlNotConfigured(logger, null);
	private static void LogUnableToPostWebhookAnnouncement(ILogger logger, Exception exception) => s_logUnableToPostWebhookAnnouncement(logger, exception);

	private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNameCaseInsensitive = true };
	private readonly HttpClient m_client = client ?? throw new ArgumentNullException(nameof(client));
	private readonly IConfiguration m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
	private readonly ILogger<WebhookCharacterFollowAnnouncer> m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
}

public sealed record WebhookAnnouncement(string DiscordUserId, IReadOnlyList<VerifiedCharacter> Characters);
