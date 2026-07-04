using System.Collections.Concurrent;

namespace MPlusKeybot.TestServices;

internal sealed class StubBlizzardState
{
	private IReadOnlyList<BlizzardCharacterDto> m_characters = [];
	private readonly Lock m_lock = new();

	public void SetCharacters(IReadOnlyList<BlizzardCharacterDto> characters)
	{
		lock (m_lock)
			m_characters = [.. characters];
	}

	public object BuildProfile(string region) => new
	{
		wow_accounts = new[]
		{
			new
			{
				characters = GetCharacters().Select(c => new
				{
					character = new
					{
						id = c.BlizzardCharacterId,
						name = c.Name,
						level = c.Level,
						realm = new { name = c.RealmDisplayName ?? c.Realm, slug = c.Realm },
						region = new { slug = region },
						playable_class = c.Class is null ? null : new { name = c.Class },
					},
				}).ToArray(),
			},
		},
	};

	private List<BlizzardCharacterDto> GetCharacters()
	{
		lock (m_lock)
			return [.. m_characters];
	}
}

internal sealed class AnnouncementCollector
{
	private readonly ConcurrentQueue<WebhookAnnouncement> m_announcements = new();

	public void Add(WebhookAnnouncement announcement) => m_announcements.Enqueue(announcement);
	public IReadOnlyList<WebhookAnnouncement> GetAll() => [.. m_announcements];
	public void Clear() => m_announcements.Clear();
}

public sealed record BlizzardStateRequest(IReadOnlyList<BlizzardCharacterDto>? Characters);
public sealed record BlizzardCharacterDto(string Region, string Realm, string Name, long? BlizzardCharacterId, string? RealmDisplayName, int? Level, string? Class = null);
public sealed record WebhookAnnouncement(string DiscordUserId, IReadOnlyList<VerifiedCharacter> Characters);
public sealed record VerifiedCharacter(string Region, string Realm, string Name, long? BlizzardCharacterId = null, string? RealmDisplayName = null, int? Level = null, string? Class = null);
