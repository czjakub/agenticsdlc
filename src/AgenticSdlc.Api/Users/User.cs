namespace AgenticSdlc.Api.Users;

/// <summary>
/// A stored user. Immutable so the repository never hands out a reference
/// a caller could mutate into the store (spec 2026-10-05-user-management-api R5).
/// </summary>
public sealed record User(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
