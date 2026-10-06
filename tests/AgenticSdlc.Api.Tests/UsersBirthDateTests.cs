using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AgenticSdlc.Api.Tests;

/// <summary>The optional user birth date on /api/users (spec 2026-10-06-user-birth-date BD1–BD13).</summary>
public sealed class UsersBirthDateTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The fixed "now" of spec §7: today is 2026-10-06 (UTC).</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private HttpClient NewClient() => NewClient(new FixedTimeProvider(Now));

    private HttpClient NewClient(TimeProvider clock) =>
        ApiFactory.For(factory, Environments.Development)
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(clock)))
            .CreateClient();

    private static async Task<JsonElement> CreateWithBirthDateAsync(
        HttpClient client, string email, string? birthDate, string? phoneNumber = null)
    {
        using var response = await UsersApi.PostAsync(client,
            new { email, firstName = "Jan", lastName = "Kowalski", phoneNumber, birthDate });
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"POST {UsersApi.BasePath} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        return await UsersApi.ReadJsonAsync(response);
    }

    private static async Task<JsonElement> GetUserAsync(HttpClient client, string id)
    {
        using var response = await UsersApi.GetAsync(client, $"{UsersApi.BasePath}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await UsersApi.ReadJsonAsync(response);
    }

    private static async Task<HttpResponseMessage> PostBirthDateAsync(HttpClient client, string birthDate) =>
        await UsersApi.PostAsync(client,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", birthDate });

    private static async Task AssertNothingStoredAsync(HttpClient client)
    {
        var list = await UsersApi.ListAsync(client);
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
    }

    /// <summary>Asserts <c>birthDate</c> is present (never omitted) and is exactly the date-only string or null.</summary>
    private static void AssertBirthDate(string? expected, JsonElement user)
    {
        Assert.True(user.TryGetProperty("birthDate", out var birthDate), $"no 'birthDate' in {user.GetRawText()}");
        if (expected is null)
        {
            Assert.Equal(JsonValueKind.Null, birthDate.ValueKind);
        }
        else
        {
            Assert.Equal(JsonValueKind.String, birthDate.ValueKind);
            Assert.Equal(expected, birthDate.GetString());
        }
    }

    private static void AssertPhone(string? expected, JsonElement user)
    {
        Assert.True(user.TryGetProperty("phoneNumber", out var phone), $"no 'phoneNumber' in {user.GetRawText()}");
        Assert.Equal(expected, phone.ValueKind == JsonValueKind.Null ? null : phone.GetString());
    }

    [Fact] // BD1
    public async Task Post_without_birthDate_returns_201_with_null_birthDate()
    {
        using var client = NewClient();

        var user = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        AssertBirthDate(null, user);
        AssertBirthDate(null, await GetUserAsync(client, UsersApi.Id(user)));
    }

    [Theory] // BD2
    [InlineData("1990-05-17")]
    [InlineData("  1990-05-17  ")]
    [InlineData("\t1990-05-17\n")]
    public async Task Post_with_birthDate_stores_it_as_a_date_only_string(string birthDate)
    {
        using var client = NewClient();

        var user = await CreateWithBirthDateAsync(client, "jan@example.com", birthDate);

        AssertBirthDate("1990-05-17", user);
        AssertBirthDate("1990-05-17", await GetUserAsync(client, UsersApi.Id(user)));
    }

    public static TheoryData<string> BlankBirthDateBodies => new()
    {
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","birthDate":""}""",
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","birthDate":"   "}""",
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","birthDate":null}""",
    };

    [Theory] // BD3
    [MemberData(nameof(BlankBirthDateBodies))]
    public async Task Post_with_blank_or_null_birthDate_returns_201_with_null_birthDate(string body)
    {
        using var client = NewClient();

        using var response = await UsersApi.PostRawAsync(client, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await UsersApi.ReadJsonAsync(response);
        AssertBirthDate(null, user);
        AssertBirthDate(null, await GetUserAsync(client, UsersApi.Id(user)));
    }

    public static TheoryData<string> InvalidBirthDates => new()
    {
        "17.05.1990",                  // European order
        "1990/05/17",                  // wrong separator
        "1990-5-17",                   // month not zero-padded
        "90-05-17",                    // two-digit year
        "1990-05-17T00:00:00Z",        // a datetime, not a date
        "1990-05-17x",                 // trailing garbage
        "1990 -05-17",                 // inner whitespace
        "1990-02-30",                  // impossible day
        "2023-02-29",                  // not a leap year
        "1990-13-01",                  // impossible month
        "1990-00-10",                  // month zero
        "abcd-ef-gh",                  // not digits
        "١٩٩٠-٠٥-١٧",                  // Arabic-Indic digits
        "１９９０-０５-１７",              // full-width digits
        "05/17/1990",                  // US order
        "+1990-05-17",                 // sign
        new string('1', 200),          // pasted garbage
    };

    [Theory] // BD4
    [MemberData(nameof(InvalidBirthDates))]
    public async Task Post_with_invalid_birthDate_returns_400_on_birthDate(string birthDate)
    {
        using var client = NewClient();

        using var response = await PostBirthDateAsync(client, birthDate);

        await UsersApi.AssertValidationErrorsAsync(response, "birthDate");
        await AssertNothingStoredAsync(client);
    }

    [Fact] // BD5
    public async Task Post_with_leap_day_birthDate_returns_201()
    {
        using var client = NewClient();

        var user = await CreateWithBirthDateAsync(client, "jan@example.com", "2024-02-29");

        AssertBirthDate("2024-02-29", user);
    }

    [Fact] // BD6: tomorrow is in the future
    public async Task Post_with_future_birthDate_returns_400()
    {
        using var client = NewClient();

        using var response = await PostBirthDateAsync(client, "2026-10-07");

        await UsersApi.AssertValidationErrorsAsync(response, "birthDate");
        await AssertNothingStoredAsync(client);
    }

    [Theory] // BD6: far future
    [InlineData("2027-01-01")]
    [InlineData("9999-12-31")]
    public async Task Post_with_far_future_birthDate_returns_400(string birthDate)
    {
        using var client = NewClient();

        using var response = await PostBirthDateAsync(client, birthDate);

        await UsersApi.AssertValidationErrorsAsync(response, "birthDate");
    }

    [Fact] // BD6: today itself is allowed (newborns)
    public async Task Post_with_today_as_birthDate_returns_201()
    {
        using var client = NewClient();

        var user = await CreateWithBirthDateAsync(client, "jan@example.com", "2026-10-06");

        AssertBirthDate("2026-10-06", user);
    }

    [Fact] // BD6: "today" comes from the injected TimeProvider, not the real clock
    public async Task Future_check_uses_the_injected_clock()
    {
        using (var past = NewClient(new FixedTimeProvider(new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero))))
        {
            using var response = await PostBirthDateAsync(past, "2000-01-02");
            await UsersApi.AssertValidationErrorsAsync(response, "birthDate");
        }

        using var future = NewClient(new FixedTimeProvider(new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.Zero)));
        var user = await CreateWithBirthDateAsync(future, "jan@example.com", "2050-06-15");
        AssertBirthDate("2050-06-15", user);
    }

    [Fact] // BD6 / spec R3: "today" is the UTC date, even late in the UTC day
    public async Task Today_is_the_utc_date_at_the_end_of_the_day()
    {
        using var client = NewClient(new FixedTimeProvider(new DateTimeOffset(2026, 10, 6, 23, 59, 59, TimeSpan.Zero)));

        using (var tomorrow = await PostBirthDateAsync(client, "2026-10-07"))
        {
            await UsersApi.AssertValidationErrorsAsync(tomorrow, "birthDate");
        }
        var user = await CreateWithBirthDateAsync(client, "jan@example.com", "2026-10-06");
        AssertBirthDate("2026-10-06", user);
    }

    [Fact] // BD6 / spec R3: a local time zone already on the next day does not move "today"
    public async Task Today_ignores_the_local_time_zone()
    {
        var plus14 = TimeZoneInfo.CreateCustomTimeZone("UTC+14", TimeSpan.FromHours(14), "UTC+14", "UTC+14");
        using var client = NewClient(new FixedTimeProvider(Now, plus14)); // local: 2026-10-07 02:00

        using var response = await PostBirthDateAsync(client, "2026-10-07");

        await UsersApi.AssertValidationErrorsAsync(response, "birthDate");
    }

    [Fact] // BD7: exactly 150 years ago is allowed
    public async Task Post_with_birthDate_exactly_150_years_ago_returns_201()
    {
        using var client = NewClient();

        var user = await CreateWithBirthDateAsync(client, "jan@example.com", "1876-10-06");

        AssertBirthDate("1876-10-06", user);
    }

    [Theory] // BD7: older than 150 years
    [InlineData("1876-10-05")]
    [InlineData("1066-10-14")]
    [InlineData("0990-05-17")]
    [InlineData("0001-01-01")]
    public async Task Post_with_birthDate_older_than_150_years_returns_400(string birthDate)
    {
        using var client = NewClient();

        using var response = await PostBirthDateAsync(client, birthDate);

        await UsersApi.AssertValidationErrorsAsync(response, "birthDate");
        await AssertNothingStoredAsync(client);
    }

    [Fact] // BD8
    public async Task Post_reports_birthDate_error_together_with_other_field_errors()
    {
        using var client = NewClient();

        using var response = await UsersApi.PostAsync(client,
            new { email = "not-an-email", firstName = "Jan", lastName = "Kowalski", phoneNumber = "600123456", birthDate = "17.05.1990" });

        await UsersApi.AssertValidationErrorsAsync(response, "email", "phoneNumber", "birthDate");
    }

    [Fact] // BD8: a range error is reported together too
    public async Task Post_reports_future_birthDate_together_with_name_errors()
    {
        using var client = NewClient();

        using var response = await UsersApi.PostAsync(client,
            new { email = "jan@example.com", firstName = "", lastName = "", birthDate = "2030-01-01" });

        await UsersApi.AssertValidationErrorsAsync(response, "firstName", "lastName", "birthDate");
    }

    [Fact] // BD9: PUT sets and changes the birth date
    public async Task Put_sets_and_changes_birthDate()
    {
        using var client = NewClient();
        var created = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");
        var id = UsersApi.Id(created);

        using (var set = await UsersApi.PutAsync(client, id,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", birthDate = " 1990-05-17 " }))
        {
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
            AssertBirthDate("1990-05-17", await UsersApi.ReadJsonAsync(set));
        }
        AssertBirthDate("1990-05-17", await GetUserAsync(client, id));

        using (var changed = await UsersApi.PutAsync(client, id,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", birthDate = "1985-12-01" }))
        {
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            AssertBirthDate("1985-12-01", await UsersApi.ReadJsonAsync(changed));
        }
        AssertBirthDate("1985-12-01", await GetUserAsync(client, id));
    }

    public static TheoryData<string> PutBodiesWithoutBirthDate => new()
    {
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski"}""",
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","birthDate":null}""",
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","birthDate":""}""",
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","birthDate":"  "}""",
    };

    [Theory] // BD9: PUT is a full replace — omitting, nulling or blanking the birth date clears it
    [MemberData(nameof(PutBodiesWithoutBirthDate))]
    public async Task Put_without_birthDate_clears_the_stored_birthDate(string body)
    {
        using var client = NewClient();
        var created = await CreateWithBirthDateAsync(client, "jan@example.com", "1990-05-17");
        var id = UsersApi.Id(created);

        using var response = await client.PutAsync($"{UsersApi.BasePath}/{id}",
            new StringContent(body, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertBirthDate(null, await UsersApi.ReadJsonAsync(response));
        AssertBirthDate(null, await GetUserAsync(client, id));
    }

    [Theory] // BD9: an invalid birth date on PUT leaves the stored user unchanged
    [InlineData("17.05.1990")]
    [InlineData("2026-10-07")]
    [InlineData("1876-10-05")]
    public async Task Put_with_invalid_birthDate_returns_400_and_keeps_the_stored_birthDate(string birthDate)
    {
        using var client = NewClient();
        var created = await CreateWithBirthDateAsync(client, "jan@example.com", "1990-05-17");
        var id = UsersApi.Id(created);

        using var response = await UsersApi.PutAsync(client, id,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", birthDate });

        await UsersApi.AssertValidationErrorsAsync(response, "birthDate");
        AssertBirthDate("1990-05-17", await GetUserAsync(client, id));
    }

    [Fact] // BD10: validation (birth date included) runs before the lookup
    public async Task Put_invalid_birthDate_on_unknown_id_returns_400_not_404()
    {
        using var client = NewClient();

        using var response = await UsersApi.PutAsync(client, Guid.NewGuid().ToString(),
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", birthDate = "1990-02-30" });

        await UsersApi.AssertValidationErrorsAsync(response, "birthDate");
    }

    [Fact] // BD11: list items carry birthDate, a string or null
    public async Task List_items_contain_birthDate()
    {
        using var client = NewClient();
        await CreateWithBirthDateAsync(client, "a@example.com", "1990-05-17");
        await UsersApi.CreateAsync(client, "b@example.com", "Jan", "Nowak");

        var page = await UsersApi.ListAsync(client);

        var items = page.GetProperty("items").EnumerateArray()
            .ToDictionary(u => u.GetProperty("email").GetString()!);
        Assert.Equal(2, items.Count);
        AssertBirthDate("1990-05-17", items["a@example.com"]);
        AssertBirthDate(null, items["b@example.com"]);
    }

    [Theory] // BD11: search does not match the birth date
    [InlineData("1990")]
    [InlineData("1990-05-17")]
    public async Task Search_does_not_match_by_birthDate(string search)
    {
        using var client = NewClient();
        await CreateWithBirthDateAsync(client, "a@example.com", "1990-05-17");

        var page = await UsersApi.ListAsync(client, $"?search={Uri.EscapeDataString(search)}");

        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    [Theory] // BD12: a non-string birthDate fails in the binder
    [InlineData("""{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","birthDate":19900517}""")]
    [InlineData("""{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","birthDate":true}""")]
    [InlineData("""{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","birthDate":{}}""")]
    [InlineData("""{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","birthDate":["1990-05-17"]}""")]
    public async Task Post_with_non_string_birthDate_returns_400(string body)
    {
        using var client = NewClient();

        using var response = await UsersApi.PostRawAsync(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingStoredAsync(client);
    }

    [Fact] // BD13: birthDate and phoneNumber are independent
    public async Task BirthDate_and_phoneNumber_are_independent()
    {
        using var client = NewClient();

        var both = await CreateWithBirthDateAsync(client, "a@example.com", "1990-05-17", "+48 600 123 456");
        AssertBirthDate("1990-05-17", both);
        AssertPhone("+48600123456", both);

        var onlyBirthDate = await CreateWithBirthDateAsync(client, "b@example.com", "1990-05-17");
        AssertBirthDate("1990-05-17", onlyBirthDate);
        AssertPhone(null, onlyBirthDate);

        var onlyPhone = await CreateWithBirthDateAsync(client, "c@example.com", null, "+48 600 123 456");
        AssertBirthDate(null, onlyPhone);
        AssertPhone("+48600123456", onlyPhone);
    }

    [Fact] // BD13: changing one via PUT does not touch the other
    public async Task Put_changing_birthDate_keeps_the_phone_and_vice_versa()
    {
        using var client = NewClient();
        var created = await CreateWithBirthDateAsync(client, "jan@example.com", "1990-05-17", "+48 600 123 456");
        var id = UsersApi.Id(created);

        using (var changedDate = await UsersApi.PutAsync(client, id,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", phoneNumber = "+48600123456", birthDate = "1985-12-01" }))
        {
            Assert.Equal(HttpStatusCode.OK, changedDate.StatusCode);
            var user = await UsersApi.ReadJsonAsync(changedDate);
            AssertBirthDate("1985-12-01", user);
            AssertPhone("+48600123456", user);
        }

        using (var changedPhone = await UsersApi.PutAsync(client, id,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", phoneNumber = "+1 202 555 0123", birthDate = "1985-12-01" }))
        {
            Assert.Equal(HttpStatusCode.OK, changedPhone.StatusCode);
            var user = await UsersApi.ReadJsonAsync(changedPhone);
            AssertBirthDate("1985-12-01", user);
            AssertPhone("+12025550123", user);
        }
    }

    [Fact] // BD13: an invalid phone does not take a valid birth date down with a birthDate error
    public async Task Invalid_phone_with_valid_birthDate_reports_only_phoneNumber()
    {
        using var client = NewClient();

        using var response = await UsersApi.PostAsync(client,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", phoneNumber = "600123456", birthDate = "1990-05-17" });

        await UsersApi.AssertValidationErrorsAsync(response, "phoneNumber");
    }
}
