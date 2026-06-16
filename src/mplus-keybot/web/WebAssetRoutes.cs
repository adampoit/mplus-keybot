using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

public static class WebAssetRoutes
{
	private static readonly FileExtensionContentTypeProvider s_contentTypes = new();

	public static void MapWebAssetRoutes(this WebApplication app)
	{
		app.MapGet("/assets/{**path}", ServeBuiltAsset);
	}

	private static IResult ServeBuiltAsset(string path)
	{
		var assetPath = WebAssetPaths.GetBuiltAssetPath(path);
		if (assetPath is null)
			return Results.NotFound();

		return Results.File(assetPath, GetContentType(assetPath));
	}


	private static string GetContentType(string path)
	{
		return s_contentTypes.TryGetContentType(path, out var contentType) ? contentType : "application/octet-stream";
	}
}
