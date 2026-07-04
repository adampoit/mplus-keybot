using Discord;

namespace MPlusKeybot.Api;

public static class CharacterFollowAnnouncementFormatter
{
	public static Embed BuildEmbed(string discordUserId, VerifiedCharacter character, string pageUrl, DateTimeOffset? timestamp = null)
	{
		var builder = new EmbedBuilder()
			.WithFooter(footer => footer.Text = DiscordEmbedDefaults.GetFooterText(pageUrl))
			.WithTitle($"Now following {character.Name}!")
			.WithColor(Color.Blue)
			.WithDescription($"<@{discordUserId}> added [{character.Name}]({BuildRaiderIOProfileUrl(character)}) on **{FormatRealmRegion(character)}** with `/follow`.")
			.WithTimestamp(timestamp ?? DateTimeOffset.UtcNow);

		if (!string.IsNullOrWhiteSpace(character.RenderUrl))
			builder.WithThumbnailUrl(character.RenderUrl);

		return builder.Build();
	}

	private static string FormatRealmRegion(VerifiedCharacter character)
	{
		var realm = character.RealmDisplayName ?? character.Key.Realm;
		return $"{realm}-{character.Key.Region}";
	}

	private static string BuildRaiderIOProfileUrl(VerifiedCharacter character) =>
		$"https://raider.io/characters/{Uri.EscapeDataString(character.Key.Region)}/{Uri.EscapeDataString(character.Key.Realm)}/{Uri.EscapeDataString(character.Key.Name)}";
}
