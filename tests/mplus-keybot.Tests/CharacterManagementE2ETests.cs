using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using SQLite;
using Vite.AspNetCore;

namespace mplus_keybot.Tests;

public sealed class CharacterManagementE2ETests
{
	[PlaywrightE2EFact]
	public async Task CharacterPickerFiltersSortsAndSavesFollowSelection()
	{
		var characters = new[]
		{
			new VerifiedCharacter("us", "Hyjal", "Keela", 101, "Hyjal", 80),
			new VerifiedCharacter("us", "Area 52", "Newmage", 202, "Area 52", 70),
			new VerifiedCharacter("us", "Hyjal", "Bearcat", 303, "Hyjal", 60),
		};
		await using var app = await CharacterManagementTestApp.StartAsync(characters);
		app.SeedFollowedCharacter(characters[0]);
		await using var browser = await LaunchChromiumAsync();
		await using var context = await browser.Browser.NewContextAsync(new() { BaseURL = app.BaseUrl });
		var page = await context.NewPageAsync();

		await page.GotoAsync("/follow/characters");

		await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Manage Characters" })).ToBeVisibleAsync();
		await Expect(page.Locator("#char-count")).ToHaveTextAsync("3 characters");
		await Expect(page.Locator(".character-card[data-name='Keela'] input")).ToBeCheckedAsync();
		await Expect(page.Locator(".character-card[data-name='Newmage'] input")).Not.ToBeCheckedAsync();

		await page.Locator("#char-filter").FillAsync("area");
		await Expect(page.Locator(".character-card:not(.hidden)")).ToHaveCountAsync(1);
		await Expect(page.Locator(".character-card:not(.hidden)")).ToContainTextAsync("Newmage");
		await Expect(page.Locator("#char-count")).ToHaveTextAsync("1 character");

		await page.Locator("#char-filter").FillAsync(string.Empty);
		await page.Locator("#char-sort").SelectOptionAsync("name");
		await Expect(page.Locator(".realm-group").First.Locator("h3")).ToHaveTextAsync("All Characters");
		var sortedNames = await page.Locator(".realm-group").First.Locator(".character-card").EvaluateAllAsync<string[]>(
			"cards => cards.map(card => card.getAttribute('data-name'))");
		Assert.Equal(["Bearcat", "Keela", "Newmage"], sortedNames);

		await page.Locator(".character-card[data-name='Newmage']").ClickAsync();
		await page.Locator(".character-card[data-name='Keela']").ClickAsync();
		await page.GetByRole(AriaRole.Button, new() { Name = "Save Follow Settings" }).ClickAsync();

		await Expect(page.GetByText("Settings saved!")).ToBeVisibleAsync();
		await Expect(page.GetByText("Now Followed")).ToBeVisibleAsync();
		await Expect(page.GetByText("Newmage")).ToBeVisibleAsync();
		await Expect(page.GetByText("Now Unfollowed")).ToBeVisibleAsync();
		await Expect(page.GetByText("Keela")).ToBeVisibleAsync();

		Assert.False(app.Repository.GetCharacter(characters[0].Key)!.IsFollowed);
		Assert.True(app.Repository.GetCharacter(characters[1].Key)!.IsFollowed);
		Assert.Null(app.Repository.GetCharacter(characters[2].Key));
		var announced = Assert.Single(app.FollowAnnouncer.Announcements);
		var announcedCharacter = Assert.Single(announced.Characters);
		Assert.Equal(characters[1].Key, announcedCharacter.Key);
		Assert.Equal(characters[1].RealmDisplayName, announcedCharacter.RealmDisplayName);
	}

