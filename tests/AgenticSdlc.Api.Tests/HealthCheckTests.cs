using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AgenticSdlc.Api.Tests;

public sealed class HealthCheckTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("Development")] // AC1
    [InlineData("Production")]  // AC2: health is environment-independent
    [InlineData("Staging")]
    public async Task Get_health_returns_200_Healthy_as_plain_text(string environment)
    {
        using var client = ApiFactory.For(factory, environment).CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Head_health_succeeds()
    {
        using var client = ApiFactory.For(factory, Environments.Production).CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Head, "/health");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] // AC8
    public async Task Post_health_is_not_a_success()
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();

        using var response = await client.PostAsync("/health", content: null, TestContext.Current.CancellationToken);

        Assert.False(response.IsSuccessStatusCode, $"POST /health returned {(int)response.StatusCode}");
    }

    [Fact] // §4: health returns status only, no check details
    public async Task Health_response_does_not_leak_details()
    {
        using var client = ApiFactory.For(factory, Environments.Production).CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("{", body);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
    }
}
