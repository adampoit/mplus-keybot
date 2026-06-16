using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Vite.AspNetCore;

public static class WebPageRenderer
{
	public static IResult RenderApp(
		WebUrlBuilder urls,
		IViteManifest? viteManifest,
		IViteDevServerStatus? viteDevServer,
		string title = "mplus-keybot",
		HttpStatusCode statusCode = HttpStatusCode.OK,
		bool isDevelopment = false)
	{
		var template = WebAssetPaths.ReadText("templates/layout.html");
		var requestPathBase = !string.IsNullOrWhiteSpace(urls.PathBase)
			? urls.PathBase
			: urls.CookiePath == "/" ? string.Empty : urls.CookiePath;
		var routeBaseUrl = string.IsNullOrWhiteSpace(requestPathBase) ? "/" : requestPathBase;
		var apiBaseUrl = requestPathBase + "/api";
		var html = template
			.Replace("{{Title}}", Html(title), StringComparison.Ordinal)
			.Replace("{{ApiBaseUrl}}", Html(apiBaseUrl), StringComparison.Ordinal)
			.Replace("{{RouteBaseUrl}}", Html(routeBaseUrl), StringComparison.Ordinal)
			.Replace("{{AssetTags}}", BuildAssetTags(urls, viteManifest, viteDevServer, isDevelopment), StringComparison.Ordinal);
		return Results.Content(html, "text/html; charset=utf-8", statusCode: (int)statusCode);
	}

	public static string Html(string value) => HtmlEncoder.Default.Encode(value);

	private static string BuildAssetTags(WebUrlBuilder urls, IViteManifest? viteManifest, IViteDevServerStatus? viteDevServer, bool isDevelopment)
	{
		if (isDevelopment && viteDevServer?.IsEnabled == true)
		{
			var viteUrl = viteDevServer.ServerUrlWithBasePath.TrimEnd('/');
			return $$"""
			<script type="module">
				import RefreshRuntime from '{{Html(viteUrl)}}/@react-refresh';
				RefreshRuntime.injectIntoGlobalHook(window);
				window.$RefreshReg$ = () => {};
				window.$RefreshSig$ = () => type => type;
				window.__vite_plugin_react_preamble_installed__ = true;
			</script>
			<script type="module" src="{{Html(viteUrl)}}/@vite/client"></script>
			<script type="module" src="{{Html(viteUrl)}}/src/main.tsx"></script>
			""";
		}

		if (viteManifest is not null && viteManifest.ContainsKey("src/main.tsx"))
		{
			var scriptEntry = viteManifest["src/main.tsx"];
			if (scriptEntry is not null && !string.IsNullOrWhiteSpace(scriptEntry.File))
			{
				var tags = new StringBuilder();
				foreach (var cssPath in scriptEntry.Css ?? [])
				{
					tags.AppendLine($"<link rel=\"stylesheet\" href=\"{Html(urls.BuildPublicUrl("/assets/" + cssPath))}\">");
				}
				tags.Append($"<script type=\"module\" src=\"{Html(urls.BuildPublicUrl("/assets/" + scriptEntry.File))}\"></script>");
				return tags.ToString();
			}
		}

		return $$"""
		<script type="module" src="{{Html(BuildSourceAssetUrl(urls, "/assets/app.js", "src/main.tsx", isDevelopment))}}"></script>
		""";
	}

	private static string BuildSourceAssetUrl(WebUrlBuilder urls, string publicPath, string assetPath, bool isDevelopment)
	{
		if (!isDevelopment)
			return urls.BuildPublicUrl(publicPath);

		return urls.BuildPublicUrl(publicPath, ("v", WebAssetPaths.GetVersion(assetPath)));
	}
}