	[PlaywrightE2EFact]
	public async Task CharacterPickerCanContinueManagingAndSaveAgainAfterConfirmation()
	{
		var characters = new[]
		{
			new VerifiedCharacter("us", "Hyjal", "Keela", 101, "Hyjal", 80),
			new VerifiedCharacter("us", "Area 52", "Newmage", 202, "Area 52", 70),
		};
		await using var app = await CharacterManagementTestApp.StartAsync(characters);
		await using var browser = await LaunchChromiumAsync();
		await using var context = await browser.Browser.NewContextAsync(new() { BaseURL = app.BaseUrl });
		var page = await context.NewPageAsync();

		await page.GotoAsync("/follow/characters");
		await page.Locator(".character-card[data-name='Keela']").ClickAsync();
		await page.GetByRole(AriaRole.Button, new() { Name = "Save Follow Settings" }).ClickAsync();

		await Expect(page.GetByText("Settings saved!")).ToBeVisibleAsync();
		await Expect(page.GetByText("Now Followed")).ToBeVisibleAsync();
		await Expect(page.GetByText("Keela")).ToBeVisibleAsync();

		await page.GetByRole(AriaRole.Button, new() { Name = "Continue managing characters" }).ClickAsync();

		await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Manage Characters" })).ToBeVisibleAsync();
		await Expect(page.Locator(".character-card[data-name='Keela'] input")).ToBeCheckedAsync();
		await Expect(page.Locator(".character-card[data-name='Newmage'] input")).Not.ToBeCheckedAsync();

		await page.Locator(".character-card[data-name='Newmage']").ClickAsync();
		await page.GetByRole(AriaRole.Button, new() { Name = "Save Follow Settings" }).ClickAsync();

		await Expect(page.GetByText("Settings saved!")).ToBeVisibleAsync();
		await Expect(page.GetByText("Now Followed")).ToBeVisibleAsync();
		await Expect(page.GetByText("Newmage")).ToBeVisibleAsync();
		Assert.True(app.Repository.GetCharacter(characters[0].Key)!.IsFollowed);
		Assert.True(app.Repository.GetCharacter(characters[1].Key)!.IsFollowed);
		Assert.Collection(
			app.FollowAnnouncer.Announcements,
			announcement => Assert.Equal(characters[0].Key, Assert.Single(announcement.Characters).Key),
			announcement => Assert.Equal(characters[1].Key, Assert.Single(announcement.Characters).Key));
	}

	[PlaywrightE2EFact]
	public async Task HomePageShowsFollowedCharacterProgress()
	{
		var followed = new VerifiedCharacter("us", "Hyjal", "Keela", 101, "Hyjal", 80);
		var alt = new VerifiedCharacter("us", "Area 52", "Newmage", 202, "Area 52", 70);
		await using var app = await CharacterManagementTestApp.StartAsync([followed, alt]);
		app.SeedFollowedCharacter(followed, character =>
		{
			character.CurrentScore = 2468;
			character.LastCheckedAt = DateTime.UtcNow.AddMinutes(-5);
		});
		await using var browser = await LaunchChromiumAsync();
		await using var context = await browser.Browser.NewContextAsync(new() { BaseURL = app.BaseUrl });
		var page = await context.NewPageAsync();

		await page.GotoAsync("/");

		await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Manage Characters" }).First).ToBeVisibleAsync();
		await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Character Progress" })).ToBeVisibleAsync();
		await Expect(page.Locator(".character-row", new() { HasTextString = "Keela" })).ToBeVisibleAsync();
		await Expect(page.Locator(".character-row", new() { HasTextString = "🏆 2468" })).ToBeVisibleAsync();
	}

	[PlaywrightE2EFact]
	public async Task HomePageWorksWithPublicBasePath()
	{
		var followed = new VerifiedCharacter("us", "Hyjal", "Keela", 101, "Hyjal", 80);
		await using var app = await CharacterManagementTestApp.StartAsync([followed], publicPathBase: "/mplus-keybot");
		app.SeedFollowedCharacter(followed);
		await using var browser = await LaunchChromiumAsync();
		await using var context = await browser.Browser.NewContextAsync(new() { BaseURL = app.BaseUrl });
		var page = await context.NewPageAsync();

		await page.GotoAsync("/mplus-keybot/");

		await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Manage Characters" }).First).ToBeVisibleAsync();
		await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Character Progress" })).ToBeVisibleAsync();
		await Expect(page.Locator(".character-row", new() { HasTextString = "Keela" })).ToBeVisibleAsync();
	}

