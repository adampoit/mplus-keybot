using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;
using MPlusKeybot.Api;
using MPlusKeybot.Api.Database;
using MPlusKeybot.Api.Services;
using SQLite;

namespace MPlusKeybot.Tests;

[Trait("Category", "E2E")]
public sealed class CharacterManagementE2ETests(CharacterManagementE2ETests.AspireE2EFixture app) : IClassFixture<CharacterManagementE2ETests.AspireE2EFixture>
{
	[PlaywrightE2EFact]
	public async Task FrontendServesProxiedApiAndServerRenderedReact()
	{
		await m_app.SetStateAsync([], []);

		using var api = await m_app.Client.GetAsync("api/health").ConfigureAwait(false);
		api.EnsureSuccessStatusCode();
		Assert.Contains("\"service\":\"api\"", await api.Content.ReadAsStringAsync().ConfigureAwait(false));

		using var web = await m_app.Client.GetAsync(string.Empty).ConfigureAwait(false);
		web.EnsureSuccessStatusCode();
		var html = await web.Content.ReadAsStringAsync().ConfigureAwait(false);
		Assert.Contains("mplus-keybot", html);
		Assert.Contains("Track Mythic+ runs", html);
	}

	[PlaywrightE2EFact]
	public async Task CharacterPickerFiltersSortsAndSavesFollowSelection()
	{
		var characters = new[]
		{
			new VerifiedCharacterDto("us", "Hyjal", "Keela", 101, "Hyjal", 80),
			new VerifiedCharacterDto("us", "Area 52", "Newmage", 202, "Area 52", 70),
			new VerifiedCharacterDto("us", "Hyjal", "Bearcat", 303, "Hyjal", 60),
		};
		await m_app.SetStateAsync(characters, [new E2EFollowedCharacterDto(characters[0], 2468, 5)]);
		await using var browser = await LaunchChromiumAsync();
		await using var context = await browser.Browser.NewContextAsync(new()
		{
			BaseURL = m_app.BaseUrl,
			IgnoreHTTPSErrors = true,
		});
		var page = await context.NewPageAsync();

		await m_app.StartManagementSessionAsync(page);

		await page.GotoAsync("follow/characters");

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

		Assert.False((await m_app.GetCharacterAsync(characters[0])).IsFollowed);
		Assert.True((await m_app.GetCharacterAsync(characters[1])).IsFollowed);
		Assert.Null(await m_app.TryGetCharacterAsync(characters[2]));
		var announced = Assert.Single(await m_app.GetAnnouncementsAsync());
		Assert.Equal("discord-e2e", announced.DiscordUserId);
		var announcedCharacter = Assert.Single(announced.Characters);
		Assert.Equal("Newmage", announcedCharacter.Name);
	}

	[PlaywrightE2EFact]
	public async Task CharacterPickerCanContinueManagingAndSaveAgainAfterConfirmation()
	{
		var characters = new[]
		{
			new VerifiedCharacterDto("us", "Hyjal", "Keela", 101, "Hyjal", 80),
			new VerifiedCharacterDto("us", "Area 52", "Newmage", 202, "Area 52", 70),
		};
		await m_app.SetStateAsync(characters, []);
		await using var browser = await LaunchChromiumAsync();
		await using var context = await browser.Browser.NewContextAsync(new()
		{
			BaseURL = m_app.BaseUrl,
			IgnoreHTTPSErrors = true,
		});
		var page = await context.NewPageAsync();

		await m_app.StartManagementSessionAsync(page);
		await page.GotoAsync("follow/characters");
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
		Assert.True((await m_app.GetCharacterAsync(characters[0])).IsFollowed);
		Assert.True((await m_app.GetCharacterAsync(characters[1])).IsFollowed);
		Assert.Collection(
			await m_app.GetAnnouncementsAsync(),
			announcement => Assert.Equal("Keela", Assert.Single(announcement.Characters).Name),
			announcement => Assert.Equal("Newmage", Assert.Single(announcement.Characters).Name));
	}

