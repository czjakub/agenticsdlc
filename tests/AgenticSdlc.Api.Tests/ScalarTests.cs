using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AgenticSdlc.Api.Tests;

public sealed class ScalarTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact] // AC5
    public async Task Scalar_ui_is_served_as_html_in_development()
    {
        // The factory's default client follows redirects, so /scalar → /scalar/ (or /scalar/v1) is fine.
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();

        using var response = await client.GetAsync("/scalar", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("scalar", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // AC5: the UI is wired to our OpenAPI document
    public async Task Scalar_ui_references_the_openapi_document()
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();

        using var response = await client.GetAsync("/scalar", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("openapi", html, StringComparison.OrdinalIgnoreCase);
    }
}
