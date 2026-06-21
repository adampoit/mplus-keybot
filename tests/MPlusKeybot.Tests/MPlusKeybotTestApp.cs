using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MPlusKeybot.Tests;

// Composes the real production AppHost with test doubles injected at the
// Aspire composition layer (not in the app). Adds the TestServices resource
// (stub OpenIddict IdP + stub Blizzard API + announcement collector) and
// rewires the api's environment to point at it, fixes the web port so the
// OIDC redirect_uri is known, and isolates the SQLite database to a temp file.
public sealed class MPlusKeybotTestApp
{
    public const string WebPort = "5180";

    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"mplus-keybot-e2e-{Guid.NewGuid():N}.db");

    public DistributedApplication Application { get; private set; } = null!;

    public async Task StartAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MPlusKeybot_AppHost>(
            [$"--Web:LocalPort={WebPort}", "--AppHost:ApiEnvironment=Development", "--AppHost:PathBase=/mplus-keybot"]).ConfigureAwait(false);

        // Add the TestServices resource (stub IdP + stub Blizzard + collector).
        var testServices = builder.AddProject<Projects.MPlusKeybot_TestServices>("test-services")
            .WithHttpEndpoint(name: "test-services-http", env: "PORT");

        var api = builder.Resources.First(r => r.Name == "api");
        var web = builder.Resources.First(r => r.Name == "web");

        var webPublicBaseUrl = $"https://localhost:{WebPort}/mplus-keybot";
        var testServicesUrl = testServices.GetEndpoint("test-services-http");

        api.Annotations.Add(new EnvironmentCallbackAnnotation(env =>
        {
            env["Blizzard:OAuthAuthority"] = testServicesUrl;
            env["Blizzard:OAuthMetadataAddress"] = ReferenceExpression.Create($"{testServicesUrl}/.well-known/openid-configuration");
            env["Blizzard:ClientId"] = "test";
            env["Blizzard:ClientSecret"] = "secret";
            env["Blizzard:ApiBaseUrl"] = testServicesUrl;
            env["Follow:Announcer"] = "Webhook";
            env["Follow:WebhookUrl"] = ReferenceExpression.Create($"{testServicesUrl}/announcements");
            env["Discord:Token"] = string.Empty;
            env["Database:Path"] = DatabasePath;
            env["Web:PublicBaseUrl"] = webPublicBaseUrl;
        }));

        // Fix the web HTTPS port so the OIDC redirect_uri is stable and matches
        // what OpenIddict will see (https://localhost:5180/mplus-keybot/...).
        if (web.TryGetLastAnnotation<EndpointAnnotation>(out var webEndpoint))
        {
            webEndpoint.AllocatedEndpoint = new AllocatedEndpoint(webEndpoint, "localhost", int.Parse(WebPort));
        }

        Application = await builder.BuildAsync().ConfigureAwait(false);
        await Application.StartAsync().ConfigureAwait(false);
    }
}
