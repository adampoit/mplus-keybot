using SQLite;

public sealed class MythicPlusRun : IEquatable<MythicPlusRun>
{
	[PrimaryKey]
	public string Id { get; set; }
	public DateTimeOffset Date { get; set; }

	public override int GetHashCode() => HashCode.Combine(Id);

	public override bool Equals(object other) => Equals(other as MythicPlusRun);

	public bool Equals(MythicPlusRun? other) => other?.Id == Id;
}
