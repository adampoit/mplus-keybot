using SQLite;

public sealed class AffixInfo
{
	[PrimaryKey, AutoIncrement]
	public int Id { get; set; }
	public string Affixes { get; set; }
}
