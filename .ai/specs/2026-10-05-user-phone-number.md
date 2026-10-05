# Spec: User phone number

- **Date:** 2026-10-05
- **Status:** Accepted (architect, step 1 of 5, workflow `smf-sdlc`)
- **Task (PL):** "Dodaj do użytkownika numer telefonu" (add a phone number to the user).
- **Builds on:** `2026-10-05-user-management-api.md`. Every rule there still holds unless this spec overrides it.

## 1. Context

`/api/users` (`src/AgenticSdlc.Api/Users/`) stores `email`, `firstName`, `lastName` in an in-memory, lock-guarded repository. Validation is hand-written in `UserValidator`, which trims input and reports every failing field in one `ValidationProblem`. Tests are xUnit v3 integration tests that read responses as `JsonElement` (`tests/AgenticSdlc.Api.Tests/UsersApi.cs`).

This task adds one field to the user: a phone number.

## 2. Decisions

| Topic | Decision | Why |
| --- | --- | --- |
| Name | JSON `phoneNumber`; C# `PhoneNumber` | Matches the existing camelCase naming (`firstName`). |
| Required? | **Optional.** If it is absent, `null`, `""` or only whitespace, the user has no phone. | Existing clients and users must keep working. Many users have no phone to give. |
| Storage format | **Normalized E.164**: `+` followed by digits only, e.g. `+48600123456`. | One canonical form makes values comparable and safe to show, and makes a later search or uniqueness rule simple. |
| Accepted input | After `Trim()`: an optional leading `+`, or a leading `00` that becomes `+`. Then digits mixed with the separators space, `-`, `.`, `(` and `)`. Separators are removed. **A leading `+` or `00` is required.** | People type `+48 600 123 456` or `0048-600-123-456`. Without a country code there is no correct normalization. We do not guess `+48`, because that would be hidden configuration. |
| Digit rules (after normalization) | 8 to 15 digits after `+`. The first digit is 1–9. | E.164 allows at most 15 digits, and a country code never starts with 0. Using 8 as the minimum rejects obvious junk. |
| Raw length cap | Reject raw (trimmed) input longer than **32** chars before normalizing. | Bounds the work done and gives a clear error for pasted garbage. |
| Library | **None.** Do not add libphonenumber. Validate with a small hand-written loop. Do not use `Regex`. | The previous spec says no new packages. Checking a real numbering plan is out of scope (§8). |
| Uniqueness | **Not unique.** | Users can share a phone (a family, an office line). Email stays the only unique field. |
| Response | `UserResponse.phoneNumber` is **always present**: a string or `null`. | Clients get a stable shape. The ASP.NET default serializer writes `null`; do not add `JsonIgnore(WhenWritingNull)`. |
| `PUT` semantics | Unchanged: **full replace**. If `phoneNumber` is omitted, `null` or blank in a `PUT`, the stored phone is **cleared**. | This matches the spec's "PUT = full replace". There is still no `PATCH`. The README must say this (risk R2). |
| Search / filters | **Not changed.** `search` does not match the phone, and there is no `phoneNumber` filter. Ordering does not change. | The task does not ask for this, and it keeps AC14–AC20 exactly as they are. Possible follow-up in §8. |
| OpenAPI | The path set does not change (`/api/users`, `/api/users/{id}`). The schemas gain `phoneNumber` automatically. | AC21 of the previous spec still holds. |

## 3. Data model & contracts (delta)

```
User               { …, string LastName, string? PhoneNumber, DateTimeOffset CreatedAt, … }   // new positional param right after LastName
UserFields         ( Email, FirstName, LastName, string? PhoneNumber )                      // normalized value or null
CreateUserRequest  ( string? Email, string? FirstName, string? LastName, string? PhoneNumber )
UpdateUserRequest  ( string? Email, string? FirstName, string? LastName, string? PhoneNumber )
UserResponse       ( Id, Email, FirstName, LastName, string? PhoneNumber, CreatedAt, UpdatedAt )
```

A JSON non-string for `phoneNumber` (for example the number `48600123456`) fails in the binder and returns 400. That is framework behaviour and needs no special handling.

### Validation (error key `phoneNumber`; reported together with other field errors)

1. `v = phoneNumber?.Trim()`. If `v` is null or empty, the phone is `null` and valid.
2. If `v.Length > 32`, return `"Phone number must be at most 32 characters."`
3. If `v` starts with `00`, replace that prefix with `+`. If `v` does not then start with `+`, return `"Phone number must start with a country code (+ or 00)."`
4. Go through the rest of the string. Keep the ASCII digits `0-9` (use `char.IsAsciiDigit`, not `char.IsDigit`, so Arabic-Indic and other non-ASCII digits are rejected). Skip the separators ` -.()`. Any other char, including a second `+`, letters, `/`, tabs and control chars, returns `"Phone number may contain only digits, spaces and - . ( ) separators."`
5. If the digit count is outside 8..15, or the first digit is `0`, return `"Phone number must have 8 to 15 digits in international (E.164) format."`
6. Store `"+" + digits`.

Each failure produces exactly one message for the key, as for the other fields. Put this in `UserValidator` as a private `ValidatePhone(errors, value) → string?`. The existing `Validate(email, firstName, lastName, out fields)` gains a `phoneNumber` parameter, and both handlers pass `request.PhoneNumber`.

## 4. Endpoints

The routes and status codes do not change. The bodies change like this:

- `POST /api/users` / `PUT /api/users/{id}` accept an optional `phoneNumber`.
- Every user representation (`POST` 201, `GET` by id, `PUT` 200, list `items[]`) contains `phoneNumber`, which is a normalized string or `null`.
- The order of checks does not change: validate (400, and this includes `phoneNumber`) → 404 → 409.

