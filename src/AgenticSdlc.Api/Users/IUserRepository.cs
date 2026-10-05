namespace AgenticSdlc.Api.Users;

/// <summary>
/// List filter. <see cref="Search"/> matches any of the three fields (OR); the field filters
/// match only their own field; all present terms are ANDed. Matching is a case-insensitive
/// literal substring (spec §2).
/// </summary>
public sealed record UserFilter(string? Search, string? Email, string? FirstName, string? LastName);

public sealed record UserPage(IReadOnlyList<User> Items, int TotalCount);

public enum WriteStatus
{
    Ok,
    NotFound,
    DuplicateEmail,
}

public readonly record struct WriteResult(WriteStatus Status, User? User = null);

public interface IUserRepository
{
    UserPage Search(UserFilter filter, int page, int pageSize);

    User? Get(Guid id);

    /// <returns><see cref="WriteStatus.Ok"/> or <see cref="WriteStatus.DuplicateEmail"/>.</returns>
    WriteResult Create(UserFields fields);

    /// <returns><see cref="WriteStatus.Ok"/>, <see cref="WriteStatus.NotFound"/> or <see cref="WriteStatus.DuplicateEmail"/>.</returns>
    WriteResult Update(Guid id, UserFields fields);

    bool Delete(Guid id);
}
