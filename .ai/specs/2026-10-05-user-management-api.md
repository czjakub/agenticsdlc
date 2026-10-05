# Spec: User management API (CRUD + list with search)

- **Date:** 2026-10-05
- **Status:** Accepted (architect step 1 of 4, workflow `smf-sdlc`)
- **Task (PL):** "Do api dodaj zarządzanie użytkownikami (dodawanie, edycja, usuwanie) oraz listę użytkowników z ich wyszukiwaniem po polu email, firstName oraz lastName."
- **Builds on:** `2026-10-05-dotnet-minimal-api-skeleton.md` (all its rules still hold unless overridden in §9).

## 1. Context

The repo is a .NET 10 Minimal API skeleton (`src/AgenticSdlc.Api`, `Program.cs` only) with OpenAPI + Scalar (Development only), `/health`, and xUnit v3 integration tests on `WebApplicationFactory<Program>` (`tests/AgenticSdlc.Api.Tests`). `npm test` → `dotnet test --solution AgenticSdlc.slnx`; SDK is at `~/.dotnet` (10.0.401), the npm shim already prepends it to `PATH`.

This is the first business feature: create, update, delete users, get one, and list users with search over `email`, `firstName`, `lastName`.

## 2. Decisions

| Topic | Decision | Why |
| --- | --- | --- |
| Persistence | **In-memory store** (`InMemoryUserRepository`, singleton) behind `IUserRepository` | No database exists and the skeleton spec ruled one out; the interface makes swapping in EF Core later a one-class change. Data is lost on restart — documented in README. |
| Thread safety | Repository guards all state with a single `lock` (or `Lock`); email-uniqueness check + write happen inside the same critical section | Singleton under concurrent requests; check-then-insert must be atomic or two POSTs can create duplicate emails. |
| Route shape | `MapGroup("/api/users").WithTags("Users")`, endpoints in `Users/UserEndpoints.cs` as `MapUserEndpoints(this IEndpointRouteBuilder)` | Keeps `Program.cs` thin; one feature folder readable in one sitting. |
| Id | `Guid` generated server-side (`Guid.CreateVersion7()`); route constraint `{id:guid}` | Non-enumerable, no client-chosen ids; a non-GUID segment is a plain 404 from routing. |
| Update semantics | `PUT` = full replace of the three editable fields. No `PATCH`. | Simplest correct contract; all three fields are required anyway. |
| Validation | **Hand-written** `UserValidator` returning `Dictionary<string,string[]>` → `TypedResults.ValidationProblem(errors)` (400, `application/problem+json`) | Deterministic, no source-generator surprises, and we must trim/normalize before validating anyway. Do not add FluentValidation. |
| Errors | `builder.Services.AddProblemDetails()`; 404 and 409 returned as `TypedResults.Problem(statusCode: …, title: …)` | Uniform RFC 9457 error bodies. |
| Return types | `TypedResults` with `Results<…>` union return types | Endpoints describe their status codes in OpenAPI automatically. |
| Search | `search` = case-insensitive substring match on **any** of email/firstName/lastName (OR); optional `email`, `firstName`, `lastName` = case-insensitive substring on that field (AND with each other and with `search`) | Covers both "one search box" and "filter by field" readings of the task with one small predicate. |
| Paging | `page` (default 1, ≥1), `pageSize` (default 20, 1..100) | Unbounded lists are a DoS footgun. |
| Ordering | `lastName`, then `firstName`, then `email`, then `id` (ordinal ignore-case) | Deterministic pages; tests can assert order. |
| Time | `TimeProvider` injected (`TimeProvider.System` registered) for `createdAt`/`updatedAt` | Testable timestamps without a clock abstraction of our own. |
| Exposure | User endpoints mapped in **all** environments (unlike docs) | It is the API. **No auth** — see §8 risk R1. |
| New packages | **None** | Everything is in `Microsoft.AspNetCore.App`. |

## 3. Data model & contracts

Domain (internal to the repository):

```
User { Guid Id; string Email; string FirstName; string LastName; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt }
```

HTTP DTOs (records, JSON camelCase — ASP.NET default):

```
CreateUserRequest  { string? Email; string? FirstName; string? LastName }   // nullable so missing fields reach the validator → 400, not a binder 400 without field errors
UpdateUserRequest  { string? Email; string? FirstName; string? LastName }
UserResponse       { Guid Id; string Email; string FirstName; string LastName; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt }
PagedResponse<T>   { IReadOnlyList<T> Items; int Page; int PageSize; int TotalCount }
```

Unknown JSON properties (e.g. a client-sent `id`, `createdAt`) are ignored.

### Validation rules (applied after `Trim()`)

