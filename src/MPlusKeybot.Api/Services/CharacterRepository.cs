using MPlusKeybot.Api.Database;
using SQLite;

namespace MPlusKeybot.Api.Services;

public sealed class CharacterRepository(SQLiteConnection db)
{
	public IReadOnlyList<Character> GetFollowedCharacters()
	{
		lock (m_lock)
		{
			return [.. m_db.Table<Character>().Where(x => x.IsFollowed)];
		}
	}

	public IReadOnlyDictionary<CharacterKey, Character> GetCharacters(IReadOnlyCollection<CharacterKey> keys)
	{
		ArgumentNullException.ThrowIfNull(keys);
		lock (m_lock)
		{
			var result = new Dictionary<CharacterKey, Character>();
			foreach (var key in keys)
			{
				var character = GetCharacterNoLock(key);
				if (character is not null)
					result[key] = character;
			}

			return result;
		}
	}

	public Character? GetCharacter(CharacterKey key)
	{
		lock (m_lock)
		{
			return GetCharacterNoLock(key);
		}
	}

	public Character UpsertFollowedCharacter(VerifiedCharacter verifiedCharacter, string discordUserId, DateTime verifiedAt)
	{
		ArgumentNullException.ThrowIfNull(verifiedCharacter);
		var key = verifiedCharacter.Key;
		lock (m_lock)
		{
			var character = GetCharacterNoLock(key);
			if (character is null)
			{
				character = new Character
				{
					Name = key.Name,
					Realm = key.Realm,
					Region = key.Region,
					IsFollowed = true,
					LastVerifiedAt = verifiedAt,
					LastManagedByDiscordUserId = discordUserId,
					BlizzardCharacterId = verifiedCharacter.BlizzardCharacterId,
					RealmDisplayName = verifiedCharacter.RealmDisplayName,
				};
				m_db.Insert(character, "OR IGNORE");
				character = GetCharacterNoLock(key)!;
			}

			character.IsFollowed = true;
			character.ErroringSince = null;
			character.LastVerifiedAt = verifiedAt;
			character.LastManagedByDiscordUserId = discordUserId;
			character.BlizzardCharacterId = verifiedCharacter.BlizzardCharacterId ?? character.BlizzardCharacterId;
			character.RealmDisplayName = verifiedCharacter.RealmDisplayName ?? character.RealmDisplayName;
			character.Class = verifiedCharacter.Class ?? character.Class;
			m_db.Update(character);
			return character;
		}
	}

	public Character? UnfollowCharacter(VerifiedCharacter verifiedCharacter, string discordUserId, DateTime verifiedAt)
	{
		ArgumentNullException.ThrowIfNull(verifiedCharacter);
		lock (m_lock)
		{
			var character = GetCharacterNoLock(verifiedCharacter.Key);
			if (character is null)
				return null;

			character.IsFollowed = false;
			character.LastVerifiedAt = verifiedAt;
			character.LastManagedByDiscordUserId = discordUserId;
			character.BlizzardCharacterId = verifiedCharacter.BlizzardCharacterId ?? character.BlizzardCharacterId;
			character.RealmDisplayName = verifiedCharacter.RealmDisplayName ?? character.RealmDisplayName;
			character.Class = verifiedCharacter.Class ?? character.Class;
			m_db.Update(character);
			return character;
		}
	}

	public void SaveVerifiedCharacterSet(string sessionId, string verificationSetId, IReadOnlyCollection<VerifiedCharacter> characters, DateTime seenAt)
	{
		lock (m_lock)
		{
			m_db.Execute("DELETE FROM VerifiedCharacterSession WHERE SessionId = ? AND VerificationSetId = ?", sessionId, verificationSetId);
			m_db.InsertAll(characters.Select(character => new VerifiedCharacterSession
			{
				SessionId = sessionId,
				VerificationSetId = verificationSetId,
				Region = character.Key.Region,
				Realm = character.Key.Realm,
				Name = character.Key.Name,
				BlizzardCharacterId = character.BlizzardCharacterId,
				RealmDisplayName = character.RealmDisplayName,
				Class = character.Class,
				SeenAt = seenAt,
			}));
			m_db.Execute("DELETE FROM VerifiedCharacterSession WHERE SeenAt < ?", DateTime.UtcNow - TimeSpan.FromHours(1));
		}
	}

	public IReadOnlyList<VerifiedCharacter> GetVerifiedCharacterSet(string sessionId, string verificationSetId, TimeSpan maxAge)
	{
		lock (m_lock)
		{
			var cutoff = DateTime.UtcNow - maxAge;
			return [.. m_db.Table<VerifiedCharacterSession>()
				.Where(x => x.SessionId == sessionId && x.VerificationSetId == verificationSetId && x.SeenAt >= cutoff)
				.ToList()
				.Select(x => new VerifiedCharacter(x.Region, x.Realm, x.Name, x.BlizzardCharacterId, x.RealmDisplayName, null, x.Class))];
		}
	}

	public void DeleteVerifiedCharacterSet(string sessionId, string verificationSetId)
	{
		lock (m_lock)
		{
			m_db.Execute("DELETE FROM VerifiedCharacterSession WHERE SessionId = ? AND VerificationSetId = ?", sessionId, verificationSetId);
		}
	}

	public IReadOnlyList<CharacterDungeonAchievementState> GetCharacterDungeonAchievements(int characterId, string? season)
	{
		lock (m_lock)
		{
			var query = m_db.Table<CharacterDungeonAchievementState>().Where(x => x.CharacterId == characterId);
			if (!string.IsNullOrWhiteSpace(season))
				query = query.Where(x => x.Season == season);
			return [.. query.OrderByDescending(x => x.HighestTimedKeyLevelSeen)];
		}
	}

	public CharacterAchievementState? GetCharacterScoreState(int characterId, string season)
	{
		lock (m_lock)
		{
			return m_db.Table<CharacterAchievementState>().FirstOrDefault(x => x.CharacterId == characterId && x.Season == season);
		}
	}

	private Character? GetCharacterNoLock(CharacterKey key) => m_db.Table<Character>()
		.FirstOrDefault(x => x.Name == key.Name && x.Realm == key.Realm && x.Region == key.Region);

	private readonly SQLiteConnection m_db = db ?? throw new ArgumentNullException(nameof(db));
	private readonly Lock m_lock = new();
}