## 5. Affected files

```
src/AgenticSdlc.Api/Users/User.cs                    # + string? PhoneNumber
src/AgenticSdlc.Api/Users/UserContracts.cs           # requests + response (+ From mapping)
src/AgenticSdlc.Api/Users/UserValidator.cs           # UserFields + ValidatePhone + MaxPhoneLength = 32, Min/MaxPhoneDigits = 8/15
src/AgenticSdlc.Api/Users/UserEndpoints.cs           # pass request.PhoneNumber to Validate
src/AgenticSdlc.Api/Users/InMemoryUserRepository.cs  # Create/Update copy fields.PhoneNumber (inside the existing lock)
README.md                                            # table bodies + phone rules + "PUT omitting phoneNumber clears it"
tests/AgenticSdlc.Api.Tests/UsersPhoneTests.cs       # new (Tester)
tests/AgenticSdlc.Api.Tests/UsersApi.cs              # optional: CreateAsync overload taking phoneNumber; do not break existing callers
```

No `Program.cs` change, no new packages, `IUserRepository` signature unchanged (it already takes `UserFields`).

## 6. Data flow

```
POST {email, firstName, lastName, phoneNumber:" 0048 (600) 123-456 "}
  → bind → UserValidator: trim → "00"→"+" → strip separators → "+48600123456" (10 digits ✓)
  → repo.Create(fields) → User{PhoneNumber="+48600123456"} → 201 {…, "phoneNumber":"+48600123456"}
PUT {email, firstName, lastName}            (no phoneNumber)
  → fields.PhoneNumber = null → stored phone cleared → 200 {…, "phoneNumber":null}
```

## 7. Acceptance criteria

Test isolation is the same as before: every test uses one fresh client per scenario.

1. **PH1** `POST` without `phoneNumber` → `201`, body has `"phoneNumber": null` (the property is present). `GET` returns the same.
2. **PH2** `POST` with `"+48 600 123 456"`, `"0048600123456"`, `"+48-600-123-456"`, `"+48 (600) 123.456"` and `"  +48600123456  "` → `201`, and every one stores `"+48600123456"`. `GET` by id returns the same value.
3. **PH3** `POST` with `phoneNumber` `""`, `"   "` or `null` → `201`, `phoneNumber: null`.
4. **PH4** Invalid → `400` whose `errors` contain exactly `phoneNumber`: `"600123456"` (no country code), `"+48 600 abc"`, `"+48/600123456"`, `"++48600123456"`, `"+0123456789"`, `"+1234567"` (7 digits), `"+1234567890123456"` (16 digits), a 33-char string, `"+48\t600123456"`, `"+٤٨٦٠٠١٢٣٤٥٦"` (Arabic-Indic digits).
5. **PH5** Boundaries: `"+12345678"` (8 digits) → `201`; `"+123456789012345"` (15 digits) → `201`.
6. **PH6** All errors are reported together: invalid email + invalid phone → `400` with keys `email` and `phoneNumber`.
7. **PH7** `PUT` sets, changes and normalizes the phone → `200`, and a later `GET` shows it. `PUT` without `phoneNumber` on a user that has one → `200`, `phoneNumber: null` (cleared).
8. **PH8** `PUT` with an invalid phone on an **unknown** id → `400`, because validation runs before 404.
9. **PH9** Two users can have the same phone → both `201`. No 409.
10. **PH10** List `items[]` contain `phoneNumber`. `search=600123` does **not** match by phone (it returns users only through email or name matches).
11. **PH11** A JSON number for `phoneNumber` → `400`.
12. **PH12** All existing tests stay green without changes (AC1–AC23 of the previous spec). The OpenAPI path set does not change. `npm test` is green with zero build warnings.

## 8. Edge cases & risks

- **R1 PII.** A phone number is personal data. Do not log it. The existing rule against logging request bodies applies to it too.
- **R2 Clearing on PUT.** A client written before this change sends `PUT` without `phoneNumber`, which silently erases a phone that another client set. This is accepted because it is the consequence of full-replace semantics. Document it in the README. The Reviewer should mention it as a known trade-off and not "fix" it by making `PUT` partial.
- **R3 No real numbering-plan validation.** `+99912345678` passes even though it is not a real number. Accepted: there is no new package. If plan-level accuracy is needed later, that means adopting libphonenumber in a separate spec.
- **R4 Numbers without a country code are rejected.** Polish users who type `600 123 456` get a clear 400 message. We do not assume a default region.
- **R5 Extensions** (`+48 22 123 45 67 ext. 12`) are rejected: letters are not allowed. Out of scope.
- Unicode: use `char.IsAsciiDigit` only. Full-width and other non-ASCII digits are rejected, not normalized.
- Follow-up candidates, not part of this task: a `phoneNumber` list filter; matching `search` against digits.

## 9. Handoff

- **Step 2 (Tester):** add `UsersPhoneTests.cs` covering PH1–PH11 so the build is red, as in the previous TDD flow. Use `JsonElement` assertions and do not depend on production types. Do not edit existing tests. If you add a helper overload, keep the current `CreateAsync` signature.
- **Step 3 (Developer):** implement §3–§5 exactly. Keep handlers thin, keep repository writes inside the lock, and keep records immutable. Update the README. `npm test` must be green with zero warnings.
- **Steps 4–5 (Review / QA):** verify normalization, `IsAsciiDigit`, the 32-char cap applied before any processing, `phoneNumber` always serialized, PUT clearing documented, no logging of phone numbers, and no new packages.
