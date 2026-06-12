using LiteBus.Extensions.AspNetCore;
using LiteBus.Extensions.Diagnostics.HealthChecks;
using LiteBus.Inbox.Extensions.OpenTelemetry;
using LiteBus.Outbox.Extensions.OpenTelemetry;
using LiteBus.Samples.V6;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApiDocument();
builder.Services.AddLiteBusV6(builder.Configuration);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddLiteBusInboxInstrumentation())
    .WithMetrics(metrics => metrics.AddLiteBusInboxMetrics().AddLiteBusOutboxMetrics());

// Local demo: allow anonymous management and skip probe enforcement.
// Production hosts should follow ProductionHostTemplate.ConfigureProductionManagement.
builder.Services.AddLiteBusManagement(options =>
{
    options.FailHealthWhenNoProbes = false;
    options.AllowAnonymousManagement = builder.Environment.IsDevelopment();
});

builder.Services.AddHealthChecks().AddLiteBus(options => options.FailHealthWhenNoProbes = false);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUi();
    app.UseOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");
app.AddLiteBusManagementEndpoints();

// Graceful shutdown: drain processors before the host stops leasing in-flight work.
// POST /litebus/inbox/processor/drain and POST /litebus/outbox/processor/drain (or call
// IInboxProcessorControl.DrainAsync / IOutboxProcessorControl.DrainAsync from IHostApplicationLifetime).
app.Lifetime.ApplicationStopping.Register(() =>
{
    // Application-owned drain hook: invoke management endpoints or processor controls here in production.
});

app.Run();