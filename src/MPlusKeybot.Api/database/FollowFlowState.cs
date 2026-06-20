using SQLite;

public sealed class FollowFlowState
{
	[PrimaryKey]
	public string State { get; set; } = null!;
	[Indexed]
	public string DiscordUserId { get; set; } = null!;
	public DateTime CreatedAt { get; set; }
	public DateTime ExpiresAt { get; set; }
	public DateTime? ConsumedAt { get; set; }
}
