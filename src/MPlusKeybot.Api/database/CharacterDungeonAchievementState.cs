using SQLite;

public sealed class CharacterDungeonAchievementState
{
	[PrimaryKey, AutoIncrement]
	public int Id { get; set; }

	[Indexed(Name = "IX_CharacterDungeonAchievementState_Key", Unique = true)]
	public int CharacterId { get; set; }

	[Indexed(Name = "IX_CharacterDungeonAchievementState_Key", Unique = true)]
	public string Season { get; set; } = null!;

	[Indexed(Name = "IX_CharacterDungeonAchievementState_Key", Unique = true)]
	public string DungeonSlug { get; set; } = null!;

	public string DungeonName { get; set; } = null!;

	public string? DungeonShortName { get; set; }

	public int HighestTimedKeyLevelSeen { get; set; }

	public int HighestTimedKeyLevelAnnounced { get; set; }
}
