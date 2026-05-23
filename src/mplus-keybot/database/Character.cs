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
}
