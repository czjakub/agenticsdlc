using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgenticSdlc.Api.Tests;

/// <summary>AC7: no business endpoints and no catch-all route.</summary>
public sealed class RoutingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("Development", "/api/anything")]
    [InlineData("Development", "/api")]
    [InlineData("Development", "/")]
    [InlineData("Production", "/api/anything")]
    [InlineData("Production", "/")]
    public async Task Unknown_routes_return_404(string environment, string path)
    {
        using var client = ApiFactory.For(factory, environment).CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact] // Guards against the health probe being satisfied by a fallback that answers everything.
    public async Task Health_endpoint_is_exact_path()
    {
        using var client = ApiFactory.For(factory, "Production").CreateClient();

        using var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
