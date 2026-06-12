using SQLite;

public sealed class VerifiedCharacterSession
{
	[PrimaryKey, AutoIncrement]
	public int Id { get; set; }
	[Indexed(Name = "IX_VerifiedCharacterSession_Session_Set")]
	public string SessionId { get; set; } = null!;
	[Indexed(Name = "IX_VerifiedCharacterSession_Session_Set")]
	public string VerificationSetId { get; set; } = null!;
	public string Region { get; set; } = null!;
	public string Realm { get; set; } = null!;
	public string Name { get; set; } = null!;
	public long? BlizzardCharacterId { get; set; }
	public string? RealmDisplayName { get; set; }
	public DateTime SeenAt { get; set; }
}
