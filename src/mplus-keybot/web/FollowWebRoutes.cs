using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Quartz;

public static class FollowWebRoutes
{
	public static void MapFollowWebRoutes(this WebApplication app)
	{
		app.MapGet("/", ShowLandingPageAsync);
		app.MapGet("/signin", SignIn);
		app.MapGet("/signout", SignOut);
		app.MapGet("/follow/start", StartFollowAsync);
		app.MapGet("/follow/characters", ShowCharactersAsync);
		app.MapPost("/follow/characters", SaveCharactersAsync);

		if (app.Environment.IsDevelopment())
		{
			app.MapGet("/dev", ShowDevPageAsync);
			app.MapPost("/dev/raiderio/sync", RunDevRaiderIOSyncAsync);
			app.MapGet("/dev/follow", (string? discordUserId, FollowFlowStateService states, WebUrlBuilder urls) =>
			{
				var state = states.Create(string.IsNullOrWhiteSpace(discordUserId) ? "dev-discord-user" : discordUserId, TimeSpan.FromMinutes(10));
				return Results.Redirect(urls.BuildPublicUrl("/follow/start", ("state", state.State)));
			});
		}
	}

	private static async Task<IResult> ShowLandingPageAsync(
		HttpContext context,
		IConfiguration config,
		IBlizzardProfileClient blizzard,
		CharacterRepository characters,
		WebUrlBuilder urls,
		IWebHostEnvironment environment,
		CancellationToken cancellationToken)
	{
		var auth = await context.AuthenticateAsync().ConfigureAwait(false);
		var isAuthenticated = auth.Succeeded;
		var body = new StringBuilder();

		body.AppendLine("<div class=\"hero\">");
		body.AppendLine("<h1>🔑 mplus-keybot</h1>");
		body.AppendLine("<p>Track Mythic+ runs for your World of Warcraft characters and get announcements in Discord. To manage follows, run <code>/follow</code> in Discord.</p>");
		body.AppendLine("<div class=\"hero-actions\">");
		body.AppendLine($"<a class=\"btn btn-primary\" href=\"{Html(urls.BuildPublicUrl("/follow/characters"))}\">Manage Characters</a>");
		body.AppendLine("</div>");
		body.AppendLine("</div>");

		if (!isAuthenticated)
		{
			body.AppendLine("<div class=\"card\">");
			body.AppendLine("<div class=\"card-title\">View Your Characters</div>");
			body.AppendLine("<p>Sign in with Battle.net to see which of your verified characters are currently followed by this bot.</p>");
			body.AppendLine($"<p><a class=\"btn btn-primary\" href=\"{Html(urls.BuildPublicUrl("/signin"))}\">Sign in with Battle.net</a></p>");
			body.AppendLine("</div>");
			return WebPageRenderer.RenderPage(urls, "Home", body.ToString(), isAuthenticated, isDevelopment: environment.IsDevelopment());
		}

		var accessToken = await context.GetTokenAsync("access_token").ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(accessToken))
		{
			body.AppendLine("<div class=\"alert alert-error\">");
			body.AppendLine("<strong>Session expired</strong><br>Your Battle.net session does not include an access token. Sign in again to view your characters.");
			body.AppendLine("</div>");
			body.AppendLine($"<p><a class=\"btn btn-primary\" href=\"{Html(urls.BuildPublicUrl("/signin"))}\">Sign in again</a></p>");
			return WebPageRenderer.RenderPage(urls, "Home", body.ToString(), isAuthenticated, isDevelopment: environment.IsDevelopment());
		}

		if (IsWowProfileScopeExplicitlyMissing(auth.Properties))
		{
			body.AppendLine("<div class=\"alert alert-error\">");
			body.AppendLine("<strong>Missing WoW profile scope</strong><br>Battle.net did not grant the <code>wow.profile</code> scope. Sign in again and approve WoW profile access.");
			body.AppendLine("</div>");
			body.AppendLine($"<p><a class=\"btn btn-primary\" href=\"{Html(urls.BuildPublicUrl("/signin"))}\">Sign in again</a></p>");
			return WebPageRenderer.RenderPage(urls, "Home", body.ToString(), isAuthenticated, HttpStatusCode.Forbidden, isDevelopment: environment.IsDevelopment());
		}

