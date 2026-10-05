using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AgenticSdlc.Api.Tests;

/// <summary>PUT and DELETE /api/users/{id} (spec 2026-10-05-user-management-api AC10–AC13).</summary>
public sealed class UsersUpdateDeleteTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient NewClient() => ApiFactory.For(factory, Environments.Development).CreateClient();

    [Fact] // AC10
    public async Task Put_valid_returns_200_with_changes_and_keeps_createdAt()
    {
        using var client = NewClient();
        var created = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");
        var id = UsersApi.Id(created);

        using var response = await UsersApi.PutAsync(client, id,
            new { email = " jan.nowy@example.com ", firstName = " Janusz ", lastName = " Kowalski-Nowak " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await UsersApi.ReadJsonAsync(response);
        Assert.Equal(id, UsersApi.Id(updated));
        Assert.Equal("jan.nowy@example.com", updated.GetProperty("email").GetString());
        Assert.Equal("Janusz", updated.GetProperty("firstName").GetString());
        Assert.Equal("Kowalski-Nowak", updated.GetProperty("lastName").GetString());
        Assert.Equal(UsersApi.Time(created, "createdAt"), UsersApi.Time(updated, "createdAt"));
        Assert.True(UsersApi.Time(updated, "updatedAt") >= UsersApi.Time(updated, "createdAt"),
            "updatedAt is earlier than createdAt");
        Assert.True(UsersApi.Time(updated, "updatedAt") >= UsersApi.Time(created, "updatedAt"),
            "updatedAt went backwards");

        using var fetched = await UsersApi.GetAsync(client, $"{UsersApi.BasePath}/{id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var reread = await UsersApi.ReadJsonAsync(fetched);
        Assert.Equal("jan.nowy@example.com", reread.GetProperty("email").GetString());
        Assert.Equal("Janusz", reread.GetProperty("firstName").GetString());
        Assert.Equal("Kowalski-Nowak", reread.GetProperty("lastName").GetString());
        Assert.Equal(UsersApi.Time(updated, "updatedAt"), UsersApi.Time(reread, "updatedAt"));
    }

    [Fact] // AC10: the old email is released and the new one is taken
    public async Task Put_changing_email_frees_the_old_one()
    {
        using var client = NewClient();
        var created = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        using var put = await UsersApi.PutAsync(client, UsersApi.Id(created),
            new { email = "jan2@example.com", firstName = "Jan", lastName = "Kowalski" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        await UsersApi.CreateAsync(client, "jan@example.com", "Other", "Person");
        using var duplicate = await UsersApi.PostAsync(client,
            new { email = "JAN2@example.com", firstName = "Third", lastName = "Person" });
        await UsersApi.AssertProblemAsync(duplicate, HttpStatusCode.Conflict);
    }

    [Fact] // AC11
    public async Task Put_unknown_id_returns_404_problem()
    {
        using var client = NewClient();

        using var response = await UsersApi.PutAsync(client, Guid.NewGuid().ToString(),
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski" });

        await UsersApi.AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact] // AC11
    public async Task Put_non_guid_id_returns_404()
    {
        using var client = NewClient();

        using var response = await UsersApi.PutAsync(client, "not-a-guid",
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact] // AC11
    public async Task Put_invalid_body_returns_400_listing_each_field()
    {
        using var client = NewClient();
        var created = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        using var response = await UsersApi.PutAsync(client, UsersApi.Id(created),
            new { email = "not-an-email", firstName = " ", lastName = new string('x', 101) });

        await UsersApi.AssertValidationErrorsAsync(response, "email", "firstName", "lastName");

        using var fetched = await UsersApi.GetAsync(client, $"{UsersApi.BasePath}/{UsersApi.Id(created)}");
        var unchanged = await UsersApi.ReadJsonAsync(fetched);
        Assert.Equal("jan@example.com", unchanged.GetProperty("email").GetString());
        Assert.Equal("Jan", unchanged.GetProperty("firstName").GetString());
        Assert.Equal("Kowalski", unchanged.GetProperty("lastName").GetString());
    }

    [Fact] // AC11: validation runs before the lookup
    public async Task Put_invalid_body_on_unknown_id_returns_400_not_404()
    {
        using var client = NewClient();

        using var response = await UsersApi.PutAsync(client, Guid.NewGuid().ToString(),
            new { email = "", firstName = "Jan", lastName = "Kowalski" });

        await UsersApi.AssertValidationErrorsAsync(response, "email");
    }

    [Fact] // AC11 / AC7: malformed JSON on PUT
    public async Task Put_malformed_json_returns_400()
    {
        using var client = NewClient();
        var created = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        using var response = await client.PutAsync($"{UsersApi.BasePath}/{UsersApi.Id(created)}",
            new StringContent("{", System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory] // AC12
    [InlineData("anna@nowak.pl")]
    [InlineData("ANNA@Nowak.PL")]
    [InlineData(" anna@nowak.pl ")]
    public async Task Put_with_another_users_email_returns_409(string email)
    {
        using var client = NewClient();
        var jan = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");
        await UsersApi.CreateAsync(client, "anna@nowak.pl", "Anna", "Nowak");

        using var response = await UsersApi.PutAsync(client, UsersApi.Id(jan),
            new { email, firstName = "Jan", lastName = "Kowalski" });

        await UsersApi.AssertProblemAsync(response, HttpStatusCode.Conflict);
        using var fetched = await UsersApi.GetAsync(client, $"{UsersApi.BasePath}/{UsersApi.Id(jan)}");
        var unchanged = await UsersApi.ReadJsonAsync(fetched);
        Assert.Equal("jan@example.com", unchanged.GetProperty("email").GetString());
    }

    [Theory] // AC12: re-saving one's own email, in any case, is not a conflict
    [InlineData("jan@example.com")]
    [InlineData("JAN@EXAMPLE.COM")]
    public async Task Put_keeping_own_email_returns_200(string email)
    {
        using var client = NewClient();
        var jan = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        using var response = await UsersApi.PutAsync(client, UsersApi.Id(jan),
            new { email, firstName = "Janek", lastName = "Kowalski" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await UsersApi.ReadJsonAsync(response);
        Assert.Equal(email, updated.GetProperty("email").GetString());
        Assert.Equal("Janek", updated.GetProperty("firstName").GetString());
    }

    [Fact] // §4: for PUT the 404 check precedes the uniqueness check
    public async Task Put_unknown_id_with_taken_email_returns_404()
    {
        using var client = NewClient();
        await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        using var response = await UsersApi.PutAsync(client, Guid.NewGuid().ToString(),
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski" });

        await UsersApi.AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact] // AC13
    public async Task Delete_existing_returns_204_then_user_is_gone()
    {
        using var client = NewClient();
        var jan = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");
        await UsersApi.CreateAsync(client, "anna@nowak.pl", "Anna", "Nowak");
        var id = UsersApi.Id(jan);

        using var deleted = await UsersApi.DeleteAsync(client, id);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var fetched = await UsersApi.GetAsync(client, $"{UsersApi.BasePath}/{id}");
        await UsersApi.AssertProblemAsync(fetched, HttpStatusCode.NotFound);

        using var again = await UsersApi.DeleteAsync(client, id);
        await UsersApi.AssertProblemAsync(again, HttpStatusCode.NotFound);

        var list = await UsersApi.ListAsync(client);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(["anna@nowak.pl"], UsersApi.Emails(list));
    }

    [Fact] // AC13: deleting frees the email for reuse
    public async Task Delete_frees_the_email()
    {
        using var client = NewClient();
        var jan = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        using var deleted = await UsersApi.DeleteAsync(client, UsersApi.Id(jan));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var again = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");
        Assert.NotEqual(UsersApi.Id(jan), UsersApi.Id(again));
    }

    [Fact] // AC13
    public async Task Delete_unknown_id_returns_404_problem()
    {
        using var client = NewClient();

        using var response = await UsersApi.DeleteAsync(client, Guid.NewGuid().ToString());

        await UsersApi.AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact] // AC13: {id:guid} route constraint
    public async Task Delete_non_guid_id_returns_404()
    {
        using var client = NewClient();

        using var response = await UsersApi.DeleteAsync(client, "not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