	[PlaywrightE2EFact]
	public async Task HomePageShowsFollowedCharacterProgress()
	{
		var followed = new VerifiedCharacterDto("us", "Hyjal", "Keela", 101, "Hyjal", 80);
		var alt = new VerifiedCharacterDto("us", "Area 52", "Newmage", 202, "Area 52", 70);
		await m_app.SetStateAsync([followed, alt], [new E2EFollowedCharacterDto(followed, 2468, 5)]);
		await using var browser = await LaunchChromiumAsync();
		await using var context = await browser.Browser.NewContextAsync(new()
		{
			BaseURL = m_app.BaseUrl,
			IgnoreHTTPSErrors = true,
		});
		var page = await context.NewPageAsync();

		// The home page calls /api/home, which needs an authenticated Battle.net
		// session to fetch the verified character list from the stub Blizzard API.
		await m_app.StartManagementSessionAsync(page);
		await page.GotoAsync(string.Empty);

		await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Manage Characters" }).First).ToBeVisibleAsync();
		await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Character Progress" })).ToBeVisibleAsync();
		await Expect(page.Locator(".character-row", new() { HasTextString = "Keela" })).ToBeVisibleAsync();
		await Expect(page.Locator(".character-row", new() { HasTextString = "🏆 2468" })).ToBeVisibleAsync();
	}

