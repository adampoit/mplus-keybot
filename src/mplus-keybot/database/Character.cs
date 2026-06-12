using SQLite;

public sealed class Character
{
	[PrimaryKey, AutoIncrement]
	public int Id { get; set; }
	[Unique(Name = "UK_Character_Name_Realm_Region"), Collation("NOCASE")]
	public string Name { get; set; } = null!;
	[Unique(Name = "UK_Character_Name_Realm_Region"), Collation("NOCASE")]
	public string Realm { get; set; } = null!;
	[Unique(Name = "UK_Character_Name_Realm_Region"), Collation("NOCASE")]
	public string Region { get; set; } = null!;
	public DateTime? ErroringSince { get; set; }
	public bool IsFollowed { get; set; } = true;
	public DateTime? LastVerifiedAt { get; set; }
	public string? LastManagedByDiscordUserId { get; set; }
	public long? BlizzardCharacterId { get; set; }
	public string? RealmDisplayName { get; set; }
	public DateTime? LastCheckedAt { get; set; }
	public double CurrentScore { get; set; }
	public string? CurrentSeason { get; set; }

	public string? RenderUrl => BlizzardCharacterId is { } id
		? $"https://render.worldofwarcraft.com/{Region.ToLowerInvariant()}/character/{Realm}/{id % 256}/{id}-avatar.jpg"
		: null;
}
