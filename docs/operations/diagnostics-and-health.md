# Diagnostics and Health

LiteBus registers framework-neutral diagnostic probes through the module manifest. ASP.NET Core and OpenTelemetry packages bridge those probes to host health checks and metrics.

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Runtime` | `IDiagnosticCheck`, manifest collection |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | `AddLiteBus()` health check (uses `DiagnosticCheckRunner`) |
| `LiteBus.Inbox.Extensions.OpenTelemetry` | Inbox activity source and meter registration |
| `LiteBus.Outbox.Extensions.OpenTelemetry` | Outbox activity source and meter registration |
| `LiteBus.Transport.Extensions.OpenTelemetry` | Shared transport activity source and meter registration |
| `LiteBus.Extensions.AspNetCore` | Management routes and `/litebus/health` (uses `DiagnosticCheckRunner`) |

## Registration

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddInboxModule(inbox =>
    {
        inbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
        inbox.AddDiagnosticCheck<PostgreSqlInboxSchemaProbe>("inbox-schema");
    });
});

builder.Services.AddHealthChecks().AddLiteBus();
```

Probes implement `IDiagnosticCheck` with `Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken)`.

`AddDiagnosticCheck<TCheck>(string name)` is available on `InboxModuleBuilder` and `OutboxModuleBuilder`. Only AMQP transport ships a broker connectivity probe today (`AmqpConnectivityDiagnosticCheck`).

`DiagnosticCheckRunner` in `LiteBus.Runtime.Abstractions` executes manifest probes once and aggregates status for both `AddHealthChecks().AddLiteBus()` and `GET /litebus/health`.

## Options Reference

| Option | Location | Default |
| --- | --- | --- |
| `FailHealthWhenNoProbes` | `LiteBusManagementOptions` | `true` |
| Meter names | Public constants on axis telemetry types | Stable consumer contract |

See [Architecture](../architecture/README.md) for telemetry instrument names, including `litebus.inbox.processor.persist_failed`, `litebus.outbox.processor.persist_failed`, and `litebus.*.diagnostics.unavailable` when terminal persist or queue depth probes fail. Renames are breaking changes.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| Manifest lists all registered probes at build time | Automatic schema migration |
| Health aggregates probe results when checks registered | Liveness without probes unless you disable `FailHealthWhenNoProbes` |

## Operations

| Symptom | Action |
| --- | --- |
| Health always unhealthy | Register at least one probe or set `FailHealthWhenNoProbes = false` |
| Schema probe fails | Run `EnsureAsync` / fix drift; see [PostgreSQL schema management](../integrations/postgresql-schema-management.md) |
| Missing metrics | Add axis OpenTelemetry package; verify exporter pipeline |

## Tests

| Scenario | Location |
| --- | --- |
| Probe manifest contents | `LiteBus.Runtime.UnitTests`, composition tests |
| Health check integration | `LiteBus.Extensions.IntegrationTests` |
| `FailHealthWhenNoProbes` | `LiteBus.Extensions.IntegrationTests` (AspNetCore management) |

## Related Docs

* [Hosted services](../architecture/hosted-services.md)
* [Operations and management](README.md)
