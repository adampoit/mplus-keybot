#pragma warning disable ASPIRECERTIFICATES001

using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

// The sub-path the app is served under. Defaults to `/` so the AppHost is
// path-agnostic; set AppHost:PathBase (e.g. via AppHost__PathBase env) to
// mirror a deployment that serves the app under a sub-path.
var builder = DistributedApplication.CreateBuilder(args);
var pathBase = NormalizePathBase(builder.Configuration["AppHost:PathBase"]);

var webDirectory = Path.GetFullPath("../MPlusKeybot.Web", builder.AppHostDirectory);
var webPort = builder.Configuration.GetValue<int?>("Web:LocalPort") ?? 5173;

var api = builder
    .AddProject<Projects.MPlusKeybot_Api>("api")
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Configuration["AppHost:ApiEnvironment"] ?? "Development");

var web = builder
    .AddViteApp("web", "../MPlusKeybot.Web", runScriptName: "dev")
    .WithReference(api)
    .WithEnvironment("BASE_PATH", pathBase)
    .WithEnvironment("REACT_ROUTER_ROOT", webDirectory)
    .WithEnvironment("NODE_ENV", "development")
    .WithEnvironment("API_BASE_URL", api.GetEndpoint("http"))
    .WithHttpsEndpoint(targetPort: webPort, env: "PORT", isProxied: false)
    .WithHttpsDeveloperCertificate()
    .WithExternalHttpEndpoints();

api.WithEnvironment("Web__PathBase", pathBase);
var publicBaseUrl = builder.Configuration["AppHost:PublicBaseUrl"];
if (string.IsNullOrWhiteSpace(publicBaseUrl))
    api.WithEnvironment("Web__PublicBaseUrl", ReferenceExpression.Create($"{web.GetEndpoint("https")}{pathBase}"));
else
    api.WithEnvironment("Web__PublicBaseUrl", publicBaseUrl.TrimEnd('/'));

builder.Build().Run();

static string NormalizePathBase(string? pathBase)
{
    if (string.IsNullOrWhiteSpace(pathBase) || pathBase == "/")
        return "/";

    return "/" + pathBase.Trim('/');
}