		IReadOnlyList<VerifiedCharacter> verifiedCharacters;
		try
		{
			verifiedCharacters = await blizzard.GetProfileCharactersAsync(accessToken, config["Blizzard:Region"] ?? "us", cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			body.AppendLine("<div class=\"alert alert-error\">");
			body.AppendLine($"<strong>Unable to load characters</strong><br>Battle.net profile lookup failed: {Html(ex.Message)}");
			body.AppendLine("</div>");
			return WebPageRenderer.RenderPage(urls, "Home", body.ToString(), isAuthenticated, HttpStatusCode.BadGateway, isDevelopment: environment.IsDevelopment());
		}

		var existingCharacters = characters.GetCharacters(verifiedCharacters.Select(x => x.Key).ToList());
		var maxLevel = verifiedCharacters.Max(x => x.Level) ?? 0;
		var followedCharacters = verifiedCharacters
			.Where(character => existingCharacters.TryGetValue(character.Key, out var existing) && existing.IsFollowed)
			.OrderBy(character => character.RealmDisplayName ?? character.Realm)
			.ThenBy(character => character.Name)
			.ToList();

		if (followedCharacters.Count > 0)
		{
			body.AppendLine("<h2>Followed Characters</h2>");
			body.AppendLine("<div class=\"home-grid\">");
			foreach (var character in followedCharacters)
			{
				var renderUrl = character.RenderUrl ?? (existingCharacters.TryGetValue(character.Key, out var existing) ? existing.RenderUrl : null);
				var dbCharacter = existingCharacters.TryGetValue(character.Key, out var dbChar) ? dbChar : null;
				var isErroring = dbCharacter?.ErroringSince is not null;
				var season = dbCharacter?.CurrentSeason;
				var dungeonAchievements = season is not null && dbCharacter is not null
					? characters.GetCharacterDungeonAchievements(dbCharacter.Id, season)
						.Select(x => (x.DungeonName, x.HighestTimedKeyLevelSeen))
						.ToList()
					: (IReadOnlyList<(string, int)>)Array.Empty<(string, int)>();

				body.AppendLine(WebPageRenderer.RenderCharacterHomeCard(
					character.Name,
					character.RealmDisplayName ?? character.Realm,
					character.Key.Region,
					renderUrl,
					character.Level,
					maxLevel,
					isErroring,
					dbCharacter?.CurrentScore ?? 0,
					dbCharacter?.LastCheckedAt,
					dungeonAchievements));
			}
			body.AppendLine("</div>");
		}

		var otherCharacters = verifiedCharacters.Except(followedCharacters).OrderByDescending(character => character.Level).ThenBy(character => character.Name).ToList();
		if (otherCharacters.Count > 0)
		{
			body.AppendLine("<h2>Other Verified Characters</h2>");
			body.AppendLine("<div class=\"character-grid\">");
			foreach (var character in otherCharacters)
			{
				var renderUrl = character.RenderUrl ?? (existingCharacters.TryGetValue(character.Key, out var existing) ? existing.RenderUrl : null);
				body.AppendLine(WebPageRenderer.RenderCharacterReadonlyCard(
					character.Name,
					character.RealmDisplayName ?? character.Realm,
					character.Key.Region,
					renderUrl,
					character.Level,
					maxLevel));
			}
			body.AppendLine("</div>");
		}

		if (verifiedCharacters.Count == 0)
		{
			body.AppendLine("<div class=\"empty-state\">");
			body.AppendLine("<div class=\"empty-state-icon\">🏳️</div>");
			body.AppendLine("<p>No retail WoW characters were returned by Battle.net for this account.</p>");
			body.AppendLine("</div>");
		}

		return WebPageRenderer.RenderPage(urls, "Home", body.ToString(), isAuthenticated, isDevelopment: environment.IsDevelopment());
	}

