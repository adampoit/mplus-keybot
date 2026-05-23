public static class RunAchievementDetector
{
	public static List<RunPersonalBestAchievement> GetPersonalBestAchievements(
		MythicPlusKeystoneRunDto run,
		IEnumerable<Character> followedCharacters,
		IReadOnlyDictionary<int, CharacterDto> characterProfiles,
		Func<Character, string, DungeonDto, CharacterDungeonAchievementState> getDungeonAchievementState)
	{
		var achievements = new List<RunPersonalBestAchievement>();
		if (run.Mythic_Level < AchievementRules.MinimumPersonalBestAnnouncementLevel || run.Clear_Time_Ms > run.Keystone_Time_Ms)
			return achievements;

		foreach (var character in followedCharacters)
		{
			if (!characterProfiles.TryGetValue(character.Id, out var profile))
				continue;

			var season = profile.CurrentMythicPlusSeason;
			if (season is null)
				continue;

			if (!run.Roster.Any(rosterMember => IsSameCharacter(rosterMember.Character, character)))
				continue;

			var state = getDungeonAchievementState(character, season, run.Dungeon);
			if (run.Mythic_Level <= state.HighestTimedKeyLevelSeen)
				continue;

			achievements.Add(new RunPersonalBestAchievement(character.Name, run.Dungeon.Name, run.Mythic_Level, state));
		}

		return achievements;
	}

	public static bool IsSameCharacter(RosterCharacterDto rosterCharacter, Character followedCharacter)
	{
		var characterName = rosterCharacter.Name.Split('-')[0];
		var pathParts = rosterCharacter.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
		var region = pathParts.Length >= 2 ? pathParts[1] : null;
		var realm = pathParts.Length >= 3 ? pathParts[2] : null;

		return string.Equals(characterName, followedCharacter.Name, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(region, followedCharacter.Region, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(NormalizeRealmSlug(realm), NormalizeRealmSlug(followedCharacter.Realm), StringComparison.OrdinalIgnoreCase);
	}

	private static string? NormalizeRealmSlug(string? realm) => realm?.Replace(' ', '-');
}

public sealed record RunPersonalBestAchievement(string CharacterName, string DungeonName, int KeyLevel, CharacterDungeonAchievementState State)
{
	public override string ToString() => $"🏆 {CharacterName} set a new **{DungeonName}** personal best: **+{KeyLevel}**";
}
