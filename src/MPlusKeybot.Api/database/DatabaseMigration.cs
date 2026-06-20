using SQLite;

public sealed class DatabaseMigration
{
	[PrimaryKey]
	public string Name { get; set; } = null!;
	public DateTime AppliedAt { get; set; }
}
