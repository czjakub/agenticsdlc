# Spec: User birth date

- **Date:** 2026-10-06
- **Status:** Accepted (architect, step 1 of 5, workflow `smf-sdlc`)
- **Task (PL):** "Dodaj pole z datą urodzin do encji User" (add a birth-date field to the User entity).
- **Builds on:** `2026-10-05-user-management-api.md` and `2026-10-05-user-phone-number.md`. Every rule in those specs still holds unless this spec overrides it.

## 1. Context

`/api/users` (`src/AgenticSdlc.Api/Users/`) stores `email`, `firstName`, `lastName` and an optional `phoneNumber` in an in-memory, lock-guarded repository. Validation is hand-written in `UserValidator`. It trims input, reports every failing field in one `ValidationProblem`, and returns the normalized values as `UserFields`. `TimeProvider` is registered as a singleton (`Program.cs`), and the repository uses it for timestamps. Tests are xUnit v3 integration tests that read responses as `JsonElement` (`tests/AgenticSdlc.Api.Tests/UsersApi.cs`).

This task adds one field: the user's date of birth. The phone-number feature (`UsersPhoneTests.cs`) is the template. Follow its shape closely.

## 2. Decisions

| Topic | Decision | Why |
| --- | --- | --- |
| Name | JSON `birthDate`; C# `BirthDate` | camelCase, as for the other fields. `birthDate` is the schema.org / common API name. |
| Domain type | `DateOnly?` on `User`, `UserFields` and `UserResponse` | A birth date is a calendar date with no time and no time zone. `DateTimeOffset` would invite off-by-one-day bugs across time zones. |
| Wire format | ISO 8601 calendar date **`yyyy-MM-dd`** only, e.g. `"1990-05-17"` | System.Text.Json writes `DateOnly` exactly like this, so the response needs no custom converter. |
| Request type | `string? BirthDate` in `CreateUserRequest` / `UpdateUserRequest`, **not** `DateOnly?` | If the request used `DateOnly?`, a bad value such as `"17.05.1990"` would fail in the binder and return a 400 **without** a `birthDate` field error. Taking a string keeps the existing rule that every field error is reported together under its key (user-management spec §3). A JSON non-string, such as a number, still fails in the binder with 400, as `phoneNumber` does. |
| Required? | **Optional.** If it is absent, `null`, `""` or only whitespace, the user has no birth date. | Existing clients and stored users must keep working. |
| Parsing | `v.Trim()`, then `DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)` | Strict and culture-independent. It rejects `1990-5-17`, `17.05.1990`, `1990/05/17`, datetimes (`1990-05-17T00:00:00Z`), impossible dates (`1990-02-30`, `2023-02-29`) and non-ASCII digits. No `Regex`. |
| Not in the future | `d <= today`. `today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)`, using the **injected `TimeProvider`**. | A future birth date is always wrong. Using `TimeProvider` makes the rule testable with a fixed clock. Today itself is allowed (newborns). |
| Lower bound | `d >= today.AddYears(-150)` (inclusive) | Rejects typo years like `0990` or `1066` without a fixed constant that grows stale. 150 years is safely above the oldest recorded human. |
| Minimum age | **None.** | The task does not ask for one. An age gate is a business rule for a separate spec (§8). |
| Response | `UserResponse.birthDate` is **always present**: `"yyyy-MM-dd"` or `null`. | Gives a stable shape, the same as `phoneNumber`. Do not add `JsonIgnore(WhenWritingNull)`. |
| Derived age | **Not exposed.** There is no `age` field. | Age changes daily and is cheap to compute on the client. It also avoids a time-zone debate on the server. |
| `PUT` semantics | Unchanged: **full replace**. If `birthDate` is omitted, `null` or blank in a `PUT`, the stored value is **cleared**. | Consistent with `phoneNumber` (phone spec R2). There is still no `PATCH`. |
| Search / filters / ordering | **Not changed.** There is no `birthDate` filter, `search` does not match it, and the ordering is unchanged. | Out of scope. This keeps the list ACs intact. |
| Uniqueness | Not unique. | Obvious. |
| Packages | **None new.** | Matches the prior specs. |
| OpenAPI | The path set does not change. The response schema gains `birthDate` (`string`, `format: date`, nullable). The request schema shows it as a nullable `string`. | AC21 of the user-management spec still holds. A missing `format: date` on the *request* schema is accepted, and the README documents the format. The Developer *may* add `[property: Description("ISO date yyyy-MM-dd")]`, but does not have to. |

## 3. Data model & contracts (delta)

