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
    builder.AddMessaging(_ => { });
    builder.AddInbox(inbox =>
    {
        inbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
        inbox.AddDiagnosticCheck<PostgreSqlInboxSchemaProbe>("inbox-schema");
    });
});

builder.Services.AddHealthChecks().AddLiteBus();
```

Probes implement `IDiagnosticCheck` with `Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken)`.

`AddDiagnosticCheck<TCheck>(string name)` is available on `InboxModuleBuilder` and `OutboxModuleBuilder`. Root transport modules register these broker probes automatically:

| Transport | Probe | Target and permission |
| --- | --- | --- |
| AMQP | `transport.amqp.connectivity` | Opens the configured shared connection |
| Kafka | `transport.kafka.connectivity` | Describes the cluster within `ConnectivityCheckTimeout` |
| AWS SQS | `transport.sqs.connectivity` | Reads `QueueArn` from `ConnectivityCheckQueueUrl` with `sqs:GetQueueAttributes` |
| Azure Service Bus | `transport.azure_service_bus.connectivity` | Peeks `ConnectivityCheckTarget` with entity listen permission |

SQS and Azure Service Bus require an explicit entity target so the probe can use narrow data-plane permissions. When no target is configured, the registered probe reports degraded instead of treating an unopened client as healthy. In-memory transport has no external dependency and does not register a connectivity probe.

`DiagnosticCheckRunner` in `LiteBus.Runtime.Abstractions` executes manifest probes with bounded parallelism, a timeout per probe, exception isolation, and caller cancellation. Both `AddHealthChecks().AddLiteBus()` and `GET /litebus/health` use the same runner. The ASP.NET Core health registration has `litebus` and `ready` tags so hosts can filter a readiness endpoint using the standard health-check predicate.

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

The provider calls follow their current SDK contracts: [SQS queue attributes](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/SQS/TSQSClient.html), [Azure Service Bus non-settling peek](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.servicebus.servicebusreceiver.peekmessagesasync?view=azure-dotnet), and [Confluent Kafka cluster description request timeout](https://docs.confluent.io/platform/current/clients/confluent-kafka-dotnet/_site/api/Confluent.Kafka.Admin.DescribeClusterOptions.html). ASP.NET Core documents tag-based readiness filtering in its [.NET 10 health-check guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0).

## Options Reference

| Option | Location | Default |
| --- | --- | --- |
| `FailHealthWhenNoProbes` | `LiteBusManagementOptions` | `true` |
| `DiagnosticChecks.Timeout` | Management and health-check options | 5 seconds per probe |
| `DiagnosticChecks.MaxParallelism` | Management and health-check options | 4 probes |
| Meter names | Public constants on axis telemetry types | Stable consumer contract |

See [Architecture](../architecture/README.md) for telemetry instrument names, including `litebus.inbox.processor.persist_failed`, `litebus.outbox.processor.persist_failed`, and `litebus.*.diagnostics.unavailable` when terminal persist or queue depth probes fail. Renames are breaking changes.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| Manifest lists all registered probes at build time | Automatic schema migration |
| Health aggregates probe results when checks registered | Liveness without probes unless you disable `FailHealthWhenNoProbes` |
| One hanging or failing probe cannot prevent sibling outcomes | A connectivity claim when SQS or Azure has no configured entity target |

The Generic Host bridge supervises LiteBus background loops separately from readiness probes. Unexpected loop faults request application shutdown and are rethrown to the host. The ingress background service treats a normal consumer exit as recoverable and restarts it after the configured retry interval. This prevents a dead ingress loop from leaving a healthy process behind without coupling runtime packages to ASP.NET Core health types.

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
| Broker readiness | `LiteBus.Transport.UnitTests` and durable broker end-to-end suites |

## Related Docs

* [Hosted services](../architecture/hosted-services.md)
* [Operations and management](README.md)
