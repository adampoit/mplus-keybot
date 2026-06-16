using System.Globalization;

public sealed record CharacterKey(string Region, string Realm, string Name)
{
	public static CharacterKey From(string region, string realm, string name) => new(
		NormalizeRegion(region),
		NormalizeRealm(realm),
		name.Trim());

	public static bool TryParse(string value, out CharacterKey key)
	{
		var parts = value.Split('|', 3, StringSplitOptions.TrimEntries);
		if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
		{
			key = null!;
			return false;
		}

		key = From(parts[0], parts[1], parts[2]);
		return true;
	}

	public override string ToString() => $"{Region}|{Realm}|{Name}";

	private static string NormalizeRegion(string region) => region.Trim().ToLowerInvariant();

	private static string NormalizeRealm(string realm) => realm.Trim().Replace(' ', '-').ToLower(CultureInfo.InvariantCulture);
}

public sealed record VerifiedCharacter(
	string Region,
	string Realm,
	string Name,
	long? BlizzardCharacterId = null,
	string? RealmDisplayName = null,
	int? Level = null,
	string? Class = null)
{
	public CharacterKey Key => CharacterKey.From(Region, Realm, Name);

	public string? RenderUrl => BlizzardCharacterId is { } id
		? $"https://render.worldofwarcraft.com/{Region.ToLowerInvariant()}/character/{Realm}/{id % 256}/{id}-avatar.jpg"
		: null;
}
