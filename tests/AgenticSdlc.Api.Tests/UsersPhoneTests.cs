using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AgenticSdlc.Api.Tests;

/// <summary>The optional user phone number on /api/users (spec 2026-10-05-user-phone-number PH1–PH11).</summary>
public sealed class UsersPhoneTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Normalized = "+48600123456";

    private HttpClient NewClient() => ApiFactory.For(factory, Environments.Development).CreateClient();

    private static async Task<JsonElement> CreateWithPhoneAsync(HttpClient client, string email, string? phoneNumber)
    {
        using var response = await UsersApi.PostAsync(client,
            new { email, firstName = "Jan", lastName = "Kowalski", phoneNumber });
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

    /// <summary>Asserts <c>phoneNumber</c> is present (never omitted) and has the expected value.</summary>
    private static void AssertPhone(string? expected, JsonElement user)
    {
        Assert.True(user.TryGetProperty("phoneNumber", out var phone), $"no 'phoneNumber' in {user.GetRawText()}");
        if (expected is null)
        {
            Assert.Equal(JsonValueKind.Null, phone.ValueKind);
        }
        else
        {
            Assert.Equal(JsonValueKind.String, phone.ValueKind);
            Assert.Equal(expected, phone.GetString());
        }
    }

    [Fact] // PH1
    public async Task Post_without_phone_returns_201_with_null_phone()
    {
        using var client = NewClient();

        var user = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");

        AssertPhone(null, user);
        AssertPhone(null, await GetUserAsync(client, UsersApi.Id(user)));
    }

    public static TheoryData<string> AcceptedFormats => new()
    {
        "+48600123456",
        "+48 600 123 456",
        "0048600123456",
        "+48-600-123-456",
        "+48 (600) 123.456",
        "  +48600123456  ",
        " 0048 (600) 123-456 ",
    };

    [Theory] // PH2
    [MemberData(nameof(AcceptedFormats))]
    public async Task Post_with_phone_stores_it_normalized_to_E164(string phoneNumber)
    {
        using var client = NewClient();

        var user = await CreateWithPhoneAsync(client, "jan@example.com", phoneNumber);

        AssertPhone(Normalized, user);
        AssertPhone(Normalized, await GetUserAsync(client, UsersApi.Id(user)));
    }

    public static TheoryData<string> BlankPhoneBodies => new()
    {
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","phoneNumber":""}""",
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","phoneNumber":"   "}""",
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","phoneNumber":null}""",
    };

    [Theory] // PH3
    [MemberData(nameof(BlankPhoneBodies))]
    public async Task Post_with_blank_or_null_phone_returns_201_with_null_phone(string body)
    {
        using var client = NewClient();

        using var response = await UsersApi.PostRawAsync(client, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await UsersApi.ReadJsonAsync(response);
        AssertPhone(null, user);
        AssertPhone(null, await GetUserAsync(client, UsersApi.Id(user)));
    }

    public static TheoryData<string> InvalidPhones => new()
    {
        "600123456",                                   // no country code
        "600 123 456",                                 // no country code, with separators
        "+48 600 abc",                                 // letters
        "+48 22 123 45 67 ext. 12",                    // extensions are out of scope
        "+48/600123456",                               // unsupported separator
        "++48600123456",                               // second plus
        "+48600+123456",                               // plus in the middle
        "+0123456789",                                 // country code starting with 0
        "000123456789",                                // "00" prefix then a leading 0
        "+1234567",                                    // 7 digits
        "+1234567890123456",                           // 16 digits
        "+",                                           // no digits at all
        "00",                                          // no digits at all
        "+48" + new string(' ', 21) + "600123456",     // 33 chars: otherwise valid, over the raw cap
        "+48\t600123456",                              // tab is not a separator
        "+48\u0000600123456",                          // control char
        "+٤٨٦٠٠١٢٣٤٥٦",                                // Arabic-Indic digits
        "+４８６００１２３４５６",                         // full-width digits
    };

    [Theory] // PH4
    [MemberData(nameof(InvalidPhones))]
    public async Task Post_with_invalid_phone_returns_400_on_phoneNumber(string phoneNumber)
    {
        using var client = NewClient();

        using var response = await UsersApi.PostAsync(client,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", phoneNumber });

        await UsersApi.AssertValidationErrorsAsync(response, "phoneNumber");
        var list = await UsersApi.ListAsync(client);
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
    }

    [Theory] // PH5
    [InlineData("+12345678", "+12345678")]                                         // 8 digits
    [InlineData("+123456789012345", "+123456789012345")]                           // 15 digits
    [InlineData("+48" + "                    " + "600123456", Normalized)]        // exactly 32 raw chars
    [InlineData("   +48600123456" + "                              ", Normalized)] // the cap applies after trimming
    public async Task Post_with_boundary_phone_returns_201(string phoneNumber, string expected)
    {
        using var client = NewClient();

        var user = await CreateWithPhoneAsync(client, "jan@example.com", phoneNumber);

        AssertPhone(expected, user);
    }

    [Fact] // PH6
    public async Task Post_reports_phone_error_together_with_other_field_errors()
    {
        using var client = NewClient();

        using var response = await UsersApi.PostAsync(client,
            new { email = "not-an-email", firstName = "", lastName = "Kowalski", phoneNumber = "600123456" });

        await UsersApi.AssertValidationErrorsAsync(response, "email", "firstName", "phoneNumber");
    }

    [Fact] // PH7: PUT sets, changes and normalizes the phone
    public async Task Put_sets_and_changes_phone_normalized()
    {
        using var client = NewClient();
        var created = await UsersApi.CreateAsync(client, "jan@example.com", "Jan", "Kowalski");
        var id = UsersApi.Id(created);

        using (var set = await UsersApi.PutAsync(client, id,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", phoneNumber = "0048 600 123 456" }))
        {
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
            AssertPhone(Normalized, await UsersApi.ReadJsonAsync(set));
        }
        AssertPhone(Normalized, await GetUserAsync(client, id));

        using (var changed = await UsersApi.PutAsync(client, id,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", phoneNumber = "+1 (202) 555-0123" }))
        {
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            AssertPhone("+12025550123", await UsersApi.ReadJsonAsync(changed));
        }
        AssertPhone("+12025550123", await GetUserAsync(client, id));
    }

    public static TheoryData<string> PutBodiesWithoutPhone => new()
    {
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski"}""",
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","phoneNumber":null}""",
        """{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","phoneNumber":"  "}""",
    };

    [Theory] // PH7: PUT is a full replace — omitting, nulling or blanking the phone clears it
    [MemberData(nameof(PutBodiesWithoutPhone))]
    public async Task Put_without_phone_clears_the_stored_phone(string body)
    {
        using var client = NewClient();
        var created = await CreateWithPhoneAsync(client, "jan@example.com", "+48 600 123 456");
        var id = UsersApi.Id(created);

        using var response = await client.PutAsync($"{UsersApi.BasePath}/{id}",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPhone(null, await UsersApi.ReadJsonAsync(response));
        AssertPhone(null, await GetUserAsync(client, id));
    }

    [Fact] // PH7: an invalid phone on PUT leaves the stored user unchanged
    public async Task Put_with_invalid_phone_returns_400_and_keeps_the_stored_phone()
    {
        using var client = NewClient();
        var created = await CreateWithPhoneAsync(client, "jan@example.com", "+48 600 123 456");
        var id = UsersApi.Id(created);

        using var response = await UsersApi.PutAsync(client, id,
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", phoneNumber = "600123456" });

        await UsersApi.AssertValidationErrorsAsync(response, "phoneNumber");
        AssertPhone(Normalized, await GetUserAsync(client, id));
    }

    [Fact] // PH8: validation (phone included) runs before the lookup
    public async Task Put_invalid_phone_on_unknown_id_returns_400_not_404()
    {
        using var client = NewClient();

        using var response = await UsersApi.PutAsync(client, Guid.NewGuid().ToString(),
            new { email = "jan@example.com", firstName = "Jan", lastName = "Kowalski", phoneNumber = "+48 600 abc" });

        await UsersApi.AssertValidationErrorsAsync(response, "phoneNumber");
    }

    [Fact] // PH9: the phone is not unique
    public async Task Two_users_can_share_a_phone_number()
    {
        using var client = NewClient();

        var a = await CreateWithPhoneAsync(client, "a@example.com", "+48 600 123 456");
        var b = await CreateWithPhoneAsync(client, "b@example.com", "0048600123456");

        AssertPhone(Normalized, a);
        AssertPhone(Normalized, b);
        var list = await UsersApi.ListAsync(client);
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());
    }

    [Fact] // PH9: setting a phone already used by another user via PUT is not a conflict
    public async Task Put_with_a_phone_used_by_another_user_returns_200()
    {
        using var client = NewClient();
        await CreateWithPhoneAsync(client, "a@example.com", Normalized);
        var b = await UsersApi.CreateAsync(client, "b@example.com", "B", "B");

        using var response = await UsersApi.PutAsync(client, UsersApi.Id(b),
            new { email = "b@example.com", firstName = "B", lastName = "B", phoneNumber = Normalized });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPhone(Normalized, await UsersApi.ReadJsonAsync(response));
    }

    [Fact] // PH10: list items carry phoneNumber, a string or null
    public async Task List_items_contain_phoneNumber()
    {
        using var client = NewClient();
        await CreateWithPhoneAsync(client, "a@example.com", "+48 600 123 456");
        await UsersApi.CreateAsync(client, "b@example.com", "Jan", "Nowak");

        var page = await UsersApi.ListAsync(client);

        var items = page.GetProperty("items").EnumerateArray()
            .ToDictionary(u => u.GetProperty("email").GetString()!);
        Assert.Equal(2, items.Count);
        AssertPhone(Normalized, items["a@example.com"]);
        AssertPhone(null, items["b@example.com"]);
    }

    [Fact] // PH10: search does not match the phone number
    public async Task Search_does_not_match_by_phone()
    {
        using var client = NewClient();
        await CreateWithPhoneAsync(client, "a@example.com", "+48 600 123 456");
        await UsersApi.CreateAsync(client, "u600123@example.com", "Piotr", "Nowak");

        var page = await UsersApi.ListAsync(client, "?search=600123");

        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(["u600123@example.com"], UsersApi.Emails(page));
    }

    [Theory] // PH11: a non-string phoneNumber fails in the binder
    [InlineData("""{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","phoneNumber":48600123456}""")]
    [InlineData("""{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","phoneNumber":true}""")]
    [InlineData("""{"email":"jan@example.com","firstName":"Jan","lastName":"Kowalski","phoneNumber":["+48600123456"]}""")]
    public async Task Post_with_non_string_phone_returns_400(string body)
    {
        using var client = NewClient();

        using var response = await UsersApi.PostRawAsync(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var list = await UsersApi.ListAsync(client);
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
    }
}
