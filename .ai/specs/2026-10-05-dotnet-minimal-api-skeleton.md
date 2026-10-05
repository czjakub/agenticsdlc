# Spec: .NET Minimal API skeleton (Scalar + health check, no business endpoints)

- **Date:** 2026-10-05
- **Status:** Accepted (architect step 1 of 4, workflow `smf-sdlc`)
- **Task:** Create a new minimal API with .NET. It must have Scalar (API reference UI) and a health check implemented, and **no API endpoints yet**.

## 1. Context

The repository is empty (no commits, only `.ai/`). This is a greenfield scaffold.

Two hard constraints from the workflow (`.ai/cezar/workflows/smf-sdlc.yaml`):

1. Step 4 is a check step running **`npm test`** verbatim, and the developer/reviewer steps also run `npm test`. A .NET repo has no npm by default, so the repo **must ship a root `package.json`** whose `test` script delegates to `dotnet test`. Without it the pipeline fails regardless of code quality.
2. The environment the architect ran in has **no `dotnet` on PATH**. The implementer must verify `dotnet --list-sdks` first; if the SDK is missing, install .NET 10 SDK user-locally (`dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet`) and make the `npm test` script resolve it (see §5). Do not commit the SDK.

## 2. Decisions

| Topic | Decision | Why |
| --- | --- | --- |
| Runtime | **.NET 10 (`net10.0`)**, current LTS | Built-in OpenAPI document generation (`Microsoft.AspNetCore.OpenApi`), long support window. |
| SDK pinning | `global.json` with `"version": "10.0.100", "rollForward": "latestFeature"` | Reproducible builds without blocking newer patch/feature bands. |
| API style | ASP.NET Core **Minimal API**, top-level statements in `Program.cs` | Task requirement. |
| OpenAPI doc | `builder.Services.AddOpenApi()` + `app.MapOpenApi()` → `/openapi/v1.json` | First-party; no Swashbuckle. |
| API reference UI | **`Scalar.AspNetCore`** → `app.MapScalarApiReference()` → `/scalar` (UI reads `/openapi/v1.json`) | Task requirement. |
| Health check | Built-in `builder.Services.AddHealthChecks()` + `app.MapHealthChecks("/health")` | No extra package; plain-text `Healthy` / 200, `Unhealthy` / 503. |
| Exposure of docs | OpenAPI + Scalar mapped **only in `Development`** environment | Don't advertise the API surface in production by default. Health is mapped in **all** environments. |
| Health in OpenAPI | Health endpoint is **excluded** from the OpenAPI document (`.ExcludeFromDescription()`) | "No API endpoints yet": the generated document's `paths` must be empty. |
| Tests | **xUnit** + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`) | Standard in-memory integration testing, no real port. |
| Central package versions | `Directory.Packages.props` (CPM) | One place to bump versions; reviewer can audit dependencies easily. |
| Solution format | `AgenticSdlc.slnx` (or `.sln` if SDK lacks slnx) | `dotnet test` at root picks up the solution. |

Naming: root namespace / project **`AgenticSdlc.Api`** (repo is `agenticsdlc`). Tests: **`AgenticSdlc.Api.Tests`**.

## 3. Repository layout (files to create)

```
.gitignore                       # dotnet template (bin/ obj/ .vs/ *.user TestResults/) + node_modules/
.editorconfig                    # optional, dotnet defaults
global.json
Directory.Build.props            # Nullable=enable, ImplicitUsings=enable, TreatWarningsAsErrors=true, LangVersion latest
Directory.Packages.props         # ManagePackageVersionsCentrally=true + versions below
AgenticSdlc.slnx
package.json                     # npm shim → dotnet (see §5); private: true, no dependencies
README.md                        # how to run, URLs, how to test
src/AgenticSdlc.Api/
  AgenticSdlc.Api.csproj         # Sdk=Microsoft.NET.Sdk.Web, net10.0
  Program.cs
  appsettings.json
  appsettings.Development.json
  Properties/launchSettings.json # http profile, launchUrl "scalar", ASPNETCORE_ENVIRONMENT=Development
tests/AgenticSdlc.Api.Tests/
  AgenticSdlc.Api.Tests.csproj   # references src project; xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing
  *.cs                           # tests (written by Tester step)
```

Packages (pin to latest stable at implementation time):
- `Microsoft.AspNetCore.OpenApi` 10.0.x
- `Scalar.AspNetCore` 2.x
- `Microsoft.AspNetCore.Mvc.Testing` 10.0.x
- `Microsoft.NET.Test.Sdk`, `xunit` (or `xunit.v3`), `xunit.runner.visualstudio`

## 4. `Program.cs` — intended shape (contract, not final code)

```
builder = WebApplication.CreateBuilder(args)
builder.Services.AddOpenApi()
builder.Services.AddHealthChecks()

app = builder.Build()

if app.Environment.IsDevelopment():
    app.MapOpenApi()                    // GET /openapi/v1.json
    app.MapScalarApiReference()         // GET /scalar

app.MapHealthChecks("/health").ExcludeFromDescription()   // GET /health

app.Run()

