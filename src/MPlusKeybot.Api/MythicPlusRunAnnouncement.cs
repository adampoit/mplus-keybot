using System.Globalization;

namespace MPlusKeybot.Api;

public sealed record MythicPlusRunAnnouncement(
	string Id,
	DateTimeOffset CompletedAt,
	int KeyLevel,
	int ClearTimeMs,
	int KeystoneTimeMs,
	DungeonAnnouncement Dungeon,
	IReadOnlyList<RunAnnouncementRosterMember> Roster,
	IReadOnlyList<string> PersonalBestCharacterNames,
	IReadOnlyList<string> SeasonHighCharacterNames)
{
	public static MythicPlusRunAnnouncement From(
		string runId,
		MythicPlusKeystoneRunDto run,
		IEnumerable<string> personalBestCharacterNames,
		IEnumerable<string> seasonHighCharacterNames)
	{
		ArgumentNullException.ThrowIfNull(run);

		return new MythicPlusRunAnnouncement(
			runId,
			DateTimeOffset.Parse(run.Completed_At, CultureInfo.InvariantCulture),
			run.Mythic_Level,
			run.Clear_Time_Ms,
			run.Keystone_Time_Ms,
			new DungeonAnnouncement(run.Dungeon.Name, run.Dungeon.Slug, run.Dungeon.Expansion_Id),
			[.. run.Roster
				.Select(member => new RunAnnouncementRosterMember(
					member.Character.Name.Split('-')[0],
					member.Character.Path,
					member.Role,
					member.Character.Spec.Name,
					member.Character.Class.Name,
					member.Ranks.Score))],
			[.. personalBestCharacterNames],
			[.. seasonHighCharacterNames]);
	}
}

public sealed record DungeonAnnouncement(string Name, string Slug, int ExpansionId);

public sealed record RunAnnouncementRosterMember(
	string CharacterName,
	string CharacterPath,
	Role Role,
	string SpecName,
	string ClassName,
	double Score);
