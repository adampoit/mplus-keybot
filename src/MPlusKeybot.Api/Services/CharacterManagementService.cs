namespace MPlusKeybot.Api.Services;

public sealed class CharacterManagementService(CharacterRepository characters)
{
	public CharacterFollowUpdateResult UpdateFollowState(string discordUserId, IReadOnlyCollection<VerifiedCharacter> verifiedCharacters, IReadOnlyCollection<CharacterKey> selectedCharacters)
	{
		var verifiedByKey = verifiedCharacters
			.GroupBy(x => x.Key)
			.ToDictionary(x => x.Key, x => x.First());
		var selected = selectedCharacters.ToHashSet();

		var unverifiedSelections = selected.Where(x => !verifiedByKey.ContainsKey(x)).ToList();
		if (unverifiedSelections.Count > 0)
			throw new InvalidOperationException("Posted character choices included characters that were not verified by Battle.net.");

		var existing = m_characters.GetCharacters([.. verifiedByKey.Keys]);
		var followed = new List<CharacterKey>();
		var unfollowed = new List<CharacterKey>();
		var now = DateTime.UtcNow;

		foreach (var (key, verifiedCharacter) in verifiedByKey)
		{
			var wasFollowed = existing.TryGetValue(key, out var character) && character.IsFollowed;
			if (selected.Contains(key))
			{
				m_characters.UpsertFollowedCharacter(verifiedCharacter, discordUserId, now);
				if (!wasFollowed)
					followed.Add(key);
			}
			else if (wasFollowed)
			{
				m_characters.UnfollowCharacter(verifiedCharacter, discordUserId, now);
				unfollowed.Add(key);
			}
		}

		return new CharacterFollowUpdateResult(followed, unfollowed);
	}

	private readonly CharacterRepository m_characters = characters ?? throw new ArgumentNullException(nameof(characters));
}

public sealed record CharacterFollowUpdateResult(IReadOnlyList<CharacterKey> Followed, IReadOnlyList<CharacterKey> Unfollowed);
