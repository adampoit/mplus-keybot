using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Vite.AspNetCore;

public static class FollowWebRoutes
{
	public static void MapFollowWebRoutes(this WebApplication app)
	{
		app.MapWebAssetRoutes();

		app.MapGet("/signin", SignIn);
		app.MapGet("/signout", SignOut);
		app.MapGet("/follow/start", StartFollowAsync);

		app.MapGet("/api/session", GetSessionAsync);
		app.MapGet("/api/home", GetHomeAsync);
		app.MapGet("/api/follow/characters", GetFollowCharactersAsync);
		app.MapPost("/api/follow/characters", SaveCharactersAsync);

		if (app.Environment.IsDevelopment())
		{
			app.MapGet("/api/dev", GetDevTools);
			app.MapPost("/api/dev/raiderio/sync", RunDevRaiderIOSyncAsync);
			app.MapGet("/dev/follow", (string? discordUserId, FollowFlowStateService states, WebUrlBuilder urls) =>
			{
				var state = states.Create(string.IsNullOrWhiteSpace(discordUserId) ? "dev-discord-user" : discordUserId, TimeSpan.FromMinutes(10));
				return Results.Redirect(urls.BuildPublicUrl("/follow/start", ("state", state.State)));
			});
		}

		app.MapFallback(ServeReactApp);
	}

	private static IResult ServeReactApp(WebUrlBuilder urls, IWebHostEnvironment environment, IServiceProvider services) =>
		WebPageRenderer.RenderApp(
			urls,
			services.GetService<IViteManifest>(),
			services.GetService<IViteDevServerStatus>(),
			isDevelopment: environment.IsDevelopment());

	private static async Task<IResult> GetSessionAsync(HttpContext context, WebUrlBuilder urls, IWebHostEnvironment environment)
	{
		var auth = await context.AuthenticateAsync().ConfigureAwait(false);
		return Results.Json(new SessionResponse(
			auth.Succeeded,
			environment.IsDevelopment(),
			urls.BuildPublicUrl("/"),
			urls.BuildPublicUrl("/signin"),
			urls.BuildPublicUrl("/signout"),
			urls.BuildPublicUrl("/follow/characters"),
			urls.BuildPublicUrl("/dev")));
	}

	private static async Task<IResult> GetHomeAsync(
		HttpContext context,
		IConfiguration config,
		IBlizzardProfileClient blizzard,
		CharacterRepository characters,
		CancellationToken cancellationToken)
	{
		var auth = await context.AuthenticateAsync().ConfigureAwait(false);
		if (!auth.Succeeded)
			return Results.Json(new HomeResponse("unauthenticated", [], [], null));

		var accessToken = await context.GetTokenAsync("access_token").ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(accessToken))
			return Results.Json(new HomeResponse("missing-token", [], [], "Your Battle.net session does not include an access token. Sign in again to view your characters."), statusCode: StatusCodes.Status401Unauthorized);

		if (IsWowProfileScopeExplicitlyMissing(auth.Properties))
			return Results.Json(new HomeResponse("missing-scope", [], [], "Battle.net did not grant the wow.profile scope. Sign in again and approve WoW profile access."), statusCode: StatusCodes.Status403Forbidden);

		IReadOnlyList<VerifiedCharacter> verifiedCharacters;
		try
		{
			verifiedCharacters = await blizzard.GetProfileCharactersAsync(accessToken, config["Blizzard:Region"] ?? "us", cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			return Results.Json(new HomeResponse("error", [], [], $"Battle.net profile lookup failed: {ex.Message}"), statusCode: StatusCodes.Status502BadGateway);
		}

		var existingCharacters = characters.GetCharacters(verifiedCharacters.Select(x => x.Key).ToList());
		var maxLevel = verifiedCharacters.Count == 0 ? 0 : verifiedCharacters.Max(x => x.Level) ?? 0;
		var followedCharacters = verifiedCharacters
			.Where(character => existingCharacters.TryGetValue(character.Key, out var existing) && existing.IsFollowed)
			.OrderBy(character => character.RealmDisplayName ?? character.Realm)
			.ThenBy(character => character.Name)
			.Select(character => ToCharacterDto(character, existingCharacters, maxLevel, characters))
			.ToList();
		var otherCharacters = verifiedCharacters
			.Where(character => !existingCharacters.TryGetValue(character.Key, out var existing) || !existing.IsFollowed)
			.OrderByDescending(character => character.Level)
			.ThenBy(character => character.Name)
			.Select(character => ToCharacterDto(character, existingCharacters, maxLevel, characters))
			.ToList();

		return Results.Json(new HomeResponse("ok", followedCharacters, otherCharacters, null));
	}

