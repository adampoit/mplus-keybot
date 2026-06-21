using Discord.WebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Logging;
using SQLite;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

LogProvider.SetCurrentLogProvider(new ConsoleLogProvider());

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT");
if (int.TryParse(port, out var portNumber))
	builder.WebHost.UseUrls($"http://127.0.0.1:{portNumber}");
builder.Host.UseSystemd();
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", Microsoft.Extensions.Logging.LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Result.RedirectResult", Microsoft.Extensions.Logging.LogLevel.Warning);
var discordTokenConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Discord:Token"]);

builder.Services.AddHttpClient();
builder.Services.AddHttpClient<RaiderIOClient>();
builder.Services.AddHttpClient<IBlizzardProfileClient, BlizzardProfileClient>();
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton<RaiderIOClient>();
builder.Services.AddSingleton<WebUrlBuilder>();
builder.Services.AddSingleton<CharacterRepository>();
builder.Services.AddSingleton<CharacterManagementService>();
var announcerBackend = builder.Configuration["Follow:Announcer"] ?? "Discord";
if (string.Equals(announcerBackend, "Webhook", StringComparison.OrdinalIgnoreCase))
	builder.Services.AddSingleton<ICharacterFollowAnnouncer, WebhookCharacterFollowAnnouncer>();
else
	builder.Services.AddSingleton<ICharacterFollowAnnouncer, DiscordCharacterFollowAnnouncer>();
builder.Services.AddSingleton<FollowFlowStateService>();
if (discordTokenConfigured)
{
	builder.Services.AddSingleton<BotStatusRotator>();
	builder.Services.AddHostedService<DiscordBotHostedService>();
}
builder.Services.AddSingleton<SQLiteConnection>(services =>
{
	var configuration = services.GetRequiredService<IConfiguration>();
	var db = new SQLiteConnection(configuration["Database:Path"] ?? "mplus-data.db");

	db.CreateTable<Character>();
	db.CreateTable<CharacterAchievementState>();
	db.CreateTable<CharacterDungeonAchievementState>();
	db.CreateTable<CharacterRankingAchievementState>();
	db.CreateTable<DatabaseMigration>();
	db.CreateTable<FollowFlowState>();
	db.CreateTable<MythicPlusRun>();
	db.CreateTable<VerifiedCharacterSession>();

	return db;
});

builder.Services.AddAntiforgery(options =>
{
	var urls = new WebUrlBuilder(builder.Configuration);
	options.Cookie.Name = ".mplus-keybot.antiforgery";
	options.Cookie.HttpOnly = true;
	options.Cookie.SameSite = SameSiteMode.Lax;
	options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
	options.Cookie.Path = urls.CookiePath;
});

builder.Services
	.AddAuthentication(options =>
	{
		options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
		options.DefaultChallengeScheme = "Blizzard";
	})
	.AddCookie(options =>
	{
		var urls = new WebUrlBuilder(builder.Configuration);
		options.Cookie.Name = ".mplus-keybot.management";
		options.Cookie.HttpOnly = true;
		options.Cookie.SameSite = SameSiteMode.Lax;
		options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
		options.Cookie.Path = urls.CookiePath;
		options.ExpireTimeSpan = TimeSpan.FromHours(24);
		options.SlidingExpiration = false;
	})
	.AddOpenIdConnect("Blizzard", options =>
	{
		var urls = new WebUrlBuilder(builder.Configuration);
		var authority = builder.Configuration["Blizzard:OAuthAuthority"] ?? "https://oauth.battle.net";
		options.Authority = authority;
		options.MetadataAddress = builder.Configuration["Blizzard:OAuthMetadataAddress"] ?? $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
		options.ClientId = builder.Configuration["Blizzard:ClientId"] ?? string.Empty;
		options.ClientSecret = builder.Configuration["Blizzard:ClientSecret"] ?? string.Empty;
		options.ResponseType = OpenIdConnectResponseType.Code;
		options.CallbackPath = "/api/auth/blizzard/callback";
		options.CorrelationCookie.Path = urls.CookiePath;
		options.CorrelationCookie.SameSite = SameSiteMode.None;
		options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
		options.NonceCookie.Path = urls.CookiePath;
		options.NonceCookie.SameSite = SameSiteMode.None;
		options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
		// Tests point the authority at a stub HTTP issuer; allow that in Development.
		options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
		options.SaveTokens = true;
		options.GetClaimsFromUserInfoEndpoint = false;
		options.Scope.Clear();
		options.Scope.Add("openid");
		options.Scope.Add("wow.profile");
		options.Events = new OpenIdConnectEvents
		{
			OnRedirectToIdentityProvider = context =>
			{
				var urls = context.HttpContext.RequestServices.GetRequiredService<WebUrlBuilder>();
				context.ProtocolMessage.RedirectUri = urls.BuildPublicUrl("/api/auth/blizzard/callback");
				return Task.CompletedTask;
			},
			OnTokenResponseReceived = context =>
			{
				var grantedScope = context.TokenEndpointResponse.Scope;
				if (!string.IsNullOrWhiteSpace(grantedScope) && context.Properties is not null)
					context.Properties.Items["GrantedScope"] = grantedScope;

				return Task.CompletedTask;
			},
		};
	});
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

builder.Services.AddQuartz(q =>
{
	q.UseSimpleTypeLoader();
	q.UseInMemoryStore();
	q.UseDefaultThreadPool(tp =>
	{
		tp.MaxConcurrency = 10;
	});

	var checkRunsJobKey = new JobKey(CheckRunsJob.JobName);
	q.AddJob<CheckRunsJob>(checkRunsJobKey, job => job
		.WithDescription("Checks Raider.IO for recent mythic plus runs on followed characters."));
	q.AddTrigger(trigger => trigger
		.ForJob(checkRunsJobKey)
		.WithIdentity(CheckRunsJob.RecurringTriggerName)
		.WithSimpleSchedule(x => x
			.WithIntervalInMinutes(5)
			.RepeatForever()));
});
builder.Services.AddQuartzHostedService(opt =>
{
	opt.WaitForJobsToComplete = true;
});

var app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
	ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
});
app.UseExceptionHandler();
var webUrls = app.Services.GetRequiredService<WebUrlBuilder>();
var requestPathBase = !string.IsNullOrWhiteSpace(webUrls.PathBase)
	? webUrls.PathBase
	: webUrls.CookiePath == "/" ? string.Empty : webUrls.CookiePath;
if (!string.IsNullOrWhiteSpace(requestPathBase))
	app.UsePathBase(requestPathBase);
app.UseRouting();

if (app.Environment.IsDevelopment())
	app.UseWebSockets();

app.UseAuthentication();
app.UseAuthorization();
app.MapFollowWebRoutes();

var db = app.Services.GetRequiredService<SQLiteConnection>();
var raiderIOClient = app.Services.GetRequiredService<RaiderIOClient>();
await DatabaseMigrations.RunAsync(db, raiderIOClient).ConfigureAwait(false);

await app.RunAsync().ConfigureAwait(false);

sealed class ConsoleLogProvider : ILogProvider
{
	public Logger GetLogger(string name)
	{
		return (level, func, exception, parameters) =>
		{
			if (level >= Quartz.Logging.LogLevel.Info && func != null)
			{
				Console.WriteLine("[" + DateTime.Now.ToLongTimeString() + "] [" + level + "] " + func(), parameters);
			}
			return true;
		};
	}

	public IDisposable OpenNestedContext(string message)
	{
		throw new NotImplementedException();
	}

	public IDisposable OpenMappedContext(string key, object value, bool destructure = false)
	{
		throw new NotImplementedException();
	}
}