```
User               { …, string? PhoneNumber, DateOnly? BirthDate, DateTimeOffset CreatedAt, … }   // new positional param right after PhoneNumber
UserFields         ( Email, FirstName, LastName, string? PhoneNumber, DateOnly? BirthDate )
CreateUserRequest  ( string? Email, string? FirstName, string? LastName, string? PhoneNumber, string? BirthDate )
UpdateUserRequest  ( string? Email, string? FirstName, string? LastName, string? PhoneNumber, string? BirthDate )
UserResponse       ( Id, Email, FirstName, LastName, string? PhoneNumber, DateOnly? BirthDate, CreatedAt, UpdatedAt )
```

### Validation (error key `birthDate`; reported together with the other field errors)

`UserValidator.Validate` gains two parameters, `string? birthDate` and `DateOnly today`. The new order is `(email, firstName, lastName, phoneNumber, birthDate, today, out fields)`. The validator stays static and clock-free: **the caller supplies `today`**. Add a private `ValidateBirthDate(errors, value, today) → DateOnly?`:

1. Set `v = value?.Trim()`. If `v` is null or empty, the result is `null` and valid.
2. If `v.Length != 10` or `TryParseExact` fails, the error is `"Birth date must be a valid date in yyyy-MM-dd format."` (The length check is a cheap guard. `TryParseExact` alone would also reject, but the guard bounds the work done on pasted garbage.)
3. If `d > today`, the error is `"Birth date cannot be in the future."`
4. If `d < today.AddYears(-MaxAgeYears)`, the error is `"Birth date must be within the last 150 years."` (Use `MaxAgeYears = 150` as a public const, like the other limits.)
5. Otherwise return `d`.

Each failure produces exactly one message.

### Endpoints wiring

The `Create` and `Update` handlers each gain a `TimeProvider time` parameter. Minimal APIs resolve it from DI because it is registered, and it does not appear in OpenAPI. Each handler computes `today` once and passes `request.BirthDate, today`. Inject `TimeProvider`; do **not** read `DateTime.UtcNow` directly.

## 4. Endpoints

The routes, status codes and check order (validate → 404 → 409) do not change.

- `POST /api/users` and `PUT /api/users/{id}` accept an optional `birthDate`.
- Every user representation contains `birthDate`: `POST` 201, `GET` by id, `PUT` 200, and the list `items[]`.

## 5. Affected files

```
src/AgenticSdlc.Api/Users/User.cs                    # + DateOnly? BirthDate (after PhoneNumber)
src/AgenticSdlc.Api/Users/UserContracts.cs           # requests (string?) + response (DateOnly?) + From mapping
src/AgenticSdlc.Api/Users/UserValidator.cs           # UserFields + ValidateBirthDate + MaxAgeYears = 150; Validate signature
src/AgenticSdlc.Api/Users/UserEndpoints.cs           # inject TimeProvider in Create/Update, pass birthDate + today
src/AgenticSdlc.Api/Users/InMemoryUserRepository.cs  # Create/Update copy fields.BirthDate (inside the existing lock)
README.md                                            # request bodies table + birthDate rules + "PUT omitting birthDate clears it"
tests/AgenticSdlc.Api.Tests/UsersBirthDateTests.cs   # new (Tester)
tests/AgenticSdlc.Api.Tests/FixedTimeProvider.cs     # new test helper (Tester): sealed class : TimeProvider, overrides GetUtcNow()
```

There is no `Program.cs` change, no new package and no change to the `IUserRepository` signature. Do **not** add `Microsoft.Extensions.TimeProvider.Testing`. The hand-written `FixedTimeProvider` is only a few lines.

**Fixed clock in tests:** the Tester builds the host with
`ApiFactory.For(factory, Environments.Development).WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new FixedTimeProvider(...))))`.
This replaces `TimeProvider.System`, because the last registration wins for a single resolve. Choose a fixed "now" such as `2026-10-06T12:00:00Z`, so `today = 2026-10-06`. Boundary tests must **not** depend on the real clock, which would make them flaky around midnight UTC.

## 6. Data flow

```
POST {email, firstName, lastName, birthDate:" 1990-05-17 "}
  → bind (string) → handler: today = DateOnly(TimeProvider.GetUtcNow())
  → UserValidator: trim → TryParseExact → 1990-05-17 ≤ today ✓, ≥ today−150y ✓
  → repo.Create(fields) → User{BirthDate=1990-05-17} → 201 {…, "birthDate":"1990-05-17"}
PUT {email, firstName, lastName}   (no birthDate) → fields.BirthDate = null → cleared → 200 {…, "birthDate":null}
```