	private static IResult SignIn(string? returnUrl, WebUrlBuilder urls)
	{
		var redirectPath = string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//") ? "/" : returnUrl;
		return Results.Challenge(new AuthenticationProperties { RedirectUri = urls.BuildPublicUrl(redirectPath) }, ["Blizzard"]);
	}

	private static IResult SignOut(WebUrlBuilder urls) => Results.SignOut(
		new AuthenticationProperties { RedirectUri = urls.BuildPublicUrl("/") },
		["Cookies"]);

	private static IResult GetDevTools(CharacterRepository characters)
	{
		var followedCharacters = characters.GetFollowedCharacters()
			.OrderBy(x => x.RealmDisplayName ?? x.Realm)
			.ThenBy(x => x.Name)
			.Select(ToDevCharacterDto)
			.ToList();
		return Results.Json(new DevToolsResponse(followedCharacters));
	}

	private static async Task<IResult> RunDevRaiderIOSyncAsync(ISchedulerFactory schedulerFactory)
	{
		var scheduler = await schedulerFactory.GetScheduler().ConfigureAwait(false);
		await scheduler.TriggerJob(new JobKey(CheckRunsJob.JobName)).ConfigureAwait(false);
		return Results.Json(new MessageResponse("Raider.IO sync scheduled. The Quartz check job was triggered and will run in the background."));
	}

	private static Task<IResult> StartFollowAsync(string state, FollowFlowStateService states, WebUrlBuilder urls)
	{
		var consumed = states.Consume(state);
		if (consumed is null)
			return Task.FromResult<IResult>(Results.Redirect(urls.BuildPublicUrl("/follow/characters?error=invalid-link")));

		var properties = new AuthenticationProperties
		{
			RedirectUri = urls.BuildPublicUrl("/follow/characters"),
		};
		properties.Items[DiscordUserIdProperty] = consumed.DiscordUserId;
		properties.Items[ManagementSessionIdProperty] = GenerateToken();
		properties.Items[ManagementSessionExpiresAtProperty] = DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

		return Task.FromResult<IResult>(Results.Challenge(properties, ["Blizzard"]));
	}

	private static async Task<IResult> GetFollowCharactersAsync(
		HttpContext context,
		IConfiguration config,
		IBlizzardProfileClient blizzard,
		CharacterRepository characters,
		IAntiforgery antiforgery,
		CancellationToken cancellationToken)
	{
		var session = await GetManagementSessionAsync(context).ConfigureAwait(false);
		if (session is null)
		{
			var auth = await context.AuthenticateAsync().ConfigureAwait(false);
			return Results.Json(new CharacterManagementResponse("instructions", auth.Succeeded, [], null, null));
		}

		var accessToken = await context.GetTokenAsync("access_token").ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(accessToken))
			return Results.Json(new CharacterManagementResponse("missing-token", true, [], null, "Battle.net did not return an access token. Please start again and authorize the requested WoW profile scope."), statusCode: StatusCodes.Status400BadRequest);

		var authResult = await context.AuthenticateAsync().ConfigureAwait(false);
		if (IsWowProfileScopeExplicitlyMissing(authResult.Properties))
			return Results.Json(new CharacterManagementResponse("missing-scope", true, [], null, "Battle.net did not grant the wow.profile scope. Please start again and approve WoW profile access."), statusCode: StatusCodes.Status403Forbidden);

		IReadOnlyList<VerifiedCharacter> verifiedCharacters;
		try
		{
			verifiedCharacters = await blizzard.GetProfileCharactersAsync(accessToken, config["Blizzard:Region"] ?? "us", cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			return Results.Json(new CharacterManagementResponse("error", true, [], null, $"Battle.net profile lookup failed: {ex.Message}"), statusCode: StatusCodes.Status502BadGateway);
		}

		var verificationSetId = GenerateToken();
		characters.SaveVerifiedCharacterSet(session.SessionId, verificationSetId, verifiedCharacters, DateTime.UtcNow);
		var existingCharacters = characters.GetCharacters(verifiedCharacters.Select(x => x.Key).ToList());
		var maxLevel = verifiedCharacters.Count == 0 ? 0 : verifiedCharacters.Max(x => x.Level) ?? 0;
		var tokens = antiforgery.GetAndStoreTokens(context);
		var responseCharacters = verifiedCharacters
			.OrderBy(character => character.RealmDisplayName ?? character.Realm)
			.ThenByDescending(character => character.Level)
			.ThenBy(character => character.Name)
			.Select(character => ToCharacterDto(character, existingCharacters, maxLevel, characters))
			.ToList();

		return Results.Json(new CharacterManagementResponse(
			"ok",
			true,
			responseCharacters,
			new CharacterManagementForm(verificationSetId, tokens.RequestToken ?? string.Empty),
			null));
	}