	private static IResult SignIn(string? returnUrl, WebUrlBuilder urls)
	{
		var redirectPath = string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//") ? "/" : returnUrl;
		return Results.Challenge(new AuthenticationProperties { RedirectUri = urls.BuildPublicUrl(redirectPath) }, ["Blizzard"]);
	}

	private static IResult SignOut(WebUrlBuilder urls) => Results.SignOut(
		new AuthenticationProperties { RedirectUri = urls.BuildPublicUrl("/") },
		["Cookies"]);

	private static Task<IResult> ShowDevPageAsync(WebUrlBuilder urls, CharacterRepository characters)
	{
		var followedCharacters = characters.GetFollowedCharacters();
		var body = new StringBuilder();
		body.AppendLine("<div class=\"page-header\">");
		body.AppendLine("<h1>Development Tools</h1>");
		body.AppendLine("<p>Local-only helpers for exercising the follow workflow and refreshing Raider.IO character data without a Discord bot connection.</p>");
		body.AppendLine("</div>");

		body.AppendLine("<div class=\"card\">");
		body.AppendLine("<div class=\"card-title\">Follow management flow</div>");
		body.AppendLine("<p>Create a short-lived dev follow link for a test Discord user, then continue through the normal Battle.net authorization flow.</p>");
		body.AppendLine($"<p><a class=\"btn btn-primary\" href=\"{Html(urls.BuildPublicUrl("/dev/follow", ("discordUserId", "test-user")))}\">Start Dev Flow</a></p>");
		body.AppendLine("</div>");

		body.AppendLine("<div class=\"card\">");
		body.AppendLine("<div class=\"card-title\">Raider.IO sync</div>");
		body.AppendLine($"<p>Schedule the Raider.IO check job immediately for <strong>{followedCharacters.Count}</strong> followed character{(followedCharacters.Count == 1 ? string.Empty : "s")}. In local development, the job refreshes data without posting to Discord when no bot token is configured.</p>");
		body.AppendLine($"<form method=\"post\" action=\"{Html(urls.BuildPublicUrl("/dev/raiderio/sync"))}\">");
		body.AppendLine("<button type=\"submit\" class=\"btn btn-primary\">Force Raider.IO Sync</button>");
		body.AppendLine("</form>");
		body.AppendLine("</div>");
		AppendDevCharacterList(body, "Currently Followed", followedCharacters);

		return Task.FromResult(WebPageRenderer.RenderPage(urls, "Development Tools", body.ToString(), false, isDevelopment: true));
	}

	private static async Task<IResult> RunDevRaiderIOSyncAsync(WebUrlBuilder urls, ISchedulerFactory schedulerFactory)
	{
		var scheduler = await schedulerFactory.GetScheduler().ConfigureAwait(false);
		await scheduler.TriggerJob(new JobKey(CheckRunsJob.JobName)).ConfigureAwait(false);

		var body = new StringBuilder();
		body.AppendLine($"<a class=\"back-link\" href=\"{Html(urls.BuildPublicUrl("/dev"))}\">← Dev Tools</a>");
		body.AppendLine("<div class=\"alert alert-success\">");
		body.AppendLine("<strong>Raider.IO sync scheduled.</strong> The Quartz check job was triggered and will run in the background.");
		body.AppendLine("</div>");
		body.AppendLine($"<p><a class=\"btn btn-secondary\" href=\"{Html(urls.BuildPublicUrl("/"))}\">View Home</a></p>");
		return WebPageRenderer.RenderPage(urls, "Raider.IO Sync Scheduled", body.ToString(), false, isDevelopment: true);
	}

