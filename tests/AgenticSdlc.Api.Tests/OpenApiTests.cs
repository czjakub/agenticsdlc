using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AgenticSdlc.Api.Tests;

public sealed class OpenApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DocumentUrl = "/openapi/v1.json";

    private async Task<JsonElement> GetDocumentAsync()
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();
        using var response = await client.GetAsync(DocumentUrl, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        return json.RootElement.Clone();
    }

    [Fact] // AC3
    public async Task Document_is_served_as_json_in_development()
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();

        using var response = await client.GetAsync(DocumentUrl, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact] // AC3
    public async Task Document_is_openapi_3_with_info()
    {
        var document = await GetDocumentAsync();

        Assert.True(document.TryGetProperty("openapi", out var version), "document has no 'openapi' field");
        Assert.StartsWith("3.", version.GetString());

        Assert.True(document.TryGetProperty("info", out var info), "document has no 'info' field");
        Assert.Equal(JsonValueKind.Object, info.ValueKind);
        Assert.True(info.TryGetProperty("title", out _), "info has no 'title'");
        Assert.True(info.TryGetProperty("version", out _), "info has no 'version'");
    }

    [Fact] // AC4: "no API endpoints yet"
    public async Task Document_has_no_paths()
    {
        var document = await GetDocumentAsync();

        // An absent 'paths' would also mean "no endpoints", but OpenAPI 3 requires it; accept either.
        if (document.TryGetProperty("paths", out var paths))
        {
            Assert.Equal(JsonValueKind.Object, paths.ValueKind);
            var names = paths.EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Empty(names);
        }
    }

    [Fact] // AC4: the health probe is infrastructure, not part of the API description
    public async Task Document_does_not_describe_health()
    {
        var document = await GetDocumentAsync();

        Assert.DoesNotContain("/health", document.GetRawText());
    }
}
