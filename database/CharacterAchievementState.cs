using SQLite;

public sealed class CharacterAchievementState
{
	[PrimaryKey, AutoIncrement]
	public int Id { get; set; }

	[Indexed(Name = "IX_CharacterAchievementState_Character_Season", Unique = true)]
	public int CharacterId { get; set; }

	[Indexed(Name = "IX_CharacterAchievementState_Character_Season", Unique = true)]
	public string Season { get; set; } = null!;

	public int HighestKeyLevelSeen { get; set; }
	public int HighestKeyLevelAnnounced { get; set; }
	public int HighestScoreMilestoneAnnounced { get; set; }
}
