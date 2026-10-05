using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgenticSdlc.Api.Tests;

/// <summary>
/// Builds in-memory clients for the API in an explicit hosting environment,
/// so no test depends on WebApplicationFactory's implicit default.
/// </summary>
public static class ApiFactory
{
    public static WebApplicationFactory<Program> For(WebApplicationFactory<Program> factory, string environment) =>
        factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
}
