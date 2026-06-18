using Discord;

namespace mplus_keybot.Tests;

public sealed class CharacterFollowAnnouncementFormatterTests
{
	[Fact]
	public void BuildsCharacterFollowEmbedProperties()
	{
		var character = new VerifiedCharacter("us", "hyjal", "Keela", 101, "Hyjal");
		var timestamp = new DateTimeOffset(2026, 6, 18, 19, 30, 0, TimeSpan.Zero);

		var embed = CharacterFollowAnnouncementFormatter.BuildEmbed("123456", character, "https://mplus-keybot.example", timestamp);

		Assert.Equal("Now following Keela!", embed.Title);
		Assert.Null(embed.Url);
		Assert.Equal("mplus-keybot.example | Data from Raider.IO", embed.Footer?.Text);
		Assert.Equal(Color.Blue, embed.Color);
		Assert.Equal(timestamp, embed.Timestamp);
		Assert.Equal("<@123456> added [Keela](https://raider.io/characters/us/hyjal/Keela) on **Hyjal-us** with `/follow`.", embed.Description);
		Assert.Equal("https://render.worldofwarcraft.com/us/character/hyjal/101/101-avatar.jpg", embed.Thumbnail?.Url);
	}

	[Fact]
	public void BuildsCharacterFollowEmbedWithoutThumbnailWhenRenderUrlIsUnavailable()
	{
		var character = new VerifiedCharacter("us", "area-52", "Newmage");

		var embed = CharacterFollowAnnouncementFormatter.BuildEmbed("123456", character, "https://mplus-keybot.example");

		Assert.Null(embed.Thumbnail?.Url);
		Assert.Equal("<@123456> added [Newmage](https://raider.io/characters/us/area-52/Newmage) on **area-52-us** with `/follow`.", embed.Description);
		Assert.NotNull(embed.Timestamp);
	}
}
