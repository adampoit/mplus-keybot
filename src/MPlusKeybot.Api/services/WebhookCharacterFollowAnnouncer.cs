using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// Announces followed characters by POSTing them as JSON to a configured
// webhook URL. Used by the e2e test harness, which points this at a collector
// resource; production uses DiscordCharacterFollowAnnouncer.
public sealed class WebhookCharacterFollowAnnouncer : ICharacterFollowAnnouncer
{
	public WebhookCharacterFollowAnnouncer(HttpClient client, IConfiguration configuration, ILogger<WebhookCharacterFollowAnnouncer> logger)
	{
		m_client = client ?? throw new ArgumentNullException(nameof(client));
		m_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task AnnounceCharactersFollowedAsync(string discordUserId, IReadOnlyList<VerifiedCharacter> characters, CancellationToken cancellationToken = default)
	{
		if (characters.Count == 0)
			return;

		var webhookUrl = m_configuration["Follow:WebhookUrl"];
		if (string.IsNullOrWhiteSpace(webhookUrl))
		{
			m_logger.LogWarning("Follow:WebhookUrl is not configured; skipping character follow announcements.");
			return;
		}

		var payload = new WebhookAnnouncement(discordUserId, characters.ToList());
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
			m_logger.LogWarning(ex, "Unable to post character follow announcement to webhook.");
		}
	}

	private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNameCaseInsensitive = true };
	private readonly HttpClient m_client;
	private readonly IConfiguration m_configuration;
	private readonly ILogger<WebhookCharacterFollowAnnouncer> m_logger;
}

public sealed record WebhookAnnouncement(string DiscordUserId, IReadOnlyList<VerifiedCharacter> Characters);
