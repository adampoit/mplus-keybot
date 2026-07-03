using SQLite;

namespace MPlusKeybot.Api.Database;

public sealed class CharacterRankingAchievementState
{
	[PrimaryKey, AutoIncrement]
	public int Id { get; set; }

	[Indexed(Name = "IX_CharacterRankingAchievementState_Key", Unique = true)]
	public int CharacterId { get; set; }

	[Indexed(Name = "IX_CharacterRankingAchievementState_Key", Unique = true)]
	public string Season { get; set; } = null!;

	[Indexed(Name = "IX_CharacterRankingAchievementState_Key", Unique = true)]
	public string Lane { get; set; } = null!;

	[Indexed(Name = "IX_CharacterRankingAchievementState_Key", Unique = true)]
	public string Category { get; set; } = null!;

	public int BestBandAnnounced { get; set; }
}