	private static async Task<IResult> SaveCharactersAsync(
		HttpContext context,
		CharacterRepository characters,
		CharacterManagementService management,
		ICharacterFollowAnnouncer followAnnouncer,
		IAntiforgery antiforgery)
	{
		var session = await GetManagementSessionAsync(context).ConfigureAwait(false);
		if (session is null)
			return Results.Json(new ErrorResponse("Session expired. Run /follow in Discord to start again."), statusCode: StatusCodes.Status401Unauthorized);

		try
		{
			await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
		}
		catch (AntiforgeryValidationException)
		{
			return Results.Json(new ErrorResponse("The form token was invalid or expired. Reload the character picker and try again."), statusCode: StatusCodes.Status400BadRequest);
		}

		var request = await context.Request.ReadFromJsonAsync<SaveCharactersRequest>().ConfigureAwait(false);
		if (request is null || string.IsNullOrWhiteSpace(request.VerificationSetId))
			return Results.Json(new ErrorResponse("The verified character set was missing."), statusCode: StatusCodes.Status400BadRequest);

		var verifiedCharacters = characters.GetVerifiedCharacterSet(session.SessionId, request.VerificationSetId, TimeSpan.FromMinutes(30));
		if (verifiedCharacters.Count == 0)
			return Results.Json(new ErrorResponse("The verified character list expired. Reload the picker and try again."), statusCode: StatusCodes.Status400BadRequest);

		var selectedCharacters = new List<CharacterKey>();
		foreach (var value in request.Characters ?? [])
		{
			if (!CharacterKey.TryParse(value, out var key))
				return Results.Json(new ErrorResponse("A submitted character value was malformed."), statusCode: StatusCodes.Status400BadRequest);
			selectedCharacters.Add(key);
		}

		CharacterFollowUpdateResult result;
		try
		{
			result = management.UpdateFollowState(session.DiscordUserId, verifiedCharacters, selectedCharacters);
		}
		catch (InvalidOperationException)
		{
			return Results.Json(new ErrorResponse("A submitted character was not part of your verified Battle.net character list."), statusCode: StatusCodes.Status400BadRequest);
		}
		finally
		{
			characters.DeleteVerifiedCharacterSet(session.SessionId, request.VerificationSetId);
		}

		var maxLevel = verifiedCharacters.Count == 0 ? 0 : verifiedCharacters.Max(x => x.Level) ?? 0;
		var followedCharacters = result.Followed.Select(key => verifiedCharacters.First(character => character.Key == key)).ToList();
		var unfollowedCharacters = result.Unfollowed.Select(key => verifiedCharacters.First(character => character.Key == key)).ToList();
		await followAnnouncer.AnnounceCharactersFollowedAsync(followedCharacters, context.RequestAborted).ConfigureAwait(false);

		return Results.Json(new SaveCharactersResponse(
			followedCharacters.Select(character => ToSavedCharacterDto(character, maxLevel)).ToList(),
			unfollowedCharacters.Select(character => ToSavedCharacterDto(character, maxLevel)).ToList()));
	}

	private static bool IsWowProfileScopeExplicitlyMissing(AuthenticationProperties? properties)
	{
		if (properties is null || !properties.Items.TryGetValue("GrantedScope", out var scope) || string.IsNullOrWhiteSpace(scope))
			return false;

		return !scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Any(value => string.Equals(value, "wow.profile", StringComparison.OrdinalIgnoreCase));
	}

	private static async Task<ManagementSession?> GetManagementSessionAsync(HttpContext context)
	{
		var auth = await context.AuthenticateAsync().ConfigureAwait(false);
		if (!auth.Succeeded)
			return null;

		string? discordUserId = null;
		string? sessionId = null;
		if (auth.Properties is not null)
		{
			auth.Properties.Items.TryGetValue(DiscordUserIdProperty, out discordUserId);
			auth.Properties.Items.TryGetValue(ManagementSessionIdProperty, out sessionId);
		}
		discordUserId ??= context.User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(discordUserId) || string.IsNullOrWhiteSpace(sessionId))
			return null;

