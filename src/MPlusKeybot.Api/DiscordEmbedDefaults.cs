using Discord;
using MPlusKeybot.Api.Services;

namespace MPlusKeybot.Api;

public static class DiscordEmbedDefaults
{
	public static string GetFooterText(WebUrlBuilder urls) => GetFooterText(urls.PublicBaseUrl);

	public static string GetFooterText(string pageUrl) => FormatPageUrl(pageUrl);

	public static EmbedBuilder WithDefaultFooter(this EmbedBuilder builder, WebUrlBuilder urls) => builder
		.WithFooter(footer => footer.Text = GetFooterText(urls));

	private static string FormatPageUrl(string pageUrl)
	{
		if (pageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			return pageUrl["https://".Length..];
		if (pageUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
			return pageUrl["http://".Length..];
		return pageUrl;
	}
}
