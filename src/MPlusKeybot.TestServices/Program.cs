using System.Security.Claims;
using Microsoft.AspNetCore;
using MPlusKeybot.TestServices;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

// A single self-hosted bundle of test doubles for the e2e harness:
//   - a minimal OpenIddict authorization server (code flow + id_token) so the
//     app's real OIDC middleware does the actual Battle.net-style dance,
//   - a stub Blizzard profile API returning a configurable character list,
//   - a collector for follow announcements posted by the webhook announcer.
// The app never branches on "test mode"; this resource just impersonates the
// external services the app talks to.

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT");
if (!int.TryParse(port, out var portNumber))
	portNumber = 0;

builder.WebHost.UseUrls($"http://127.0.0.1:{portNumber}");

// The issuer must equal the URL the app's OIDC middleware uses as its authority,
// so the discovery `issuer` and token `iss` claims match. Aspire launches us on
// a fixed allocated port and points the app at http://localhost:<port>.
var issuer = $"http://localhost:{portNumber}";

builder.Services.AddOpenIddict()
	.AddServer(options =>
	{
		options.SetIssuer(issuer)
			   .SetAuthorizationEndpointUris("/connect/authorize")
			   .SetTokenEndpointUris("/connect/token")
			   .SetUserInfoEndpointUris("/connect/userinfo");

		options.AllowAuthorizationCodeFlow();

		// Storeless: run without the EF Core application/token stores and skip
		// the per-client permission checks (no client store to validate them).
		options.EnableDegradedMode();
		options.AcceptAnonymousClients();
		options.IgnoreEndpointPermissions();
		options.IgnoreGrantTypePermissions();
		options.IgnoreResponseTypePermissions();
		options.IgnoreScopePermissions();
		options.RegisterScopes("openid", "wow.profile", "email", "profile");

		// Storeless: accept the test app's redirect_uri (built from the fixed
		// web port) since there's no client application store to validate it.
		options.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.ValidateAuthorizationRequestContext>(handler => handler
			.UseInlineHandler(context =>
			{
				if (!string.IsNullOrEmpty(context.RedirectUri))
					context.SetRedirectUri(context.RedirectUri);
				return default;
			})
			.SetType(OpenIddict.Server.OpenIddictServerHandlerType.Custom)
			.Build());

		// Degraded mode requires a custom token-request validator; accept the
		// test client unconditionally (the code was issued by us moments ago).
		options.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.ValidateTokenRequestContext>(handler => handler
			.UseInlineHandler(context => default)
			.SetType(OpenIddict.Server.OpenIddictServerHandlerType.Custom)
			.Build());

		// Battle.net's OIDC returns encrypted id_tokens, and the app's middleware
		// expects to decrypt them, so register encryption + signing credentials.
		options.AddDevelopmentEncryptionCertificate();
		options.AddDevelopmentSigningCertificate();

		options.UseAspNetCore()
			   .EnableAuthorizationEndpointPassthrough()
			   .EnableTokenEndpointPassthrough()
			   .EnableUserInfoEndpointPassthrough()
			   .DisableTransportSecurityRequirement();
	});

builder.Services.AddSingleton<StubBlizzardState>();
builder.Services.AddSingleton<AnnouncementCollector>();
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
	app.UseDeveloperExceptionPage();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "test-services" }));

// Auto-approve authorize: issue an authorization code for a fixed test subject,
// echoing the requested scopes so the app's GrantedScope check sees wow.profile.
app.MapGet("/connect/authorize", (HttpContext context) =>
{
	var request = context.GetOpenIddictServerRequest()!;
	var principal = CreateTestPrincipal();
	principal.SetScopes(request.GetScopes().Append("openid").Distinct());
	return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
});

// Redeem the code for tokens. The stub always grants openid + wow.profile
// (the scopes the app requests) so the token response echoes them back.
app.MapPost("/connect/token", () =>
{
	var principal = CreateTestPrincipal();
	principal.SetScopes("openid", "wow.profile");
	foreach (var claim in principal.Claims)
		claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken);
	return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
});

app.MapGet("/connect/userinfo", () => Results.Json(new Dictionary<string, object>
{
	[OpenIddictConstants.Claims.Subject] = "battlenet-test-user",
	[OpenIddictConstants.Claims.Name] = "E2E User",
}));

// Stub Blizzard profile API.
app.MapGet("/profile/user/wow", (StubBlizzardState state, HttpRequest request) =>
{
	var region = request.Query["namespace"].ToString().Split('-').LastOrDefault() ?? "us";
	return Results.Json(state.BuildProfile(region));
});

app.MapPost("/admin/blizzard", async (StubBlizzardState state, HttpRequest request) =>
{
	var dto = await request.ReadFromJsonAsync<BlizzardStateRequest>().ConfigureAwait(false);
	state.SetCharacters(dto?.Characters ?? []);
	return Results.Ok(new { status = "ok" });
});

// Announcement collector (receives the webhook announcer's posts).
app.MapPost("/announcements", async (AnnouncementCollector collector, HttpRequest request) =>
{
	var announcement = await request.ReadFromJsonAsync<WebhookAnnouncement>().ConfigureAwait(false);
	if (announcement is not null)
		collector.Add(announcement);
	return Results.Ok(new { status = "ok" });
});
app.MapGet("/announcements", (AnnouncementCollector collector) => Results.Json(collector.GetAll()));
app.MapDelete("/announcements", (AnnouncementCollector collector) => { collector.Clear(); return Results.Ok(new { status = "ok" }); });

app.Run();

static ClaimsPrincipal CreateTestPrincipal()
{
	var identity = new ClaimsIdentity(
		authenticationType: "Test",
		nameType: OpenIddictConstants.Claims.Name,
		roleType: OpenIddictConstants.Claims.Role);
	identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, "battlenet-test-user"));
	identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, "E2E User"));
	return new ClaimsPrincipal(identity);
}
