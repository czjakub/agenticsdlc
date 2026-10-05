using Microsoft.AspNetCore.Mvc;

namespace AgenticSdlc.Api.Users;

// Fields are nullable so a missing field reaches UserValidator (400 with a field error)
// instead of failing in the binder without one (spec §3).
public sealed record CreateUserRequest(string? Email, string? FirstName, string? LastName, string? PhoneNumber);

public sealed record UpdateUserRequest(string? Email, string? FirstName, string? LastName, string? PhoneNumber);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static UserResponse From(User user) =>
        new(user.Id, user.Email, user.FirstName, user.LastName, user.PhoneNumber, user.CreatedAt, user.UpdatedAt);
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Query string of <c>GET /api/users</c>, bound with <c>[AsParameters]</c>.</summary>
public sealed class UserQuery
{
    /// <summary>Case-insensitive substring matched against email, firstName OR lastName.</summary>
    [FromQuery(Name = "search")] public string? Search { get; init; }

    [FromQuery(Name = "email")] public string? Email { get; init; }

    [FromQuery(Name = "firstName")] public string? FirstName { get; init; }

    [FromQuery(Name = "lastName")] public string? LastName { get; init; }

    [FromQuery(Name = "page")] public int? Page { get; init; }

    [FromQuery(Name = "pageSize")] public int? PageSize { get; init; }
}
