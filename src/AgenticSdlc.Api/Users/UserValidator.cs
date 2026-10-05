using System.Net.Mail;

namespace AgenticSdlc.Api.Users;

/// <summary>Trimmed, validated user fields.</summary>
public sealed record UserFields(string Email, string FirstName, string LastName);

/// <summary>Hand-written validation (spec §2, §3): trims first, reports every failing field at once.</summary>
public static class UserValidator
{
    public const int MaxEmailLength = 254;
    public const int MaxNameLength = 100;
    public const int MaxFilterLength = 254;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static Dictionary<string, string[]> Validate(
        string? email, string? firstName, string? lastName, out UserFields fields)
    {
        var errors = new Dictionary<string, string[]>();
        var e = email?.Trim() ?? "";
        var f = firstName?.Trim() ?? "";
        var l = lastName?.Trim() ?? "";

        if (e.Length == 0)
            errors["email"] = ["Email is required."];
        else if (e.Length > MaxEmailLength)
            errors["email"] = [$"Email must be at most {MaxEmailLength} characters."];
        else if (!IsValidEmail(e))
            errors["email"] = ["Email is not a valid address."];

        ValidateName(errors, "firstName", "First name", f);
        ValidateName(errors, "lastName", "Last name", l);

        fields = new UserFields(e, f, l);
        return errors;
    }

    public static Dictionary<string, string[]> Validate(UserQuery query, out UserFilter filter, out int page, out int pageSize)
    {
        var errors = new Dictionary<string, string[]>();

        page = query.Page ?? 1;
        pageSize = query.PageSize ?? DefaultPageSize;
        if (page < 1)
            errors["page"] = ["Page must be at least 1."];
        if (pageSize is < 1 or > MaxPageSize)
            errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];

        filter = new UserFilter(
            NormalizeFilter(errors, "search", query.Search),
            NormalizeFilter(errors, "email", query.Email),
            NormalizeFilter(errors, "firstName", query.FirstName),
            NormalizeFilter(errors, "lastName", query.LastName));
        return errors;
    }

    private static void ValidateName(Dictionary<string, string[]> errors, string key, string label, string value)
    {
        if (value.Length == 0)
            errors[key] = [$"{label} is required."];
        else if (value.Length > MaxNameLength)
            errors[key] = [$"{label} must be at most {MaxNameLength} characters."];
        else if (value.Any(char.IsControl))
            errors[key] = [$"{label} must not contain control characters."];
    }

    // MailAddress also accepts display-name forms ("Name <x@y>"); require the parsed address
    // to be the whole input and forbid whitespace anywhere.
    private static bool IsValidEmail(string value) =>
        !value.Any(char.IsWhiteSpace)
        && MailAddress.TryCreate(value, out var address)
        && address.Address == value;

    // Blank filters are treated as absent (spec §3).
    private static string? NormalizeFilter(Dictionary<string, string[]> errors, string key, string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length > MaxFilterLength)
        {
            errors[key] = [$"Filter must be at most {MaxFilterLength} characters."];
            return null;
        }
        return trimmed;
    }
}
