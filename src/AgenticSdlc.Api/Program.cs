using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// API docs are advertised in Development only (spec 2026-10-05 §4).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();               // GET /openapi/v1.json
    app.MapScalarApiReference();    // GET /scalar
}

// Liveness probe: infrastructure, not part of the API description.
// MapHealthChecks answers every verb by default; restrict it to GET/HEAD.
app.MapHealthChecks("/health")
    .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]))
    .ExcludeFromDescription();

app.Run();

public partial class Program { }
