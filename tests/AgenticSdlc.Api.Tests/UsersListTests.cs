using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AgenticSdlc.Api.Tests;

/// <summary>GET /api/users — list, search, filters, paging (spec 2026-10-05-user-management-api AC14–AC20).</summary>
public sealed class UsersListTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Jan = "jan.kowalski@example.com";
    private const string Anna = "anna@nowak.pl";
    private const string Piotr = "piotr@example.com";

    /// <summary>A fresh host seeded with the spec's three users, inserted out of display order.</summary>
    private async Task<HttpClient> SeededClientAsync()
    {
        var client = ApiFactory.For(factory, Environments.Development).CreateClient();
        await UsersApi.CreateAsync(client, Anna, "Anna", "Nowak");
        await UsersApi.CreateAsync(client, Jan, "Jan", "Kowalski");
        await UsersApi.CreateAsync(client, Piotr, "Piotr", "Kowalczyk");
        return client;
    }

    [Fact] // AC14
    public async Task List_without_params_returns_all_users_ordered_with_defaults()
    {
        using var client = await SeededClientAsync();

        var page = await UsersApi.ListAsync(client);

        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        // lastName → firstName → email: Kowalczyk, Kowalski, Nowak
        Assert.Equal([Piotr, Jan, Anna], UsersApi.Emails(page));

        var first = page.GetProperty("items")[0];
        Assert.True(Guid.TryParse(UsersApi.Id(first), out _));
        Assert.Equal("Piotr", first.GetProperty("firstName").GetString());
        Assert.Equal("Kowalczyk", first.GetProperty("lastName").GetString());
        Assert.True(first.TryGetProperty("createdAt", out _));
        Assert.True(first.TryGetProperty("updatedAt", out _));
    }

    [Fact] // AC14: empty store
    public async Task List_on_empty_store_returns_empty_page()
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();

        var page = await UsersApi.ListAsync(client);

        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, page.GetProperty("items").GetArrayLength());
    }

    [Fact] // AC14: ties on lastName break on firstName, then email; ordering is case-insensitive
    public async Task List_orders_by_lastName_then_firstName_then_email()
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();
        await UsersApi.CreateAsync(client, "z@example.com", "Adam", "nowak");
        await UsersApi.CreateAsync(client, "b@example.com", "Ewa", "Nowak");
        await UsersApi.CreateAsync(client, "a@example.com", "Ewa", "NOWAK");
        await UsersApi.CreateAsync(client, "x@example.com", "Zofia", "Adamska");

        var page = await UsersApi.ListAsync(client);

        Assert.Equal(["x@example.com", "z@example.com", "a@example.com", "b@example.com"], UsersApi.Emails(page));
    }

    [Theory] // AC15: OR across email/firstName/lastName, case-insensitive substring
    [InlineData("KOWAL", new[] { Piotr, Jan })]          // lastName
    [InlineData("kowal", new[] { Piotr, Jan })]
    [InlineData("anna", new[] { Anna })]                  // firstName + email
    [InlineData("nowak.pl", new[] { Anna })]              // email only
    [InlineData("PIOTR", new[] { Piotr })]                // firstName + email
    [InlineData("example.com", new[] { Piotr, Jan })]     // email only
    [InlineData("a", new[] { Piotr, Jan, Anna })]
    [InlineData("  nowak  ", new[] { Anna })]             // surrounding whitespace ignored
    public async Task Search_matches_substring_in_any_field(string search, string[] expected)
    {
        using var client = await SeededClientAsync();

        var page = await UsersApi.ListAsync(client, $"?search={Uri.EscapeDataString(search)}");

        Assert.Equal(expected, UsersApi.Emails(page));
        Assert.Equal(expected.Length, page.GetProperty("totalCount").GetInt32());
    }

    [Theory] // AC16: per-field filters, ANDed together and with search
    [InlineData("?lastName=kowal", new[] { Piotr, Jan })]
    [InlineData("?lastName=KOWALSKI", new[] { Jan })]
    [InlineData("?firstName=jan", new[] { Jan })]
    [InlineData("?firstName=ANNA", new[] { Anna })]
    [InlineData("?email=example.com", new[] { Piotr, Jan })]
    [InlineData("?email=NOWAK", new[] { Anna })]
    [InlineData("?firstName=jan&lastName=nowak", new string[0])]
    [InlineData("?firstName=piotr&lastName=kowal", new[] { Piotr })]
    [InlineData("?email=example.com&lastName=kowalc", new[] { Piotr })]
    [InlineData("?search=kowal&firstName=jan", new[] { Jan })]
    [InlineData("?search=anna&lastName=kowal", new string[0])]
    public async Task Field_filters_match_only_their_field_and_combine_with_and(string query, string[] expected)
    {
        using var client = await SeededClientAsync();

        var page = await UsersApi.ListAsync(client, query);

        Assert.Equal(expected, UsersApi.Emails(page));
        Assert.Equal(expected.Length, page.GetProperty("totalCount").GetInt32());
    }

    [Theory] // AC16: a field filter does not look at the other fields
    [InlineData("?firstName=kowal")]   // only in lastName/email
    [InlineData("?lastName=anna")]     // only in firstName/email
    [InlineData("?email=Kowalczyk")]   // only in lastName
    public async Task Field_filter_ignores_other_fields(string query)
    {
        using var client = await SeededClientAsync();

        var page = await UsersApi.ListAsync(client, query);

        Assert.Empty(UsersApi.Emails(page));
    }

    [Theory] // §3: empty/whitespace filters are treated as absent
    [InlineData("?search=")]
    [InlineData("?search=%20%20")]
    [InlineData("?email=&firstName=&lastName=")]
    public async Task Blank_filters_are_ignored(string query)
    {
        using var client = await SeededClientAsync();

        var page = await UsersApi.ListAsync(client, query);

        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
    }

    [Fact] // AC17
    public async Task Search_without_match_returns_200_with_empty_items()
    {
        using var client = await SeededClientAsync();

        var page = await UsersApi.ListAsync(client, "?search=zzz-nobody");

        Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    [Fact] // AC18
    public async Task Paging_returns_the_requested_slice()
    {
        using var client = await SeededClientAsync();

        var first = await UsersApi.ListAsync(client, "?pageSize=2&page=1");
        var second = await UsersApi.ListAsync(client, "?pageSize=2&page=2");

        Assert.Equal([Piotr, Jan], UsersApi.Emails(first));
        Assert.Equal([Anna], UsersApi.Emails(second));
        Assert.Equal(3, second.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
        Assert.Equal(2, second.GetProperty("pageSize").GetInt32());
    }

    [Theory] // AC18
    [InlineData(3)]
    [InlineData(1000)]
    public async Task Page_beyond_end_returns_empty_items_and_full_total(int page)
    {
        using var client = await SeededClientAsync();

        var result = await UsersApi.ListAsync(client, $"?pageSize=2&page={page}");

        Assert.Equal(0, result.GetProperty("items").GetArrayLength());
        Assert.Equal(3, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(page, result.GetProperty("page").GetInt32());
    }

    [Fact] // AC18: totalCount counts matches, not the whole store
    public async Task Paging_applies_after_filtering()
    {
        using var client = await SeededClientAsync();

        var result = await UsersApi.ListAsync(client, "?search=kowal&pageSize=1&page=2");

        Assert.Equal([Jan], UsersApi.Emails(result));
        Assert.Equal(2, result.GetProperty("totalCount").GetInt32());
    }

    [Theory] // AC18 boundaries
    [InlineData(1)]
    [InlineData(100)]
    public async Task PageSize_bounds_are_accepted(int pageSize)
    {
        using var client = await SeededClientAsync();

        var result = await UsersApi.ListAsync(client, $"?pageSize={pageSize}");

        Assert.Equal(pageSize, result.GetProperty("pageSize").GetInt32());
        Assert.Equal(Math.Min(pageSize, 3), result.GetProperty("items").GetArrayLength());
    }

    [Theory] // AC19
    [InlineData("?page=0", "page")]
    [InlineData("?page=-1", "page")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=-5", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    public async Task Invalid_paging_returns_400_with_matching_key(string query, string key)
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();

        using var response = await UsersApi.GetAsync(client, UsersApi.BasePath + query);

        await UsersApi.AssertValidationErrorsAsync(response, key);
    }

    [Fact] // AC19: every failing query parameter reported at once
    public async Task Invalid_page_and_pageSize_are_both_reported()
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();

        using var response = await UsersApi.GetAsync(client, UsersApi.BasePath + "?page=0&pageSize=101");

        await UsersApi.AssertValidationErrorsAsync(response, "page", "pageSize");
    }

    [Theory] // §8: huge page numbers must not overflow into a 500
    [InlineData("?page=2147483647")]
    [InlineData("?page=2147483647&pageSize=100")]
    public async Task Huge_page_is_never_a_server_error(string query)
    {
        using var client = await SeededClientAsync();

        using var response = await UsersApi.GetAsync(client, UsersApi.BasePath + query);

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest,
            $"GET {query} returned {(int)response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var page = await UsersApi.ReadJsonAsync(response);
            Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        }
        else
        {
            await UsersApi.AssertValidationErrorsAsync(response, "page");
        }
    }

    [Theory] // §3: overlong filters rejected on their own key
    [InlineData("search")]
    [InlineData("email")]
    [InlineData("firstName")]
    [InlineData("lastName")]
    public async Task Filter_longer_than_254_chars_returns_400(string key)
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();

        using var response = await UsersApi.GetAsync(client, $"{UsersApi.BasePath}?{key}={new string('a', 255)}");

        await UsersApi.AssertValidationErrorsAsync(response, key);
    }

    [Theory] // §3 boundary
    [InlineData("search")]
    [InlineData("email")]
    public async Task Filter_of_254_chars_is_accepted(string key)
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();

        var page = await UsersApi.ListAsync(client, $"?{key}={new string('a', 254)}");

        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    [Theory] // AC19: non-numeric paging never reaches a handler as garbage
    [InlineData("?page=abc")]
    [InlineData("?pageSize=ten")]
    public async Task Non_numeric_paging_returns_400(string query)
    {
        using var client = ApiFactory.For(factory, Environments.Development).CreateClient();

        using var response = await UsersApi.GetAsync(client, UsersApi.BasePath + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory] // AC20: search is literal — no wildcard or regex semantics
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("*")]
    [InlineData(".*")]
    [InlineData("[a-z]")]
    [InlineData("(")]
    [InlineData("\\")]
    [InlineData("' OR 1=1 --")]
    public async Task Search_special_characters_match_literally(string search)
    {
        using var client = await SeededClientAsync();

        var page = await UsersApi.ListAsync(client, $"?search={Uri.EscapeDataString(search)}");

        Assert.Empty(UsersApi.Emails(page));
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    [Fact] // AC20: a literal special character does match where it really occurs
    public async Task Search_matches_literal_special_characters_present_in_data()
    {
        using var client = await SeededClientAsync();
        await UsersApi.CreateAsync(client, "o.brien+test@example.com", "Seán", "O'Brien");

        var plus = await UsersApi.ListAsync(client, $"?search={Uri.EscapeDataString("+test")}");
        var apostrophe = await UsersApi.ListAsync(client, $"?lastName={Uri.EscapeDataString("o'b")}");
        var dot = await UsersApi.ListAsync(client, $"?email={Uri.EscapeDataString("o.b")}");

        Assert.Equal(["o.brien+test@example.com"], UsersApi.Emails(plus));
        Assert.Equal(["o.brien+test@example.com"], UsersApi.Emails(apostrophe));
        Assert.Equal(["o.brien+test@example.com"], UsersApi.Emails(dot));
    }

    [Fact] // AC15/AC16: list reflects updates
    public async Task Search_sees_updated_fields()
    {
        using var client = await SeededClientAsync();
        var jan = (await UsersApi.ListAsync(client, "?firstName=jan")).GetProperty("items")[0];

        using var put = await UsersApi.PutAsync(client, UsersApi.Id(jan),
            new { email = Jan, firstName = "Jan", lastName = "Zielinski" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        Assert.Equal([Jan], UsersApi.Emails(await UsersApi.ListAsync(client, "?lastName=zielin")));
        Assert.Equal([Piotr], UsersApi.Emails(await UsersApi.ListAsync(client, "?lastName=kowal")));
    }
}
