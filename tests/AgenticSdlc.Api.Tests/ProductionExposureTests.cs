using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AgenticSdlc.Api.Tests;

/// <summary>AC6: the API surface is not advertised outside Development.</summary>
public sealed class ProductionExposureTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("Production", "/openapi/v1.json")]
    [InlineData("Production", "/scalar")]
    [InlineData("Production", "/scalar/")]
    [InlineData("Staging", "/openapi/v1.json")]
    [InlineData("Staging", "/scalar")]
    public async Task Docs_are_not_found_outside_development(string environment, string path)
    {
        using var client = ApiFactory.For(factory, environment).CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