	[PlaywrightE2EFact]
	public async Task CharacterManagementEntryExplainsDiscordFlowWithoutManagementSession()
	{
		await using var app = await CharacterManagementTestApp.StartAsync([]);
		await using var browser = await LaunchChromiumAsync();
		await using var context = await browser.Browser.NewContextAsync(new() { BaseURL = app.BaseUrl });
		await context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["x-no-management-session"] = "1" });
		var page = await context.NewPageAsync();

		await page.GotoAsync("/follow/characters");

		await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Manage Characters" })).ToBeVisibleAsync();
		await Expect(page.GetByText("Start from Discord")).ToBeVisibleAsync();
		await Expect(page.GetByText("Run /follow in Discord")).ToBeVisibleAsync();
	}

	private static async Task<PlaywrightBrowserSession> LaunchChromiumAsync()
	{
		var playwright = await Playwright.CreateAsync();
		try
		{
			var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
			return new PlaywrightBrowserSession(browser, playwright);
		}
		catch
		{
			playwright.Dispose();
			throw;
		}
	}

	private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

	private sealed class PlaywrightE2EFactAttribute : FactAttribute
	{
		public PlaywrightE2EFactAttribute()
		{
			if (!PlaywrightBrowserSession.IsChromiumInstalled())
				Skip = "Install Playwright Chromium to run browser e2e tests.";
		}
	}

	private sealed class PlaywrightBrowserSession : IAsyncDisposable
	{
		public PlaywrightBrowserSession(IBrowser browser, IPlaywright playwright)
		{
			Browser = browser;
			m_playwright = playwright;
		}

		public IBrowser Browser { get; }

		public static bool IsChromiumInstalled()
		{
			try
			{
				using var playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
				return File.Exists(playwright.Chromium.ExecutablePath);
			}
			catch
			{
				return false;
			}
		}

		public async ValueTask DisposeAsync()
		{
			await Browser.DisposeAsync();
			m_playwright.Dispose();
		}

		private readonly IPlaywright m_playwright;
	}

	private sealed class CharacterManagementTestApp : IAsyncDisposable
	{
		private CharacterManagementTestApp(WebApplication app, SQLiteConnection db, string databasePath, string baseUrl, RecordingCharacterFollowAnnouncer followAnnouncer)
		{
			m_app = app;
			m_db = db;
			m_databasePath = databasePath;
			BaseUrl = baseUrl;
			FollowAnnouncer = followAnnouncer;
			Repository = app.Services.GetRequiredService<CharacterRepository>();
		}

		public string BaseUrl { get; }

		public RecordingCharacterFollowAnnouncer FollowAnnouncer { get; }

		public CharacterRepository Repository { get; }

		public static async Task<CharacterManagementTestApp> StartAsync(IReadOnlyList<VerifiedCharacter> verifiedCharacters, string publicPathBase = "")
		{
			var baseUrl = GetAvailableLoopbackUrl();
			var databasePath = Path.Combine(Path.GetTempPath(), $"mplus-keybot-e2e-{Guid.NewGuid():N}.db");
			var db = new SQLiteConnection(databasePath);
			CreateTables(db);

			var builder = WebApplication.CreateBuilder(new WebApplicationOptions
			{
				EnvironmentName = "Development",
				ApplicationName = typeof(CharacterManagementE2ETests).Assembly.GetName().Name,
				WebRootPath = WebAssetPaths.GetDistDirectory(Directory.GetCurrentDirectory()),
			});
			builder.WebHost.UseUrls(baseUrl);
			builder.Logging.ClearProviders();
			builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Web:PublicBaseUrl"] = baseUrl + publicPathBase,
				["Blizzard:Region"] = "us",
			});

			builder.Services.AddSingleton(db);
			var followAnnouncer = new RecordingCharacterFollowAnnouncer();
			builder.Services.AddSingleton<CharacterRepository>();
			builder.Services.AddSingleton<CharacterManagementService>();
			builder.Services.AddSingleton<ICharacterFollowAnnouncer>(followAnnouncer);
			builder.Services.AddSingleton<FollowFlowStateService>();
			builder.Services.AddSingleton<WebUrlBuilder>();
			builder.Services.AddSingleton<IBlizzardProfileClient>(new FakeBlizzardProfileClient(verifiedCharacters));
			builder.Services.AddAntiforgery(options =>
			{
				options.Cookie.Name = ".mplus-keybot.e2e-antiforgery";
				options.Cookie.SameSite = SameSiteMode.Lax;
				options.Cookie.SecurePolicy = CookieSecurePolicy.None;
			});
			builder.Services
				.AddAuthentication(options =>
				{
					options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
					options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
				})
				.AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
			builder.Services.AddAuthorization();
			builder.Services.AddViteServices();

			var app = builder.Build();
			var webUrls = app.Services.GetRequiredService<WebUrlBuilder>();
			var requestPathBase = !string.IsNullOrWhiteSpace(webUrls.PathBase)
				? webUrls.PathBase
				: webUrls.CookiePath == "/" ? string.Empty : webUrls.CookiePath;
			if (!string.IsNullOrWhiteSpace(requestPathBase))
				app.UsePathBase(requestPathBase);
			app.UseRouting();
			app.UseAuthentication();
			app.UseAuthorization();
			app.MapFollowWebRoutes();
			await app.StartAsync().ConfigureAwait(false);
			return new CharacterManagementTestApp(app, db, databasePath, baseUrl, followAnnouncer);
		}

		public void SeedFollowedCharacter(VerifiedCharacter character, Action<Character>? configure = null)
		{
			var followed = Repository.UpsertFollowedCharacter(character, "discord-e2e", DateTime.UtcNow);
			configure?.Invoke(followed);
			m_db.Update(followed);
		}

		public async ValueTask DisposeAsync()
		{
			await m_app.DisposeAsync();
			m_db.Dispose();
			File.Delete(m_databasePath);
		}

		private static void CreateTables(SQLiteConnection db)
		{
			db.CreateTable<Character>();
			db.CreateTable<CharacterAchievementState>();
			db.CreateTable<CharacterDungeonAchievementState>();
			db.CreateTable<CharacterRankingAchievementState>();
			db.CreateTable<FollowFlowState>();
			db.CreateTable<VerifiedCharacterSession>();
		}

		private static string GetAvailableLoopbackUrl()
		{
			using var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			return $"http://127.0.0.1:{port}";
		}

		private readonly WebApplication m_app;
		private readonly SQLiteConnection m_db;
		private readonly string m_databasePath;
	}

	private sealed class FakeBlizzardProfileClient : IBlizzardProfileClient
	{
		public FakeBlizzardProfileClient(IReadOnlyList<VerifiedCharacter> characters)
		{
			m_characters = characters;
		}

		public Task<IReadOnlyList<VerifiedCharacter>> GetProfileCharactersAsync(string accessToken, string region, CancellationToken cancellationToken = default) =>
			Task.FromResult(m_characters);

		private readonly IReadOnlyList<VerifiedCharacter> m_characters;
	}

	private sealed class RecordingCharacterFollowAnnouncer : ICharacterFollowAnnouncer
	{
		public List<Announcement> Announcements { get; } = [];

		public Task AnnounceCharactersFollowedAsync(IReadOnlyList<VerifiedCharacter> characters, CancellationToken cancellationToken = default)
		{
			Announcements.Add(new Announcement(characters.ToList()));
			return Task.CompletedTask;
		}

		public sealed record Announcement(IReadOnlyList<VerifiedCharacter> Characters);
	}

	private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
	{
		public const string SchemeName = "E2E";

		public TestAuthenticationHandler(
			IOptionsMonitor<AuthenticationSchemeOptions> options,
			ILoggerFactory logger,
			UrlEncoder encoder)
			: base(options, logger, encoder)
		{
		}

		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			var identity = new ClaimsIdentity(
			[
				new Claim(ClaimTypes.NameIdentifier, "discord-e2e"),
				new Claim(ClaimTypes.Name, "E2E User"),
			], SchemeName);
			var properties = new AuthenticationProperties();
			if (Request.Headers["x-no-management-session"] != "1")
			{
				properties.Items["DiscordUserId"] = "discord-e2e";
				properties.Items["ManagementSessionId"] = "session-e2e";
				properties.Items["ManagementSessionExpiresAt"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
			}
			properties.StoreTokens(
			[
				new AuthenticationToken { Name = "access_token", Value = "test-access-token" },
			]);

			var principal = new ClaimsPrincipal(identity);
			var ticket = new AuthenticationTicket(principal, properties, SchemeName);
			return Task.FromResult(AuthenticateResult.Success(ticket));
		}
	}
}
