using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AgenticSdlc.Api.Tests;

/// <summary>
/// Thin helpers over <c>/api/users</c> (spec 2026-10-05-user-management-api).
/// Responses are read as <see cref="JsonElement"/> so the tests need no production DTO types.
/// Every client comes from a fresh host, i.e. a fresh empty in-memory store (spec §7 isolation note).
/// </summary>
internal static class UsersApi
{
    public const string BasePath = "/api/users";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync(BasePath, body, Ct);

    public static Task<HttpResponseMessage> PostRawAsync(HttpClient client, string body) =>
        client.PostAsync(BasePath, new StringContent(body, Encoding.UTF8, "application/json"), Ct);

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string id, object body) =>
        client.PutAsJsonAsync($"{BasePath}/{id}", body, Ct);

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string path) =>
        client.GetAsync(path, Ct);

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string id) =>
        client.DeleteAsync($"{BasePath}/{id}", Ct);

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(Ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: Ct);
        return json.RootElement.Clone();
    }

    /// <summary>Creates a user, asserts 201, returns the response body.</summary>
    public static async Task<JsonElement> CreateAsync(HttpClient client, string email, string firstName, string lastName)
    {
        using var response = await PostAsync(client, new { email, firstName, lastName });
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"POST {BasePath} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        return await ReadJsonAsync(response);
    }

    /// <summary>GETs a list URL, asserts 200, returns the paged body.</summary>
    public static async Task<JsonElement> ListAsync(HttpClient client, string query = "")
    {
        using var response = await GetAsync(client, BasePath + query);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"GET {BasePath}{query} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return await ReadJsonAsync(response);
    }

    public static string Id(JsonElement user) => user.GetProperty("id").GetString()!;

    public static string[] Emails(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("email").GetString()!).ToArray();

    public static DateTimeOffset Time(JsonElement user, string property) =>
        user.GetProperty(property).GetDateTimeOffset();

    /// <summary>Asserts an RFC 9457 problem response with the given status.</summary>
    public static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        return problem;
    }

    /// <summary>Asserts a 400 validation problem whose <c>errors</c> contain exactly the expected keys.</summary>
    public static async Task AssertValidationErrorsAsync(HttpResponseMessage response, params string[] expectedKeys)
    {
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.True(problem.TryGetProperty("errors", out var errors), $"no 'errors' in {problem.GetRawText()}");
        var keys = errors.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedKeys.Order(StringComparer.Ordinal).ToArray(), keys);
        foreach (var key in expectedKeys)
        {
            Assert.NotEqual(0, errors.GetProperty(key).GetArrayLength());
        }
    }
}