## 7. Acceptance criteria

Each scenario uses a fresh client. Boundary ACs use the fixed clock `now = 2026-10-06T12:00:00Z`.

1. **BD1** `POST` without `birthDate` → `201`, and the body has `"birthDate": null` (the property is present). `GET` returns the same.
2. **BD2** `POST` with `"1990-05-17"` or `"  1990-05-17  "` → `201`, stored and returned as exactly `"1990-05-17"`. `GET` by id returns the same.
3. **BD3** `POST` with `birthDate` `""`, `"   "` or `null` → `201`, `birthDate: null`.
4. **BD4** Invalid format → `400`, and `errors` contains exactly the key `birthDate`. Inputs: `"17.05.1990"`, `"1990/05/17"`, `"1990-5-17"`, `"90-05-17"`, `"1990-05-17T00:00:00Z"`, `"1990-05-17x"`, `"1990 -05-17"`, `"1990-02-30"`, `"2023-02-29"`, `"abcd-ef-gh"`, `"١٩٩٠-٠٥-١٧"` (Arabic-Indic digits) and a 200-char string.
5. **BD5** Leap day `"2024-02-29"` → `201`.
6. **BD6** Future (fixed clock): `"2026-10-07"` → `400` `birthDate`. Today, `"2026-10-06"`, → `201`.
7. **BD7** Lower bound (fixed clock): `"1876-10-06"` (exactly 150 years) → `201`. `"1876-10-05"` → `400` `birthDate`. `"0001-01-01"` → `400`.
8. **BD8** All errors are reported together: invalid email + invalid phone + invalid birth date → `400` with the keys `email`, `phoneNumber` and `birthDate`.
9. **BD9** `PUT` sets and changes the birth date → `200`, and a later `GET` shows the new value. `PUT` without the field, or with `null` or blank, on a user that has one → `200`, `birthDate: null` (cleared). `PUT` with an invalid value → `400`, and the stored value is unchanged.
10. **BD10** `PUT` with an invalid `birthDate` on an **unknown** id → `400`, because validation runs before 404.
11. **BD11** List `items[]` contain `birthDate`, a string or `null`. `search=1990` does **not** match by birth date.
12. **BD12** A JSON non-string `birthDate` (for example `19900517`, `true` or `{}`) → `400`, and nothing is stored.
13. **BD13** The `birthDate` and `phoneNumber` fields are independent. Setting one does not affect the other.
14. **BD14** All existing tests, including `UsersPhoneTests`, stay green without edits. The OpenAPI path set does not change. `npm test` is green with zero build warnings.

## 8. Edge cases & risks

- **R1 PII.** A date of birth is personal data and, together with the name, is identifying. Do not log it.
- **R2 Clearing on PUT.** A client written before this change sends a `PUT` without `birthDate`, which silently erases the stored value. This is the same accepted trade-off as for the phone. The README must document it. Do not "fix" this by making `PUT` partial.
- **R3 Time-zone edge on "today".** "Today" is the UTC date. In the hours when local time east of UTC is already the next day, someone born on that local "today" is rejected as a future date until UTC catches up. This window is at most 14 hours on the user's day of birth. We accept it rather than add a time-zone input.
- **R4 Strict format.** Only `yyyy-MM-dd` is accepted. Clients that send `DateTime` strings get a clear 400. This is intentional: it rules out ambiguity about what a time-of-day component means.
- **R5 Validator needs `today`.** Any future caller of `UserValidator.Validate` must supply a date from `TimeProvider`. Keeping the validator free of a static clock is what makes the boundaries testable.
- Follow-up candidates, not part of this task: a minimum-age rule, a birth-date range filter, and a derived `age`.

## 9. Handoff

- **Step 2 (Tester):** add `FixedTimeProvider.cs` and `UsersBirthDateTests.cs` covering BD1–BD13, so the build or tests are red (TDD). Use `JsonElement` assertions and depend on no production types. Do not edit existing tests or the `UsersApi.CreateAsync` signature.
- **Step 3 (Developer):** implement §3–§5 exactly. Keep handlers thin, keep repository writes inside the lock, keep records immutable, use the injected `TimeProvider`, and use invariant-culture exact parsing. Update the README. `npm test` must be green with zero warnings.
- **Steps 4–5 (Review / QA):** verify the strict `yyyy-MM-dd` parsing (`InvariantCulture`, `TryParseExact`), the future/150-year bounds against the injected clock, that `birthDate` is always serialized as a date-only string, that clearing on PUT is documented, that the value is not logged, and that no packages were added.
