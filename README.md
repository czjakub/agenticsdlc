# AgenticSdlc

Minimal ASP.NET Core (.NET 10) API: user management (CRUD + searchable list), OpenAPI + Scalar API reference and a health check.

## Requirements

- .NET SDK 10 (`global.json` pins `10.0.100`, rolling forward to the latest feature band)

## Run

```bash
npm start            # or: dotnet run --project src/AgenticSdlc.Api
```

The `http` launch profile listens on http://localhost:5080 in `Development` and opens the Scalar UI.

| Path | Environment | What |
| --- | --- | --- |
| `GET /health` | all | Liveness probe, `200 Healthy` (`text/plain`), GET/HEAD only, excluded from OpenAPI |
| `GET /openapi/v1.json` | Development only | OpenAPI 3 document |
| `GET /scalar` | Development only | Scalar API reference UI |

## Users API

Specs: `.ai/specs/2026-10-05-user-management-api.md`, `.ai/specs/2026-10-05-user-phone-number.md`. Mapped in every environment. Errors are RFC 9457 problem details (`application/problem+json`).

| Method & path | Success | Errors |
| --- | --- | --- |
| `GET /api/users?search=&email=&firstName=&lastName=&page=1&pageSize=20` | `200` `{ items, page, pageSize, totalCount }` | `400` invalid query |
| `GET /api/users/{id}` | `200` user | `404` |
| `POST /api/users` `{ email, firstName, lastName, phoneNumber? }` | `201` user + `Location` | `400` validation, `409` email taken |
| `PUT /api/users/{id}` `{ email, firstName, lastName, phoneNumber? }` | `200` user | `400`, `404`, `409` |
| `DELETE /api/users/{id}` | `204` | `404` |

- `search` is a case-insensitive literal substring matched against email, firstName **or** lastName; `email`/`firstName`/`lastName` filter only their own field. All given terms are ANDed.
- `pageSize` is 1..100 (default 20); results are ordered by lastName, firstName, email.
- Email is unique case-insensitively; all fields are trimmed; names are 1..100 chars with no control characters, email ≤ 254.
- `phoneNumber` is optional and not unique; every user representation carries it as a string or `null`. Input must start with a country code (`+` or `00`), may use space `-` `.` `(` `)` as separators, is at most 32 chars, and is stored normalized to E.164 (`+` and 8–15 digits, first digit 1–9), e.g. `0048 (600) 123-456` → `+48600123456`. Blank or `null` means no phone. `search` does not match it.
- `PUT` is a **full replace**: omitting `phoneNumber` (or sending `null`/blank) **clears** a stored phone.

> **Known limitations:** storage is **in-memory** — data is lost on restart and not shared between instances. There is **no authentication or authorization**: anyone who can reach the API can read, change and delete users.

## Build & test

```bash
npm run build        # dotnet build AgenticSdlc.slnx
npm test             # dotnet test --solution AgenticSdlc.slnx (xUnit v3 on Microsoft.Testing.Platform)
```

The npm scripts prepend `~/.dotnet` to `PATH`, so a user-local SDK install works too.
