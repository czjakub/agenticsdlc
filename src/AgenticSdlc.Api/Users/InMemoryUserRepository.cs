namespace AgenticSdlc.Api.Users;

/// <summary>
/// Process-local store: data is lost on restart and not shared across instances (spec R2).
/// One lock guards all state, so the email-uniqueness check and the write are atomic (spec R4).
/// </summary>
public sealed class InMemoryUserRepository(TimeProvider time) : IUserRepository
{
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, User> _users = [];

    public UserPage Search(UserFilter filter, int page, int pageSize)
    {
        User[] snapshot;
        lock (_lock)
        {
            snapshot = [.. _users.Values];
        }

        var matches = snapshot
            .Where(u => Matches(u, filter))
            .OrderBy(u => u.LastName, Cmp)
            .ThenBy(u => u.FirstName, Cmp)
            .ThenBy(u => u.Email, Cmp)
            .ThenBy(u => u.Id)
            .ToList();

        // long arithmetic: a huge page number must not overflow into a negative offset (spec §8).
        var offset = (long)(page - 1) * pageSize;
        IReadOnlyList<User> items = offset >= matches.Count
            ? []
            : matches.Skip((int)offset).Take(pageSize).ToList();
        return new UserPage(items, matches.Count);
    }

    public User? Get(Guid id)
    {
        lock (_lock)
        {
            return _users.GetValueOrDefault(id);
        }
    }

    public WriteResult Create(UserFields fields)
    {
        lock (_lock)
        {
            if (EmailTaken(fields.Email, exceptId: null))
                return new WriteResult(WriteStatus.DuplicateEmail);

            var now = time.GetUtcNow();
            var user = new User(Guid.CreateVersion7(), fields.Email, fields.FirstName, fields.LastName, now, now);
            _users.Add(user.Id, user);
            return new WriteResult(WriteStatus.Ok, user);
        }
    }

    public WriteResult Update(Guid id, UserFields fields)
    {
        lock (_lock)
        {
            if (!_users.TryGetValue(id, out var existing))
                return new WriteResult(WriteStatus.NotFound);
            if (EmailTaken(fields.Email, exceptId: id))
                return new WriteResult(WriteStatus.DuplicateEmail);

            var updated = existing with
            {
                Email = fields.Email,
                FirstName = fields.FirstName,
                LastName = fields.LastName,
                UpdatedAt = time.GetUtcNow(),
            };
            _users[id] = updated;
            return new WriteResult(WriteStatus.Ok, updated);
        }
    }

    public bool Delete(Guid id)
    {
        lock (_lock)
        {
            return _users.Remove(id);
        }
    }

    // Caller holds _lock.
    private bool EmailTaken(string email, Guid? exceptId) =>
        _users.Values.Any(u => u.Id != exceptId && Cmp.Equals(u.Email, email));

    private static bool Matches(User u, UserFilter f) =>
        (f.Search is null || Contains(u.Email, f.Search) || Contains(u.FirstName, f.Search) || Contains(u.LastName, f.Search))
        && (f.Email is null || Contains(u.Email, f.Email))
        && (f.FirstName is null || Contains(u.FirstName, f.FirstName))
        && (f.LastName is null || Contains(u.LastName, f.LastName));

    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