	private static Task<IResult> StartFollowAsync(string state, FollowFlowStateService states, WebUrlBuilder urls)
	{
		var consumed = states.Consume(state);
		if (consumed is null)
		{
			var body = "<div class=\"alert alert-error\"><strong>Invalid link</strong><br>This follow management link is invalid, expired, or has already been used. Run <code>/follow</code> in Discord to get a new link.</div>";
			return Task.FromResult(WebPageRenderer.RenderPage(urls, "Invalid Link", body, false, HttpStatusCode.BadRequest));
		}

		var properties = new AuthenticationProperties
		{
			RedirectUri = urls.BuildPublicUrl("/follow/characters"),
		};
		properties.Items[DiscordUserIdProperty] = consumed.DiscordUserId;
		properties.Items[ManagementSessionIdProperty] = GenerateToken();
		properties.Items[ManagementSessionExpiresAtProperty] = DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

		return Task.FromResult(Results.Challenge(properties, ["Blizzard"]));
	}

	private static async Task<IResult> ShowCharactersAsync(
		HttpContext context,
		IConfiguration config,
		IBlizzardProfileClient blizzard,
		CharacterRepository characters,
		WebUrlBuilder urls,
		IAntiforgery antiforgery,
		CancellationToken cancellationToken)
	{
		var session = await GetManagementSessionAsync(context).ConfigureAwait(false);
		if (session is null)
		{
			var auth = await context.AuthenticateAsync().ConfigureAwait(false);
			return ShowCharacterManagementInstructions(urls, auth.Succeeded);
		}

		return await ShowCharacterPickerAsync(context, session, config, blizzard, characters, urls, antiforgery, cancellationToken).ConfigureAwait(false);
	}

	private static IResult ShowCharacterManagementInstructions(WebUrlBuilder urls, bool isAuthenticated)
	{
		var body = new StringBuilder();
		body.AppendLine($"<a class=\"back-link\" href=\"{Html(urls.BuildPublicUrl("/"))}\">← Home</a>");
		body.AppendLine("<div class=\"page-header\">");
		body.AppendLine("<h1>Manage Characters</h1>");
		body.AppendLine("<p>Character management starts from Discord so the bot can connect your Battle.net characters to your Discord user.</p>");
		body.AppendLine("</div>");
		body.AppendLine("<div class=\"card\">");
		body.AppendLine("<div class=\"card-title\">Start from Discord</div>");
		body.AppendLine("<p>Run <code>/follow</code> in Discord. The bot will send you a private, short-lived link to sign in with Battle.net and choose which characters to follow.</p>");
		body.AppendLine("</div>");

		return WebPageRenderer.RenderPage(urls, "Manage Characters", body.ToString(), isAuthenticated);
	}

	private static async Task<IResult> ShowCharacterPickerAsync(
		HttpContext context,
		ManagementSession session,
		IConfiguration config,
		IBlizzardProfileClient blizzard,
		CharacterRepository characters,
		WebUrlBuilder urls,
		IAntiforgery antiforgery,
		CancellationToken cancellationToken)
	{
		var accessToken = await context.GetTokenAsync("access_token").ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(accessToken))
		{
			var errorBody = "<div class=\"alert alert-error\"><strong>Missing token</strong><br>Battle.net did not return an access token. Please start again and authorize the requested WoW profile scope.</div>";
			return WebPageRenderer.RenderPage(urls, "Missing Token", errorBody, true, HttpStatusCode.BadRequest);
		}

		var auth = await context.AuthenticateAsync().ConfigureAwait(false);
		if (IsWowProfileScopeExplicitlyMissing(auth.Properties))
		{
			var errorBody = "<div class=\"alert alert-error\"><strong>Missing WoW profile scope</strong><br>Battle.net did not grant the <code>wow.profile</code> scope. Please start again and approve WoW profile access.</div>";
			return WebPageRenderer.RenderPage(urls, "Missing Scope", errorBody, true, HttpStatusCode.Forbidden);
		}

