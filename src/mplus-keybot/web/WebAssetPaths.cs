using System.Globalization;
using Microsoft.AspNetCore.Hosting;

public static class WebAssetPaths
{
	private static string? s_webDirectory;

	public static string WebDirectory => s_webDirectory ??= ResolveWebDirectory(Directory.GetCurrentDirectory());

	public static void Configure(IWebHostEnvironment environment)
	{
		s_webDirectory = ResolveWebDirectory(environment.ContentRootPath);
	}

	public static string ReadText(string relativePath)
	{
		return File.ReadAllText(GetPath(relativePath));
	}

	public static string GetPath(string relativePath)
	{
		return Path.Combine(WebDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	public static string GetVersion(string relativePath)
	{
		var path = GetPath(relativePath);
		return File.Exists(path)
			? File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture)
			: DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
	}

	public static string GetDistDirectory(string contentRootPath) => Path.Combine(ResolveWebDirectory(contentRootPath), "dist");

	public static string? GetBuiltAssetPath(string requestPath)
	{
		var relativePath = requestPath.Replace('/', Path.DirectorySeparatorChar);
		var path = Path.Combine(WebDirectory, "dist", relativePath);
		return File.Exists(path) ? path : null;
	}

	private static string ResolveWebDirectory(string contentRootPath)
	{
		var candidates = new[]
		{
			Path.Combine(contentRootPath, "web"),
			Path.Combine(contentRootPath, "src", "mplus-keybot", "web"),
			Path.Combine(AppContext.BaseDirectory, "web"),
		};

		foreach (var candidate in candidates)
		{
			if (Directory.Exists(candidate))
				return candidate;
		}

		throw new DirectoryNotFoundException($"Could not find the web asset directory. Checked: {string.Join(", ", candidates)}");
	}
}