	[PlaywrightE2EFact]
	public async Task CharacterManagementEntryExplainsDiscordFlowWithoutManagementSession()
	{
		await m_app.SetStateAsync([], []);
		await using var browser = await LaunchChromiumAsync();
		await using var context = await browser.Browser.NewContextAsync(new()
		{
			BaseURL = m_app.BaseUrl,
			IgnoreHTTPSErrors = true,
		});
		var page = await context.NewPageAsync();

		// No management session seeded -> the page explains the Discord flow.
		await page.GotoAsync("follow/characters");

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

	private readonly AspireE2EFixture m_app = app;

	private sealed class PlaywrightE2EFactAttribute : FactAttribute
	{
		public PlaywrightE2EFactAttribute()
		{
			if (!PlaywrightBrowserSession.IsChromiumInstalled() && !AspireE2EFixture.IsExternalMode)
				Skip = "Install Playwright Chromium to run browser e2e tests.";
			else if (!AspireE2EFixture.IsExternalMode && !AspireE2EFixture.HasReactRouterDependencies())
				Skip = "Run npm install in src/MPlusKeybot.Web to run browser e2e tests.";
		}
	}

	private sealed class PlaywrightBrowserSession(IBrowser browser, IPlaywright playwright) : IAsyncDisposable
	{
		public IBrowser Browser { get; } = browser;

		public static bool IsChromiumInstalled()
		{
			try
			{
				using var playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
				return File.Exists(playwright.Chromium.ExecutablePath);
			}
			catch (PlaywrightException)
			{
				return false;
			}
		}

		public async ValueTask DisposeAsync()
		{
			await Browser.DisposeAsync();
			m_playwright.Dispose();
		}

		private readonly IPlaywright m_playwright = playwright;
	}

	// Fixture supports Aspire's development topology locally and externally
	// started Nix packages in CI. Both modes use the same test doubles
	// and helpers for the real OIDC management-session flow.
#pragma warning disable CA1001 // xUnit owns async fixture disposal through IAsyncLifetime.
	public sealed class AspireE2EFixture : IAsyncLifetime
	{
		public string BaseUrl { get; private set; } = string.Empty;
		public HttpClient Client { get; private set; } = null!;

		private MPlusKeybotTestApp? m_app;
		private DistributedApplication? m_aspire;
		private HttpClient? m_testServicesClient;
		private string? m_databasePath;

		public static bool IsExternalMode =>
			string.Equals(Environment.GetEnvironmentVariable("MPLUS_KEYBOT_E2E_MODE"), "external", StringComparison.OrdinalIgnoreCase);

		public static bool HasReactRouterDependencies() =>
			File.Exists(Path.Combine(FindRepositoryRoot(), "src", "MPlusKeybot.Web", "node_modules", ".bin", "react-router"));

		private static string RequiredEnvironment(string name) =>
			Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
				? value
				: throw new InvalidOperationException($"{name} must be set for E2E tests.");

		private static string NormalizeBaseUrl(string value) => value.EndsWith('/') ? value : $"{value}/";

		public async Task InitializeAsync()
		{
			if (!PlaywrightBrowserSession.IsChromiumInstalled())
			{
				if (IsExternalMode)
					throw new InvalidOperationException("E2E tests require Playwright Chromium.");

				return;
			}

			if (IsExternalMode)
			{
				BaseUrl = NormalizeBaseUrl(RequiredEnvironment("MPLUS_KEYBOT_E2E_BASE_URL"));
				m_databasePath = RequiredEnvironment("MPLUS_KEYBOT_E2E_DATABASE_PATH");
				m_testServicesClient = new HttpClient
				{
					BaseAddress = new Uri(NormalizeBaseUrl(RequiredEnvironment("MPLUS_KEYBOT_E2E_TEST_SERVICES_URL"))),
				};
			}
			else
			{
				if (!HasReactRouterDependencies())
					return;

				m_app = new MPlusKeybotTestApp();
				await m_app.StartAsync().ConfigureAwait(false);
				m_aspire = m_app.Application;
				m_databasePath = m_app.DatabasePath;
				BaseUrl = $"https://localhost:{MPlusKeybotTestApp.WebPort}/mplus-keybot/";
				m_testServicesClient = m_aspire.CreateHttpClient("test-services", "test-services-http");
			}

			// The web endpoint is HTTPS with Aspire's self-signed dev cert or the
			// E2E proxy's self-signed certificate.
			Client = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
			{
				BaseAddress = new Uri(BaseUrl),
			};

			await WaitForReadyAsync().ConfigureAwait(false);
		}

		public async Task DisposeAsync()
		{
			m_testServicesClient?.Dispose();
			Client?.Dispose();
			if (m_aspire is not null)
				await m_aspire.StopAsync().ConfigureAwait(false);
			m_aspire?.Dispose();
			if (!IsExternalMode && m_databasePath is not null && File.Exists(m_databasePath))
				File.Delete(m_databasePath);
		}

		// Seeds the stub Blizzard API with the verified characters and writes
		// any already-followed characters straight to the isolated DB.
		public async Task SetStateAsync(IReadOnlyList<VerifiedCharacterDto> verifiedCharacters, IReadOnlyList<E2EFollowedCharacterDto> followedCharacters)
		{
			ArgumentNullException.ThrowIfNull(followedCharacters);
			await m_testServicesClient!.PostAsJsonAsync("admin/blizzard", new
			{
				Characters = verifiedCharacters.Select(c => new
				{
					c.Region,
					c.Realm,
					c.Name,
					c.BlizzardCharacterId,
					c.RealmDisplayName,
					c.Level,
					c.Class,
				}).ToList(),
			}).ConfigureAwait(false);

			using (var db = OpenDatabase())
			{
				db.DeleteAll<Character>();
				db.DeleteAll<CharacterAchievementState>();
				db.DeleteAll<CharacterDungeonAchievementState>();
				db.DeleteAll<CharacterRankingAchievementState>();
				db.DeleteAll<FollowFlowState>();
				db.DeleteAll<MythicPlusRun>();
				db.DeleteAll<VerifiedCharacterSession>();

				var repo = new CharacterRepository(db);
				foreach (var followed in followedCharacters)
				{
					var verified = new VerifiedCharacter(
						followed.Character.Region,
						followed.Character.Realm,
						followed.Character.Name,
						followed.Character.BlizzardCharacterId,
						followed.Character.RealmDisplayName,
						followed.Character.Level,
						followed.Character.Class);
					var character = repo.UpsertFollowedCharacter(verified, "discord-e2e", DateTime.UtcNow);
					if (followed.CurrentScore is { } currentScore)
						character.CurrentScore = currentScore;
					if (followed.LastCheckedMinutesAgo is { } minutesAgo)
						character.LastCheckedAt = DateTime.UtcNow.AddMinutes(-minutesAgo);
					db.Update(character);
				}
			}

			await m_testServicesClient!.DeleteAsync("announcements").ConfigureAwait(false);
		}

		// Drives the real management-session entrypoint: seeds a follow flow
		// state (as Discord's /follow would), then visits /api/follow/start,
		// which challenges the stub OIDC issuer, auto-approves, and lands the
		// management cookie — exactly the app flow minus Discord.
		public async Task StartManagementSessionAsync(IPage page)
		{
			ArgumentNullException.ThrowIfNull(page);
			string stateToken;
			using (var db = OpenDatabase())
			{
				var states = new FollowFlowStateService(db);
				var state = states.Create("discord-e2e", TimeSpan.FromMinutes(10));
				stateToken = state.State;
			}

			await page.GotoAsync($"api/follow/start?state={stateToken}").ConfigureAwait(false);
			// The stub IdP auto-approves and redirects back to the manage page.
			await page.WaitForURLAsync("**/follow/characters").ConfigureAwait(false);
		}

		public async Task<CharacterDto> GetCharacterAsync(VerifiedCharacterDto character)
		{
			ArgumentNullException.ThrowIfNull(character);

			return await TryGetCharacterAsync(character).ConfigureAwait(false) ?? throw new InvalidOperationException($"Character {character.Name} was not found.");
		}

		public Task<CharacterDto?> TryGetCharacterAsync(VerifiedCharacterDto character)
		{
			ArgumentNullException.ThrowIfNull(character);
			using var db = OpenDatabase();
			var repo = new CharacterRepository(db);
			var found = repo.GetCharacter(CharacterKey.From(character.Region, character.Realm, character.Name));
			return Task.FromResult(found is null ? null : new CharacterDto(found.IsFollowed));
		}

		public async Task<IReadOnlyList<AnnouncementDto>> GetAnnouncementsAsync()
		{
			var announcements = await m_testServicesClient!.GetFromJsonAsync<IReadOnlyList<AnnouncementDto>>("announcements", s_jsonOptions).ConfigureAwait(false);
			return announcements ?? [];
		}

		private SQLiteConnection OpenDatabase() => new(m_databasePath ?? throw new InvalidOperationException("The E2E database has not been configured."));

		private async Task WaitForReadyAsync()
		{
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
			while (!cts.IsCancellationRequested)
			{
				try
				{
					using var api = await Client.GetAsync("api/health", cts.Token).ConfigureAwait(false);
					using var web = await Client.GetAsync("health", cts.Token).ConfigureAwait(false);
					if (api.IsSuccessStatusCode && web.IsSuccessStatusCode)
						return;
				}
				catch (HttpRequestException) when (!cts.IsCancellationRequested)
				{
				}
				catch (TaskCanceledException) when (!cts.IsCancellationRequested)
				{
				}

				await Task.Delay(500, cts.Token).ConfigureAwait(false);
			}

			throw new TimeoutException($"Timed out waiting for the web app at {BaseUrl}.");
		}

		private static string FindRepositoryRoot()
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MPlusKeybot.slnx")))
				directory = directory.Parent;

			return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
		}

		private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);
	}
#pragma warning restore CA1001

	public sealed record VerifiedCharacterDto(string Region, string Realm, string Name, long? BlizzardCharacterId, string? RealmDisplayName, int? Level, string? Class = null);
	public sealed record E2EFollowedCharacterDto(VerifiedCharacterDto Character, int? CurrentScore, int? LastCheckedMinutesAgo);
	public sealed record CharacterDto(bool IsFollowed);
	public sealed record AnnouncementDto(string DiscordUserId, IReadOnlyList<VerifiedCharacterDto> Characters);
}