		IReadOnlyList<VerifiedCharacter> verifiedCharacters;
		try
		{
			verifiedCharacters = await blizzard.GetProfileCharactersAsync(accessToken, config["Blizzard:Region"] ?? "us", cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			var errorBody = $"<div class=\"alert alert-error\"><strong>Unable to load characters</strong><br>Battle.net profile lookup failed: {Html(ex.Message)}</div>";
			return WebPageRenderer.RenderPage(urls, "Error", errorBody, true, HttpStatusCode.BadGateway);
		}

		var verificationSetId = GenerateToken();
		characters.SaveVerifiedCharacterSet(session.SessionId, verificationSetId, verifiedCharacters, DateTime.UtcNow);
		var existingCharacters = characters.GetCharacters(verifiedCharacters.Select(x => x.Key).ToList());
		var tokens = antiforgery.GetAndStoreTokens(context);

		var body = new StringBuilder();
		body.AppendLine($"<a class=\"back-link\" href=\"{Html(urls.BuildPublicUrl("/"))}\">← Home</a>");
		body.AppendLine("<div class=\"page-header\">");
		body.AppendLine("<h1>Manage Characters</h1>");
		body.AppendLine("<p>Select the characters this bot should follow. Only characters returned by this Battle.net sign-in can be changed.</p>");
		body.AppendLine("</div>");

		if (verifiedCharacters.Count == 0)
		{
			body.AppendLine("<div class=\"empty-state\">");
			body.AppendLine("<div class=\"empty-state-icon\">🏳️</div>");
			body.AppendLine("<p>No retail WoW characters were returned by Battle.net for this account.</p>");
			body.AppendLine("</div>");
		}
		else
		{
			var maxLevel = verifiedCharacters.Max(x => x.Level) ?? 0;

			body.AppendLine($"<form method=\"post\" action=\"{Html(urls.BuildPublicUrl("/follow/characters"))}\">");
			body.AppendLine($"<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{Html(tokens.RequestToken ?? string.Empty)}\">");
			body.AppendLine($"<input type=\"hidden\" name=\"verificationSetId\" value=\"{Html(verificationSetId)}\">");

			body.AppendLine("<div class=\"toolbar\">");
			body.AppendLine("<div class=\"toolbar-group\">");
			body.AppendLine("<span class=\"toolbar-label\">Search</span>");
			body.AppendLine("<input type=\"text\" id=\"char-filter\" class=\"toolbar-input\" placeholder=\"Filter by name or realm...\">");
			body.AppendLine("</div>");
			body.AppendLine("<div class=\"toolbar-group\">");
			body.AppendLine("<span class=\"toolbar-label\">Sort</span>");
			body.AppendLine("<select id=\"char-sort\" class=\"toolbar-select\">");
			body.AppendLine("<option value=\"level\">Level (high → low)</option>");
			body.AppendLine("<option value=\"name\">Name (A → Z)</option>");
			body.AppendLine("</select>");
			body.AppendLine("</div>");
			body.AppendLine("<span class=\"toolbar-count\" id=\"char-count\"></span>");
			body.AppendLine("</div>");

			foreach (var realmGroup in verifiedCharacters.GroupBy(x => x.RealmDisplayName ?? x.Realm).OrderBy(x => x.Key))
			{
				body.AppendLine($"<div class=\"realm-group\" data-realm=\"{Html(realmGroup.Key)}\">");
				body.AppendLine($"<h3>{Html(realmGroup.Key)}</h3>");
				body.AppendLine("<div class=\"character-grid\">");
				foreach (var character in realmGroup.OrderByDescending(x => x.Level).ThenBy(x => x.Name))
				{
					var key = character.Key;
					var isChecked = existingCharacters.TryGetValue(key, out var existing) && existing.IsFollowed;
					body.AppendLine(WebPageRenderer.RenderCharacterCard(
						key.ToString(),
						character.Name,
						character.RealmDisplayName ?? character.Realm,
						key.Region,
						isChecked,
						character.RenderUrl,
						character.Level,
						maxLevel));
				}
				body.AppendLine("</div>");
				body.AppendLine("</div>");
			}

			body.AppendLine("<p><button type=\"submit\" class=\"btn btn-primary\">Save Follow Settings</button></p>");
			body.AppendLine("</form>");
		}

		return WebPageRenderer.RenderPage(urls, "Manage Characters", body.ToString(), true);
	}