		if (!auth.Properties!.Items.TryGetValue(ManagementSessionExpiresAtProperty, out var expiresAtValue) ||
			!long.TryParse(expiresAtValue, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAtUnix) ||
			DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix) <= DateTimeOffset.UtcNow)
		{
			await context.SignOutAsync("Cookies").ConfigureAwait(false);
			return null;
		}

		return new ManagementSession(discordUserId, sessionId);
	}

	private static WebCharacterDto ToCharacterDto(VerifiedCharacter character, IReadOnlyDictionary<CharacterKey, Character> existingCharacters, int maxLevel, CharacterRepository repository)
	{
		var key = character.Key;
		var dbCharacter = existingCharacters.TryGetValue(key, out var existing) ? existing : null;
		var season = dbCharacter?.CurrentSeason;
		var dungeons = season is not null && dbCharacter is not null
			? repository.GetCharacterDungeonAchievements(dbCharacter.Id, season)
				.Select(x => new DungeonAchievementDto(x.DungeonName, x.DungeonShortName, x.DungeonSlug, x.HighestTimedKeyLevelSeen))
				.ToList()
			: [];

		return new WebCharacterDto(
			key.ToString(),
			character.Name,
			character.RealmDisplayName ?? character.Realm,
			key.Realm,
			key.Region,
			character.RenderUrl ?? dbCharacter?.RenderUrl,
			character.Level,
			maxLevel,
			character.Class ?? dbCharacter?.Class,
			dbCharacter?.IsFollowed == true,
			dbCharacter?.ErroringSince is not null,
			dbCharacter?.CurrentScore ?? 0,
			dbCharacter?.LastCheckedAt,
			dungeons);
	}

	private static SavedCharacterDto ToSavedCharacterDto(VerifiedCharacter character, int maxLevel) => new(
		character.Key.ToString(),
		character.Name,
		character.RealmDisplayName ?? character.Realm,
		character.Key.Realm,
		character.Key.Region,
		character.RenderUrl,
		character.Level,
		maxLevel,
		character.Class);

	private static DevCharacterDto ToDevCharacterDto(Character character) => new(
		character.Name,
		character.RealmDisplayName ?? character.Realm,
		character.Region,
		character.RenderUrl,
		character.Class,
		FormatDevLastChecked(character.LastCheckedAt));

	private static string FormatDevLastChecked(DateTime? lastCheckedAt)
	{
		if (lastCheckedAt is null)
			return "never";

		var elapsed = DateTime.UtcNow - DateTime.SpecifyKind(lastCheckedAt.Value, DateTimeKind.Utc);
		if (elapsed.TotalMinutes < 1)
			return "just now";
		if (elapsed.TotalHours < 1)
			return $"{(int)elapsed.TotalMinutes}m ago";
		if (elapsed.TotalDays < 1)
			return $"{(int)elapsed.TotalHours}h ago";
		return $"{(int)elapsed.TotalDays}d ago";
	}

	private static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
		.Replace('+', '-')
		.Replace('/', '_')
		.TrimEnd('=');

	private const string DiscordUserIdProperty = "DiscordUserId";
	private const string ManagementSessionIdProperty = "ManagementSessionId";
	private const string ManagementSessionExpiresAtProperty = "ManagementSessionExpiresAt";

	private sealed record ManagementSession(string DiscordUserId, string SessionId);
}

public sealed record SessionResponse(bool IsAuthenticated, bool IsDevelopment, string HomeUrl, string SignInUrl, string SignOutUrl, string ManageUrl, string DevUrl);
public sealed record HomeResponse(string Status, IReadOnlyList<WebCharacterDto> FollowedCharacters, IReadOnlyList<WebCharacterDto> OtherCharacters, string? Message);
public sealed record CharacterManagementResponse(string Status, bool IsAuthenticated, IReadOnlyList<WebCharacterDto> Characters, CharacterManagementForm? Form, string? Message);
public sealed record CharacterManagementForm(string VerificationSetId, string RequestToken);
public sealed record WebCharacterDto(string Key, string Name, string RealmDisplayName, string Realm, string Region, string? RenderUrl, int? Level, int MaxLevel, string? ClassName, bool Followed, bool IsErroring, double CurrentScore, DateTime? LastCheckedAt, IReadOnlyList<DungeonAchievementDto> DungeonAchievements);
public sealed record DungeonAchievementDto(string DungeonName, string? DungeonShortName, string DungeonSlug, int KeyLevel);
public sealed record SaveCharactersRequest(string VerificationSetId, string[]? Characters);
public sealed record SaveCharactersResponse(IReadOnlyList<SavedCharacterDto> Followed, IReadOnlyList<SavedCharacterDto> Unfollowed);
public sealed record SavedCharacterDto(string Key, string Name, string RealmDisplayName, string Realm, string Region, string? RenderUrl, int? Level, int MaxLevel, string? ClassName);
public sealed record DevToolsResponse(IReadOnlyList<DevCharacterDto> FollowedCharacters);
public sealed record DevCharacterDto(string Name, string RealmDisplayName, string Region, string? RenderUrl, string? ClassName, string LastCheckedText);
public sealed record MessageResponse(string Message);
public sealed record ErrorResponse(string Message);
