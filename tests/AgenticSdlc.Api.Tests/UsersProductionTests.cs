using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AgenticSdlc.Api.Tests;

/// <summary>AC22: unlike the docs, the user endpoints are mapped in every environment.</summary>
public sealed class UsersProductionTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task User_endpoints_work_outside_development(string environment)
    {
        using var client = ApiFactory.For(factory, environment).CreateClient();

        var created = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");
        var page = await UsersApi.ListAsync(client, "?search=kowal");

        Assert.Equal(["jan@example.com"], UsersApi.Emails(page));
        using var fetched = await UsersApi.GetAsync(client, $"{UsersApi.BasePath}/{UsersApi.Id(created)}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact] // §2: problem details in Production must not leak exception details
    public async Task Validation_errors_are_problem_details_in_production()
    {
        using var client = ApiFactory.For(factory, Environments.Production).CreateClient();

        using var response = await UsersApi.PostRawAsync(client, """{"email":"x"}""");

        await UsersApi.AssertValidationErrorsAsync(response, "email", "firstName", "lastName");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", body);
    }

    [Theory] // §2: binder failures are problem details too, not an empty 400
    [InlineData("POST", "/api/users", "[]")]
    [InlineData("POST", "/api/users", "null")]
    [InlineData("GET", "/api/users?page=abc", null)]
    public async Task Binding_errors_are_problem_details_in_production(string method, string path, string? body)
    {
        using var client = ApiFactory.For(factory, Environments.Production).CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        await UsersApi.AssertProblemAsync(response, HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("exception", text, StringComparison.OrdinalIgnoreCase);
    }
}