	private static async Task<IResult> SaveCharactersAsync(
		HttpContext context,
		CharacterRepository characters,
		CharacterManagementService management,
		ICharacterFollowAnnouncer followAnnouncer,
		WebUrlBuilder urls,
		IAntiforgery antiforgery)
	{
		var session = await GetManagementSessionAsync(context).ConfigureAwait(false);
		if (session is null)
		{
			var errorBody = "<div class=\"alert alert-error\"><strong>Session expired</strong><br>Your management session expired. Run <code>/follow</code> in Discord to start again.</div>";
			return WebPageRenderer.RenderPage(urls, "Session Expired", errorBody, true, HttpStatusCode.Unauthorized);
		}

		try
		{
			await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
		}
		catch (AntiforgeryValidationException)
		{
			var errorBody = "<div class=\"alert alert-error\"><strong>Invalid form</strong><br>The form token was invalid or expired. Reload the character picker and try again.</div>";
			return WebPageRenderer.RenderPage(urls, "Invalid Form", errorBody, true, HttpStatusCode.BadRequest);
		}

		var form = await context.Request.ReadFormAsync().ConfigureAwait(false);
		var verificationSetId = form["verificationSetId"].ToString();
		if (string.IsNullOrWhiteSpace(verificationSetId))
		{
			var errorBody = "<div class=\"alert alert-error\"><strong>Invalid form</strong><br>The verified character set was missing.</div>";
			return WebPageRenderer.RenderPage(urls, "Invalid Form", errorBody, true, HttpStatusCode.BadRequest);
		}

		var verifiedCharacters = characters.GetVerifiedCharacterSet(session.SessionId, verificationSetId, TimeSpan.FromMinutes(30));
		if (verifiedCharacters.Count == 0)
		{
			var errorBody = "<div class=\"alert alert-error\"><strong>Verification expired</strong><br>The verified character list expired. Reload the picker and try again.</div>";
			return WebPageRenderer.RenderPage(urls, "Verification Expired", errorBody, true, HttpStatusCode.BadRequest);
		}

		var selectedCharacters = new List<CharacterKey>();
		foreach (var value in form["characters"])
		{
			if (!CharacterKey.TryParse(value!, out var key))
			{
				var errorBody = "<div class=\"alert alert-error\"><strong>Invalid form</strong><br>A submitted character value was malformed.</div>";
				return WebPageRenderer.RenderPage(urls, "Invalid Form", errorBody, true, HttpStatusCode.BadRequest);
			}
			selectedCharacters.Add(key);
		}

		CharacterFollowUpdateResult result;
		try
		{
			result = management.UpdateFollowState(session.DiscordUserId, verifiedCharacters, selectedCharacters);
		}
		catch (InvalidOperationException)
		{
			var errorBody = "<div class=\"alert alert-error\"><strong>Invalid form</strong><br>A submitted character was not part of your verified Battle.net character list.</div>";
			return WebPageRenderer.RenderPage(urls, "Invalid Form", errorBody, true, HttpStatusCode.BadRequest);
		}
		finally
		{
			characters.DeleteVerifiedCharacterSet(session.SessionId, verificationSetId);
		}

		var followedCharacters = result.Followed
			.Select(key => verifiedCharacters.First(character => character.Key == key))
			.ToList();
		await followAnnouncer.AnnounceCharactersFollowedAsync(followedCharacters, context.RequestAborted).ConfigureAwait(false);

		var body = new StringBuilder();
		body.AppendLine($"<a class=\"back-link\" href=\"{Html(urls.BuildPublicUrl("/"))}\">← Home</a>");
		body.AppendLine("<div class=\"alert alert-success\">");
		body.AppendLine("<strong>Settings saved!</strong> Your follow settings have been updated.");
		body.AppendLine("</div>");
		body.AppendLine($"<p><a class=\"btn btn-secondary\" href=\"{Html(urls.BuildPublicUrl("/follow/characters"))}\">Continue managing characters</a></p>");

		AppendCharacterUpdateSection(body, "Now Followed", result.Followed);
		AppendCharacterUpdateSection(body, "Now Unfollowed", result.Unfollowed);

		if (result.Followed.Count == 0 && result.Unfollowed.Count == 0)
		{
			body.AppendLine("<div class=\"empty-state\">");
			body.AppendLine("<p>No follow states changed.</p>");
			body.AppendLine("</div>");
		}

		return WebPageRenderer.RenderPage(urls, "Settings Saved", body.ToString(), true);
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

	private static string LevelBadgeClass(int level, int? maxLevel) => maxLevel.HasValue && level >= maxLevel.Value ? " level-max" : string.Empty;

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

	private static void AppendDevCharacterList(StringBuilder body, string title, IReadOnlyList<Character> characters)
	{
		if (characters.Count == 0)
			return;

		body.AppendLine("<div class=\"card\">");
		body.AppendLine($"<div class=\"card-title\">{Html(title)}</div>");
		body.AppendLine("<ul class=\"character-list\">");
		foreach (var character in characters.OrderBy(x => x.RealmDisplayName ?? x.Realm).ThenBy(x => x.Name))
		{
			body.AppendLine("<li>");
			body.AppendLine(WebPageRenderer.AvatarHtml(character.Name, character.RenderUrl));
			body.AppendLine("<div class=\"character-info\">");
			body.AppendLine($"<div class=\"character-name\">{Html(character.Name)}</div>");
			body.AppendLine($"<div class=\"character-meta\">{Html(character.RealmDisplayName ?? character.Realm)} · {Html(character.Region.ToUpperInvariant())} · checked {Html(FormatDevLastChecked(character.LastCheckedAt))}</div>");
			body.AppendLine("</div>");
			body.AppendLine("</li>");
		}
		body.AppendLine("</ul>");
		body.AppendLine("</div>");
	}

	private static void AppendCharacterUpdateSection(StringBuilder body, string title, IReadOnlyList<CharacterKey> characters)
	{
		if (characters.Count == 0)
			return;

		body.AppendLine("<div class=\"card\">");
		body.AppendLine($"<div class=\"card-title\">{Html(title)}</div>");
		body.AppendLine("<ul class=\"character-list\">");
		foreach (var character in characters.OrderBy(x => x.Realm).ThenBy(x => x.Name))
		{
			body.AppendLine("<li>");
			body.AppendLine(WebPageRenderer.AvatarHtml(character.Name));
			body.AppendLine("<div class=\"character-info\">");
			body.AppendLine($"<div class=\"character-name\">{Html(character.Name)}</div>");
			body.AppendLine($"<div class=\"character-meta\">{Html(character.Realm)} · {Html(character.Region.ToUpperInvariant())}</div>");
			body.AppendLine("</div>");
			body.AppendLine("</li>");
		}
		body.AppendLine("</ul>");
		body.AppendLine("</div>");
	}

	private static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
		.Replace('+', '-')
		.Replace('/', '_')
		.TrimEnd('=');

	private static string Html(string value) => WebPageRenderer.Html(value);

	private const string DiscordUserIdProperty = "DiscordUserId";
	private const string ManagementSessionIdProperty = "ManagementSessionId";
	private const string ManagementSessionExpiresAtProperty = "ManagementSessionExpiresAt";

	private sealed record ManagementSession(string DiscordUserId, string SessionId);
}
