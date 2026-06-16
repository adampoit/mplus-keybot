using Microsoft.Extensions.Configuration;

public sealed class WebUrlBuilder
{
	public WebUrlBuilder(IConfiguration configuration)
	{
		PublicBaseUrl = (configuration["Web:PublicBaseUrl"] ?? "https://localhost:5142").TrimEnd('/');
		PathBase = NormalizePathBase(configuration["Web:PathBase"]);
		CookiePath = GetCookiePath(PublicBaseUrl);
	}

	public string PublicBaseUrl { get; }

	public string PathBase { get; }

	public string CookiePath { get; }

	public string BuildPublicUrl(string path, params (string Name, string Value)[] query)
	{
		var normalizedPath = path.StartsWith('/') ? path : "/" + path;
		var url = PublicBaseUrl + normalizedPath;
		if (query.Length == 0)
			return url;

		return url + "?" + string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(x.Value)}"));
	}

	private static string NormalizePathBase(string? pathBase)
	{
		if (string.IsNullOrWhiteSpace(pathBase) || pathBase == "/")
			return string.Empty;

		return "/" + pathBase.Trim('/');
	}

	private static string GetCookiePath(string publicBaseUrl)
	{
		var path = new Uri(publicBaseUrl).AbsolutePath.TrimEnd('/');
		return string.IsNullOrWhiteSpace(path) ? "/" : path;
	}
}
