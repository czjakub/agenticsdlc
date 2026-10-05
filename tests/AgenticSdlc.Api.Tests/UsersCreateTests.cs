using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AgenticSdlc.Api.Tests;

/// <summary>POST /api/users and GET /api/users/{id} (spec 2026-10-05-user-management-api AC1–AC9).</summary>
public sealed class UsersCreateTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient NewClient() => ApiFactory.For(factory, Environments.Development).CreateClient();

    [Fact] // AC1
    public async Task Post_valid_user_returns_201_with_location_and_trimmed_body()
    {
        using var client = NewClient();

        using var response = await UsersApi.PostAsync(client,
            new { email = "  jan.kowalski@example.com ", firstName = " Jan ", lastName = "\tKowalski\n" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var user = await UsersApi.ReadJsonAsync(response);

        Assert.True(Guid.TryParse(UsersApi.Id(user), out var id), "id is not a GUID");
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("jan.kowalski@example.com", user.GetProperty("email").GetString());
        Assert.Equal("Jan", user.GetProperty("firstName").GetString());
        Assert.Equal("Kowalski", user.GetProperty("lastName").GetString());
        Assert.Equal(UsersApi.Time(user, "createdAt"), UsersApi.Time(user, "updatedAt"));

        Assert.NotNull(response.Headers.Location);
        var location = response.Headers.Location!.IsAbsoluteUri
            ? response.Headers.Location.AbsolutePath
            : response.Headers.Location.OriginalString;
        Assert.Equal($"/api/users/{id}", location, ignoreCase: true);
    }

    [Fact] // AC2
    public async Task Get_location_returns_the_created_user()
    {
        using var client = NewClient();

        using var created = await UsersApi.PostAsync(client,
            new { email = "anna@nowak.pl", firstName = "Anna", lastName = "Nowak" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdUser = await UsersApi.ReadJsonAsync(created);

        using var fetched = await client.GetAsync(created.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var user = await UsersApi.ReadJsonAsync(fetched);
        Assert.Equal(UsersApi.Id(createdUser), UsersApi.Id(user));
        Assert.Equal("anna@nowak.pl", user.GetProperty("email").GetString());
        Assert.Equal("Anna", user.GetProperty("firstName").GetString());
        Assert.Equal("Nowak", user.GetProperty("lastName").GetString());
        Assert.Equal(UsersApi.Time(createdUser, "createdAt"), UsersApi.Time(user, "createdAt"));
        Assert.Equal(UsersApi.Time(createdUser, "updatedAt"), UsersApi.Time(user, "updatedAt"));
    }

    public static TheoryData<string, string[]> MissingFieldBodies => new()
    {
        { """{}""", ["email", "firstName", "lastName"] },
        { """{"email":null,"firstName":null,"lastName":null}""", ["email", "firstName", "lastName"] },
        { """{"email":"","firstName":"","lastName":""}""", ["email", "firstName", "lastName"] },
        { """{"email":"   ","firstName":" \t ","lastName":"  "}""", ["email", "firstName", "lastName"] },
        { """{"firstName":"Jan","lastName":"Kowalski"}""", ["email"] },
        { """{"email":"jan@example.com","lastName":"Kowalski"}""", ["firstName"] },
        { """{"email":"jan@example.com","firstName":"Jan"}""", ["lastName"] },
        { """{"email":"jan@example.com","firstName":"  ","lastName":""}""", ["firstName", "lastName"] },
    };

    [Theory] // AC3: every failing field reported at once
    [MemberData(nameof(MissingFieldBodies))]
    public async Task Post_with_missing_or_blank_fields_returns_400_listing_each(string body, string[] expectedKeys)
    {
        using var client = NewClient();

        using var response = await UsersApi.PostRawAsync(client, body);

        await UsersApi.AssertValidationErrorsAsync(response, expectedKeys);
    }

    public static TheoryData<string> InvalidEmails => new()
    {
        "not-an-email",
        "a@",
        "@example.com",
        "John <j@x.io>",
        "a b@x.io",
        new string('a', 255 - "@x.io".Length) + "@x.io", // 255 chars
    };

    [Theory] // AC4
    [MemberData(nameof(InvalidEmails))]
    public async Task Post_with_invalid_email_returns_400_on_email(string email)
    {
        using var client = NewClient();

        using var response = await UsersApi.PostAsync(client, new { email, firstName = "Jan", lastName = "Kowalski" });

        await UsersApi.AssertValidationErrorsAsync(response, "email");
    }

    [Fact] // AC4 boundary: 254 chars is the maximum allowed length
    public async Task Post_with_254_char_email_is_accepted()
    {
        using var client = NewClient();
        var email = new string('a', 254 - "@x.io".Length) + "@x.io";

        var user = await UsersApi.CreateAsync(client, email, "Jan", "Kowalski");

        Assert.Equal(email, user.GetProperty("email").GetString());
    }

    [Theory] // AC5
    [InlineData("firstName")]
    [InlineData("lastName")]
    public async Task Post_with_101_char_name_returns_400(string field)
    {
        using var client = NewClient();
        var tooLong = new string('x', 101);
        object body = field == "firstName"
            ? new { email = "jan@example.com", firstName = tooLong, lastName = "Kowalski" }
            : new { email = "jan@example.com", firstName = "Jan", lastName = tooLong };

        using var response = await UsersApi.PostAsync(client, body);

        await UsersApi.AssertValidationErrorsAsync(response, field);
    }

    [Fact] // AC5 boundary
    public async Task Post_with_100_char_names_returns_201()
    {
        using var client = NewClient();
        var max = new string('x', 100);

        var user = await UsersApi.CreateAsync(client, "jan@example.com", max, max);

        Assert.Equal(max, user.GetProperty("firstName").GetString());
        Assert.Equal(max, user.GetProperty("lastName").GetString());
    }

    [Fact] // AC5: length is measured after trimming
    public async Task Post_with_100_char_name_padded_by_whitespace_returns_201()
    {
        using var client = NewClient();
        var max = new string('x', 100);

        var user = await UsersApi.CreateAsync(client, "jan@example.com", "  " + max + "  ", "Kowalski");

        Assert.Equal(max, user.GetProperty("firstName").GetString());
    }

    [Theory] // §3: control characters (NUL, newline, tab, DEL) are not valid in a name
    [InlineData("Ja\u0000n", "Kowalski", "firstName")]
    [InlineData("Jan", "Kowal\nski", "lastName")]
    [InlineData("J\tan", "Kowalski\u007f", "firstName", "lastName")]
    public async Task Post_with_control_characters_in_name_returns_400(string firstName, string lastName, params string[] keys)
    {
        using var client = NewClient();

        using var response = await UsersApi.PostAsync(client,
            new { email = "jan@example.com", firstName, lastName });

        await UsersApi.AssertValidationErrorsAsync(response, keys);
    }

    [Theory] // AC6
    [InlineData("jan@example.com")]
    [InlineData("JAN@EXAMPLE.COM")]
    [InlineData("  Jan@Example.com  ")]
    public async Task Post_with_duplicate_email_returns_409(string duplicate)
    {
        using var client = NewClient();
        await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        using var response = await UsersApi.PostAsync(client,
            new { email = duplicate, firstName = "Other", lastName = "Person" });

        await UsersApi.AssertProblemAsync(response, HttpStatusCode.Conflict);
        var list = await UsersApi.ListAsync(client);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
    }

    [Fact] // AC6 / §4: validation runs before the uniqueness check
    public async Task Post_duplicate_email_with_invalid_name_returns_400_not_409()
    {
        using var client = NewClient();
        await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        using var response = await UsersApi.PostAsync(client,
            new { email = "jan@example.com", firstName = "", lastName = "Kowalski" });

        await UsersApi.AssertValidationErrorsAsync(response, "firstName");
    }

    [Theory] // AC7
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("{\"email\":\"jan@example.com\",\"firstName\":\"Jan\",\"lastName\":\"Kowalski\"")] // unterminated object
    [InlineData("""{"email":123,"firstName":"Jan","lastName":"Kowalski"}""")]
    public async Task Post_with_malformed_or_empty_body_returns_400(string body)
    {
        using var client = NewClient();

        using var response = await UsersApi.PostRawAsync(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var list = await UsersApi.ListAsync(client);
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
    }

    [Fact] // AC7: no body at all
    public async Task Post_without_content_returns_400()
    {
        using var client = NewClient();

        using var response = await client.PostAsync(UsersApi.BasePath, content: null, TestContext.Current.CancellationToken);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType,
            $"POST without a body returned {(int)response.StatusCode}");
    }

    [Fact] // AC8
    public async Task Post_ignores_client_sent_id_and_timestamps()
    {
        using var client = NewClient();
        var clientId = Guid.NewGuid().ToString();
        var past = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using var response = await client.PostAsJsonAsync(UsersApi.BasePath,
            new { id = clientId, email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", createdAt = past, updatedAt = past },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await UsersApi.ReadJsonAsync(response);
        Assert.NotEqual(clientId, UsersApi.Id(user), StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(past, UsersApi.Time(user, "createdAt"));

        using var byClientId = await UsersApi.GetAsync(client, $"{UsersApi.BasePath}/{clientId}");
        Assert.Equal(HttpStatusCode.NotFound, byClientId.StatusCode);
    }

    [Fact] // AC1: two users get distinct ids
    public async Task Each_created_user_gets_a_distinct_id()
    {
        using var client = NewClient();

        var a = await UsersApi.CreateAsync(client, "a@example.com", "A", "A");
        var b = await UsersApi.CreateAsync(client, "b@example.com", "B", "B");

        Assert.NotEqual(UsersApi.Id(a), UsersApi.Id(b));
    }

    [Fact] // AC9
    public async Task Get_unknown_id_returns_404_problem()
    {
        using var client = NewClient();
        await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        using var response = await UsersApi.GetAsync(client, $"{UsersApi.BasePath}/{Guid.NewGuid()}");

        await UsersApi.AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Theory] // AC9: {id:guid} route constraint
    [InlineData("/api/users/not-a-guid")]
    [InlineData("/api/users/123")]
    public async Task Get_non_guid_id_returns_404(string path)
    {
        using var client = NewClient();

        using var response = await UsersApi.GetAsync(client, path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