| Field | Rules | Error key |
| --- | --- | --- |
| `email` | required (non-null, non-whitespace); length ≤ 254; valid address: `MailAddress.TryCreate(value, out var a)` **and** `a.Address == value` (rejects `"Name <x@y>"` display-name forms) and contains no whitespace | `email` |
| `firstName` | required; length 1..100 | `firstName` |
| `lastName` | required; length 1..100 | `lastName` |

- Stored values are the **trimmed** values. Email is stored as given (trimmed), but **uniqueness is case-insensitive** (`StringComparer.OrdinalIgnoreCase`).
- All failing fields are reported in one response (not first-error-only).
- Query validation: `page < 1` → error key `page`; `pageSize` outside 1..100 → `pageSize`; any of `search`/`email`/`firstName`/`lastName` longer than 254 chars → that key. Empty/whitespace query filters are treated as absent.

## 4. Endpoints

All under `/api/users`. Request/response bodies `application/json`; errors `application/problem+json`.

| Method & path | Success | Errors |
| --- | --- | --- |
| `GET /api/users?search=&email=&firstName=&lastName=&page=&pageSize=` | `200 PagedResponse<UserResponse>` (empty `items` when nothing matches — never 404) | `400` invalid query |
| `GET /api/users/{id:guid}` | `200 UserResponse` | `404` |
| `POST /api/users` | `201 UserResponse`, `Location: /api/users/{id}` | `400` validation / missing or malformed body; `409` email already used |
| `PUT /api/users/{id:guid}` | `200 UserResponse` (`updatedAt` advanced, `createdAt` unchanged) | `400`; `404`; `409` email used by **another** user (re-saving own email, in any case, is OK) |
| `DELETE /api/users/{id:guid}` | `204` | `404` (deleting twice → second is 404) |

Order of checks for `PUT`: validate body (400) → find user (404) → uniqueness (409). For `POST`: validate (400) → uniqueness (409).

## 5. Components & files

```
src/AgenticSdlc.Api/
  Program.cs                       # + AddProblemDetails(), AddSingleton(TimeProvider.System),
                                   #   AddSingleton<IUserRepository, InMemoryUserRepository>(), app.MapUserEndpoints()
  Users/
    User.cs                        # domain record/class
    UserContracts.cs               # CreateUserRequest, UpdateUserRequest, UserResponse, PagedResponse<T>, UserQuery (query-binding record, [AsParameters])
    UserValidator.cs               # static Validate(request) / Validate(query) → errors dict; also returns normalized (trimmed) values
    IUserRepository.cs             # interface + result enums
    InMemoryUserRepository.cs      # lock-guarded Dictionary<Guid,User>
    UserEndpoints.cs               # MapUserEndpoints: group + 5 handlers, mapping User → UserResponse
tests/AgenticSdlc.Api.Tests/
  Users*Tests.cs                   # new (Tester)
  OpenApiTests.cs                  # AC4 test replaced (see §9)
README.md                          # endpoint table + "in-memory, lost on restart, no auth"
```

Repository interface (contract, not final code):

```
IReadOnlyList<User> / int  Search(UserFilter filter, int page, int pageSize) → (Items, TotalCount)
User?                      Get(Guid id)
CreateResult               Create(string email, string firstName, string lastName)      // Created(User) | DuplicateEmail
UpdateResult               Update(Guid id, string email, string firstName, string lastName) // Updated(User) | NotFound | DuplicateEmail
bool                       Delete(Guid id)
```

Handlers stay thin: validate → call repository → map result to `TypedResults`. No business logic in `Program.cs`.

## 6. Data flow

```
POST /api/users {json}
  → JSON binding (malformed JSON → 400 by framework)
  → UserValidator (trim, rules) ──fail──→ 400 ValidationProblem {errors:{field:[msg]}}
  → repo.Create (lock: email exists? → DuplicateEmail → 409 Problem)
  → 201 + Location + UserResponse

GET /api/users?search=kow&page=1
  → [AsParameters] UserQuery → validate → repo.Search
     filter: (search empty || email∋s || firstName∋s || lastName∋s)  ∧ (email filter) ∧ (firstName filter) ∧ (lastName filter)
     order → count total → skip/take (snapshot copied inside the lock, filtering may run outside)
  → 200 PagedResponse
```

## 7. Acceptance criteria (Tester → tests)

Isolation note: every `ApiFactory.For(factory, env)` call builds a **new host → new empty in-memory store**. A test must create its client once and run its whole scenario on it; tests must not rely on data from other tests.

**Create**
1. **AC1** `POST` valid body → `201`, `Location` = `/api/users/{id}`, body has a GUID `id`, trimmed fields, `createdAt == updatedAt`.
2. **AC2** `GET` the `Location` → `200` with the same user.
3. **AC3** Missing/empty/whitespace `email`, `firstName`, `lastName` → `400` `application/problem+json` with `errors` containing each failing key (all at once).
4. **AC4** Invalid emails (`"not-an-email"`, `"a@"`, `"John <j@x.io>"`, `"a b@x.io"`, 255-char address) → `400` with `errors.email`.
5. **AC5** `firstName`/`lastName` of 101 chars → `400`; 100 chars → `201`.
6. **AC6** Duplicate email, differing only in case/surrounding whitespace → `409` problem.
7. **AC7** Malformed JSON / empty body → `400`.
8. **AC8** Client-sent `id` in body is ignored (server id differs).

