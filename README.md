# AgenticSdlc

Minimal ASP.NET Core (.NET 10) API skeleton: OpenAPI + Scalar API reference and a health check. No API endpoints yet.

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

## Build & test

```bash
npm run build        # dotnet build AgenticSdlc.slnx
npm test             # dotnet test --solution AgenticSdlc.slnx (xUnit v3 on Microsoft.Testing.Platform)
```

The npm scripts prepend `~/.dotnet` to `PATH`, so a user-local SDK install works too.
