using Discord;

public static class RunAnnouncementFormatter
{
	public static Embed BuildEmbed(MythicPlusRunAnnouncement announcement, string pageUrl)
	{
		var percentage = (double)announcement.ClearTimeMs / announcement.KeystoneTimeMs;
		var percentageString = percentage < 1 ? $"{1 - percentage:P1} remaining" : $"{percentage - 1:P1} over";
		var clearTimeString = $"Cleared in {TimeSpan.FromMilliseconds(announcement.ClearTimeMs):mm':'ss} of {TimeSpan.FromMilliseconds(announcement.KeystoneTimeMs):mm':'ss} ({percentageString}).";

		var rosterString = string.Join(Environment.NewLine, announcement.Roster
			.OrderBy(r => r.Role)
			.Select(r => $"{GetRoleEmoji(r.Role)} [{r.CharacterName}](https://raider.io{r.CharacterPath}) - **{r.Role}** ({r.SpecName} {r.ClassName}) - {r.Score:0} Score"));

		var achievementLines = new List<string>();
		if (announcement.PersonalBestCharacterNames.Count > 0)
			achievementLines.Add($"🏆 New personal best: {FormatCharacterList(announcement.PersonalBestCharacterNames)}");

		if (announcement.SeasonHighCharacterNames.Count > 0)
			achievementLines.Add($"🔥 First +{announcement.KeyLevel} this season: {FormatCharacterList(announcement.SeasonHighCharacterNames)}");

		var achievementsString = achievementLines.Count == 0 ? string.Empty : $"{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, achievementLines)}";
		var description = $@"{clearTimeString}{Environment.NewLine}{Environment.NewLine}{rosterString}{achievementsString}";
		var color = announcement.ClearTimeMs <= announcement.KeystoneTimeMs ? Color.Gold : Color.Red;

		return new EmbedBuilder()
			.WithFooter(footer => footer.Text = DiscordEmbedDefaults.GetFooterText(pageUrl))
			.WithTitle($"+{announcement.KeyLevel} {announcement.Dungeon.Name}")
			.WithColor(color)
			.WithDescription(description)
			.WithUrl($"https://raider.io/mythic-plus-runs/{announcement.Id}")
			.WithImageUrl($"https://cdnassets.raider.io/images/dungeons/expansion{announcement.Dungeon.ExpansionId}/base/{announcement.Dungeon.Slug}.jpg")
			.WithTimestamp(announcement.CompletedAt)
			.Build();
	}

	private static string GetRoleEmoji(Role role) => role switch
	{
		Role.Tank => "🛡️",
		Role.Healer => "💉",
		Role.Dps => "⚔️",
		_ => throw new InvalidOperationException($"No emoji found for role {role}!"),
	};

	private static string FormatCharacterList(IEnumerable<string> characterNames) => string.Join(", ", characterNames.Order(StringComparer.OrdinalIgnoreCase));
}
