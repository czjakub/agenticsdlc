using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgenticSdlc.Api.Tests;

/// <summary>
/// Builds in-memory clients for the API in an explicit hosting environment,
/// so no test depends on WebApplicationFactory's implicit default.
/// Config reload-on-change is off: every host otherwise opens inotify watchers on
/// appsettings*.json, and one host per test exhausts the default Linux limit (128).
/// </summary>
public static class ApiFactory
{
    public static WebApplicationFactory<Program> For(WebApplicationFactory<Program> factory, string environment) =>
        factory.WithWebHostBuilder(builder => builder
            .UseEnvironment(environment)
            .UseSetting("hostBuilder:reloadConfigOnChange", "false"));
}