public partial class Program { }        // exposes Program to WebApplicationFactory
```

Rules:
- No `MapGet/MapPost/...` business endpoints, no controllers, no `MapGroup("/api")` with routes.
- No `UseHttpsRedirection` (keeps `/health` probe-friendly behind TLS-terminating proxies and in tests; revisit when real endpoints arrive).
- No auth yet; nothing to protect. Health returns no details (default writer → status string only), so no information leak.
- `Program` must be accessible to the test project (the `public partial class Program {}` line, or `InternalsVisibleTo`).

## 5. `package.json` npm shim

```json
{
  "name": "agenticsdlc",
  "private": true,
  "scripts": {
    "build": "dotnet build AgenticSdlc.slnx",
    "test": "dotnet test AgenticSdlc.slnx",
    "start": "dotnet run --project src/AgenticSdlc.Api"
  }
}
```

If `dotnet` is installed only under `~/.dotnet`, prefer adding it to PATH in the environment; if that is not possible, the script may use `PATH=$HOME/.dotnet:$PATH dotnet test …`. `npm test` must exit non-zero when any test fails (dotnet test already does).

## 6. Data flow

```
GET /health          → HealthCheckMiddleware → HealthCheckService (no registered checks) → 200 "Healthy" text/plain
GET /openapi/v1.json → OpenAPI document generator (Dev only) → JSON, paths = {}
GET /scalar          → Scalar static UI (Dev only) → HTML, fetches /openapi/v1.json in browser
anything else        → 404
```

## 7. Acceptance criteria (Tester turns these into tests)

Run under `WebApplicationFactory<Program>`; default environment there is `Development` — set it explicitly per test via `WithWebHostBuilder(b => b.UseEnvironment(...))`.

1. **AC1 Health (Development):** `GET /health` → `200`, body `Healthy`, content type `text/plain`.
2. **AC2 Health (Production):** `GET /health` → `200` `Healthy` — health is environment-independent.
3. **AC3 OpenAPI doc (Development):** `GET /openapi/v1.json` → `200`, `application/json`, valid JSON with `openapi` field starting `3.` and an `info` object.
4. **AC4 No API endpoints:** the OpenAPI document's `paths` object is **empty** (in particular contains no `/health`).
5. **AC5 Scalar (Development):** `GET /scalar` → `200` (follow redirects if Scalar redirects `/scalar` → `/scalar/`), `text/html`, body references Scalar / the OpenAPI document URL.
6. **AC6 Docs hidden in Production:** `GET /openapi/v1.json` and `GET /scalar` → `404` when environment is `Production`.
7. **AC7 Unknown route:** `GET /api/anything` → `404` (no catch-all, no business endpoints).
8. **AC8 Method:** `POST /health` → not a success (405 or 404 acceptable; health checks map to GET/HEAD only by default — assert `!IsSuccessStatusCode`).
9. **AC9 Pipeline:** `npm test` at repo root builds the solution and runs all tests; exits 0 when green, non-zero when red. `dotnet build` produces no warnings (`TreatWarningsAsErrors`).

## 8. Edge cases / risks

- **SDK missing** in the agent environment (observed). Install user-locally; never commit it.
- **Scalar route trailing slash:** some Scalar versions redirect `/scalar` → `/scalar/` or serve under `/scalar/v1`. Tests should allow redirects (default `HttpClient` from factory follows them) and the assertion is on the final response.
- **WebApplicationFactory content root:** requires the test project to reference the API project and `Microsoft.AspNetCore.Mvc.Testing` to be present so `.deps.json` is copied; don't change content root.
- **Production env in tests** must not require HTTPS certs — reason for omitting `UseHttpsRedirection`.
- **NuGet network access** is required on first restore; if offline, report it rather than vendoring packages.
- Do not add Swashbuckle, controllers, a database, auth, Docker, or CI — out of scope.

## 9. Handoff to next steps

- **Tester (step 2):** create the solution, both projects' `.csproj` files, `package.json`, and the test classes for AC1–AC8. A compile-able minimal `Program.cs` stub (empty pipeline + `public partial class Program {}`) is allowed so tests compile and fail red, rather than not compiling.
- **Developer (step 3):** implement `Program.cs` per §4, make `npm test` green, keep warnings at zero, write `README.md`.
- **Reviewer (step 4):** check §4 rules (docs only in Development, no business endpoints, health excluded from OpenAPI), dependency list in `Directory.Packages.props` is minimal and pinned.

## 10. Test plan (Tester step 2 — as written)

Runner note: .NET 10 SDK refuses VSTest for xUnit v3, so `global.json` opts into
**Microsoft.Testing.Platform** (`"test": {"runner": "Microsoft.Testing.Platform"}`) and the
npm shim runs `dotnet test --solution AgenticSdlc.slnx`. `Microsoft.NET.Test.Sdk` /
`xunit.runner.visualstudio` are therefore not referenced. SDK installed at `~/.dotnet`
(10.0.401); the shim prepends it to PATH.

| AC | Test |
| --- | --- |
| AC1, AC2 | `HealthCheckTests.Get_health_returns_200_Healthy_as_plain_text` (Development / Production / Staging) |
| — | `HealthCheckTests.Head_health_succeeds`, `Health_response_does_not_leak_details` |
| AC3 | `OpenApiTests.Document_is_served_as_json_in_development`, `Document_is_openapi_3_with_info` |
| AC4 | `OpenApiTests.Document_has_no_paths`, `Document_does_not_describe_health` |
| AC5 | `ScalarTests.Scalar_ui_is_served_as_html_in_development`, `Scalar_ui_references_the_openapi_document` |
| AC6 | `ProductionExposureTests.Docs_are_not_found_outside_development` (Production + Staging) |
| AC7 | `RoutingTests.Unknown_routes_return_404`, `Health_endpoint_is_exact_path` |
| AC8 | `HealthCheckTests.Post_health_is_not_a_success` |

Red state against the stub `Program.cs`: 23 tests, 10 fail (all positive behaviours), 13
pass (negative guards — they must stay green after implementation).
