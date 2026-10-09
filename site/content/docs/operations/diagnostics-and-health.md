# Diagnostics and Health

LiteBus exposes framework-neutral diagnostic probes through the module manifest, and ASP.NET Core and OpenTelemetry packages bridge those probes to health checks and metrics.

> **This page is for** developers who wire LiteBus readiness probes and metrics into their host.
>
> **After reading it** you can register probes, expose them through health endpoints, and set probe limits. Continue with [Operations and Management](README.md).

```csharp
builder.Services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(_ => { });
    liteBus.AddInbox(inbox =>
    {
        inbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
        inbox.AddDiagnosticCheck<PostgreSqlInboxSchemaProbe>("inbox-schema");
    });
});

builder.Services.AddHealthChecks().AddLiteBus();
```

Probes implement `IDiagnosticCheck` with `Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken)`. `AddDiagnosticCheck<TCheck>(string name)` is available on `InboxModuleBuilder` and `OutboxModuleBuilder`. `DiagnosticCheckRunner` in `LiteBus.Runtime.Abstractions` executes manifest probes with bounded parallelism, a timeout per probe, exception isolation, and caller cancellation. Both `AddHealthChecks().AddLiteBus()` and `GET /litebus/health` use the same runner.

## Install The Right Packages

| Package | Role |
| :--- | :--- |
| `LiteBus.Runtime` | `IDiagnosticCheck` and manifest collection |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | `AddLiteBus()` health check through `DiagnosticCheckRunner` |
| `LiteBus.Inbox.Extensions.OpenTelemetry` | Inbox activity source and meter registration |
| `LiteBus.Outbox.Extensions.OpenTelemetry` | Outbox activity source and meter registration |
| `LiteBus.Transport.Extensions.OpenTelemetry` | Shared transport activity source and meter registration |
| `LiteBus.Extensions.AspNetCore` | Management routes and `GET /litebus/health` through `DiagnosticCheckRunner` |

## Register A Probe

Write a probe in application code for schema validation, broker connectivity, or deployment-specific readiness rules. The name you pass to `AddDiagnosticCheck<TCheck>` must equal the probe `Name` property; the health bridge validates the names at runtime.

```csharp
public sealed class OutboxBacklogCheck : IDiagnosticCheck
{
    public string Name => "payments.outbox.backlog";

    public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DiagnosticResult(DiagnosticStatus.Healthy, "Backlog within bound."));
    }
}
```

```csharp
inbox.AddDiagnosticCheck<OutboxBacklogCheck>("payments.outbox.backlog");
```

The PostgreSQL inbox and outbox storage adapters already register their schema probes (`inbox.postgresql.schema` and `outbox.postgresql.schema`) when you call `UsePostgreSqlStorage`. Do not register those library probes again under another name.

## Read Broker Probes

Root transport modules register these broker probes automatically:

| Transport | Probe | Target and permission |
| :--- | :--- | :--- |
| AMQP | `transport.amqp.connectivity` | Opens the configured shared connection |
| Kafka | `transport.kafka.connectivity` | Describes the cluster within `ConnectivityCheckTimeout` |
| AWS SQS | `transport.sqs.connectivity` | Reads `QueueArn` from `ConnectivityCheckQueueUrl` with `sqs:GetQueueAttributes` |
| Azure Service Bus | `transport.azure_service_bus.connectivity` | Peeks `ConnectivityCheckTarget` with entity listen permission |

SQS and Azure Service Bus require an explicit entity target so the probe can use narrow data-plane permissions. When no target is configured, the registered probe reports degraded instead of treating an unopened client as healthy. In-memory transport has no external dependency and does not register a connectivity probe.

## Set Probe Limits

```csharp
builder.Services.AddHealthChecks().AddLiteBus(options =>
{
    options.DiagnosticChecks = new DiagnosticCheckRunOptions
    {
        Timeout = TimeSpan.FromSeconds(3),
        MaxParallelism = 4
    };
});
```

| Option | Location | Default |
| :--- | :--- | :--- |
| `FailHealthWhenNoProbes` | `LiteBusManagementOptions` | `true` |
| `FailHealthWhenNoProbes` | `LiteBusHealthCheckOptions` | `true` |
| `DiagnosticChecks.Timeout` | Management and health-check options | 5 seconds per probe |
| `DiagnosticChecks.MaxParallelism` | Management and health-check options | 4 probes |
| Meter names | Public constants on axis telemetry types | Stable consumer contract |

When the manifest has zero probes and `FailHealthWhenNoProbes` is true, the endpoint returns degraded or unhealthy. Set it to false in local samples so demos work without probes, and keep the default true in production with at least one probe registered. The Generic Host health registration carries `litebus` and `ready` tags so you can filter a readiness endpoint with the standard health-check predicate. See [Architecture](../architecture/README.md) for telemetry instrument names. Renames are breaking changes.

## Know What Health Covers

| Guaranteed | Not guaranteed |
| :--- | :--- |
| Manifest lists all registered probes at build time | Automatic schema migration |
| Health aggregates probe results when checks are registered | Liveness without probes unless you disable `FailHealthWhenNoProbes` |
| One hanging or failing probe cannot prevent sibling outcomes | A connectivity claim when SQS or Azure has no configured entity target |

The Generic Host bridge supervises LiteBus background loops separately from readiness probes. Unexpected loop faults request application shutdown and are rethrown to the host. The ingress background service treats a normal consumer exit as recoverable and restarts it after the configured retry interval.

## Fix Common Symptoms

| Symptom | Action |
| :--- | :--- |
| Health always unhealthy | Register at least one probe or set `FailHealthWhenNoProbes` to false |
| Schema probe fails | Run `EnsureAsync` or fix drift; see [PostgreSQL Schema Management](../integrations/postgresql-schema-management.md) |
| Missing metrics | Add the axis OpenTelemetry package and verify your exporter pipeline |

## Details

| Scenario | Location |
| :--- | :--- |
| Probe manifest contents | `LiteBus.Runtime.UnitTests` composition tests |
| Health check integration | `LiteBus.Extensions.IntegrationTests` |
| `FailHealthWhenNoProbes` | `LiteBus.Extensions.IntegrationTests` management tests |
| Broker readiness | `LiteBus.Transport.UnitTests` and durable broker end-to-end suites |

## Next

- [Hosted Services](../architecture/hosted-services.md)
- [Operations and Management](README.md)