**Read**
9. **AC9** `GET /api/users/{random guid}` → `404` problem; `GET /api/users/not-a-guid` → `404`.

**Update**
10. **AC10** `PUT` valid → `200`, fields changed, `createdAt` unchanged, `updatedAt` ≥ `createdAt`; subsequent `GET` reflects it.
11. **AC11** `PUT` unknown id → `404`; invalid body → `400` (even for unknown id, validation first).
12. **AC12** `PUT` with another user's email (any case) → `409`; `PUT` keeping own email in different case → `200`.

**Delete**
13. **AC13** `DELETE` existing → `204`; then `GET` → `404`; second `DELETE` → `404`; user no longer in list.

**List & search** (seed e.g. Jan Kowalski `jan.kowalski@example.com`, Anna Nowak `anna@nowak.pl`, Piotr Kowalczyk `piotr@example.com`)
14. **AC14** No params → `200`, all users, `totalCount` correct, `page=1`, `pageSize=20`, ordered lastName→firstName→email.
15. **AC15** `search` matches substring, case-insensitive, on each field separately: `search=KOWAL` → Kowalski+Kowalczyk (lastName); `search=anna` → Nowak (firstName/email); `search=nowak.pl` → Nowak (email).
16. **AC16** Field filters: `lastName=kowal` → 2; `firstName=jan` → Jan only; `email=example.com` → Jan+Piotr; `firstName=jan&lastName=nowak` → empty (AND).
17. **AC17** No match → `200` with empty `items`, `totalCount=0`.
18. **AC18** Paging: `pageSize=2&page=2` with 3 users → 1 item, `totalCount=3`; page beyond end → empty `items`, same `totalCount`.
19. **AC19** `page=0`, `pageSize=0`, `pageSize=101` → `400` with the matching `errors` key.
20. **AC20** Search input is treated literally (e.g. `search=%`, `search=.*`) — no wildcard/regex semantics, no 500.

**Cross-cutting**
21. **AC21** OpenAPI (Development) `paths` contains exactly `/api/users` and `/api/users/{id}` and still no `/health`.
22. **AC22** User endpoints work in `Production` (smoke: POST + GET list).
23. **AC23** `npm test` green, zero build warnings. All pre-existing tests stay green except the one replaced per §9.

## 8. Edge cases & risks

- **R1 No authentication/authorization.** Anyone reaching the API can read, change and delete users. Accepted for this iteration (task does not ask for it); README must say so, and the Reviewer should flag it as a known gap, not fix it by inventing an auth scheme.
- **R2 In-memory data.** Lost on restart and not shared across instances. Accepted; interface isolates the swap.
- **R3 PII in logs.** Do not log emails/names; no request-body logging.
- **R4 Race on uniqueness.** Must be inside the repository lock (AC6 tests the logic; the lock is a review item).
- **R5 Mutable state leaks.** Repository must never hand out references that callers can mutate into the store — use immutable records (`with` for updates).
- Unicode: comparisons use `OrdinalIgnoreCase`; no culture-sensitive `ToLower()`.
- Very large page numbers: `(page-1)*pageSize` must not overflow — compute with `long` or cap `page` (e.g. reject `page > 1_000_000` with 400 `page`).
- `HEAD`/`OPTIONS`/other verbs on these routes → 405 from routing; no special handling.

## 9. Changes to the skeleton spec

- Skeleton **AC4 ("OpenAPI `paths` is empty") is superseded** by AC21 above. The Tester replaces `OpenApiTests.Document_has_no_paths` with a test asserting the exact path set. This is a deliberate spec change, not test weakening — the Developer must not touch it further.
- `RoutingTests` stays as is: `/api` and `/api/anything` remain 404 (no catch-all, no `/api` root handler).
- `Document_does_not_describe_health` stays.

## 10. Handoff

- **Tester (step 2):** write `UsersTests` (may split into Create/Update/Delete/List classes) covering AC1–AC22, replace the AC4 test per §9. To compile red, add only the minimal public DTO types the tests reference if needed — prefer asserting via `JsonElement`/anonymous objects so no production types are required.
- **Developer (step 3):** implement §5 files per §2–§4, keep `Program.cs` thin, update README, `npm test` green, zero warnings, no new packages.
- **Reviewer (step 4):** verify lock-guarded uniqueness (R4), immutability (R5), validation completeness, `OrdinalIgnoreCase` usage, paging bounds, no PII logging, R1/R2 documented.
