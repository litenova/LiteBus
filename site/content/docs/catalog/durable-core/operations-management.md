# Operations and Management

- **ID**: `durable-core.operations-management`
- **Name**: Operations and Management
- **Maturity**: GA
- **Summary**: Provides typed inbox and outbox management, retention, processor control, diagnostics, and optional ASP.NET Core routes.

## What It Does

`IInboxManager` and `IOutboxManager` expose operator operations without exposing storage adapter types. Each manager delegates to the operations and retention roles implemented by the selected store.

Processor controls are separate contracts. `IInboxProcessorControl` and `IOutboxProcessorControl` pause leasing, resume work, report state, and run a bounded drain pass. Hosts can use these contracts directly or expose them through `LiteBus.Extensions.AspNetCore`.

## Manager API

| Operation | Inbox | Outbox | Behavior |
| --- | --- | --- | --- |
| Query | `IInboxManager.QueryAsync` | `IOutboxManager.QueryAsync` | Applies typed filters and keyset pagination |
| Get by identifier | `GetMessageAsync` | `GetMessageAsync` | Returns one envelope or `null` |
| Requeue selected | `RequeueAsync` | `RequeueAsync` | Returns requested and requeued counts |
| Requeue dead letters | `RequeueDeadLettersAsync` | `RequeueDeadLettersAsync` | Reads and requeues in pages of 200 |
| Purge | `PurgeAsync` | `PurgeAsync` | Requires confirmation for an unrestricted filter |
| Status counts | `GetStatusCountsAsync` | `GetStatusCountsAsync` | Groups current rows by durable status |
| Schema information | `GetSchemaInfoAsync` | `GetSchemaInfoAsync` | Returns logical or physical schema metadata |
| Retention status | `GetRetentionStatusAsync` | `GetRetentionStatusAsync` | Returns the latest run outcome |
| Run retention | `RunRetentionPurgeAsync` | `RunRetentionPurgeAsync` | Deletes terminal rows older than the configured retention period |

Query and purge APIs accept `InboxMessageFilter` or `OutboxMessageFilter`. Filters can select identifiers, statuses, contract names, trace fields, tenants, and creation-time bounds. Page requests use opaque keyset cursors returned by the previous page.

## Processor Control

| Contract | Methods | State Values |
| --- | --- | --- |
| `IInboxProcessorControl` | `PauseAsync`, `ResumeAsync`, `DrainAsync` | Running, paused, draining |
| `IOutboxProcessorControl` | `PauseAsync`, `ResumeAsync`, `DrainAsync` | Running, paused, draining |

Pause waits for the active pass before blocking new leases. Resume opens the processor gate. Drain processes currently available work once and then stops new passes, subject to a caller-supplied timeout.

## Retention

Inbox retention deletes completed rows. Outbox retention deletes published rows. The corresponding cleanup host options select the retention period and background loop interval. A null or non-positive retention period disables deletion while leaving status inspection available.

Each manager records the latest retention run timestamp, deleted-row count, and failure message through its retention coordinator. Manual and hosted retention use the same coordinator state.

## HTTP Adapter

`LiteBus.Extensions.AspNetCore` maps manager and processor-control contracts under a configurable prefix:

```csharp
builder.Services.AddLiteBusManagement(options =>
{
    options.AuthorizationPolicy = "LiteBusOperator";
});

app.AddLiteBusManagementEndpoints();
```

The default prefix is `/litebus`. Inbox and outbox route groups are independent. A missing manager produces HTTP 404 for its entire axis. See [ASP.NET Core Management Endpoints](../hosting/aspnet-management-endpoints.md) for the route table and authorization behavior.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | Inbox manager, filters, pages, receipts, processor control |
| `LiteBus.Inbox` | Default inbox manager and retention coordinator |
| `LiteBus.Outbox.Abstractions` | Outbox manager, filters, pages, receipts, processor control |
| `LiteBus.Outbox` | Default outbox manager and retention coordinator |
| `LiteBus.Extensions.AspNetCore` | Optional HTTP binding |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | ASP.NET Core health-check bridge for manifest diagnostics |

## Requires

- One inbox or outbox storage adapter that implements the matching operations roles.
- Processor registration for pause, resume, state, or drain operations.
- Cleanup host registration only when periodic retention is required.

## Safety Rules

- Unrestricted purge requires explicit confirmation.
- Selected requeue reports missing or ineligible identifiers instead of treating every request as successful.
- Dead-letter replay uses bounded pages rather than loading the full dead-letter set.
- Processor drain always has a timeout.
- Inbox and outbox controls remain separate so a host can expose only the axis it runs.

## Observability

| Signal | Source | Use |
| --- | --- | --- |
| `litebus.inbox.queue.depth` | `LiteBus.Inbox` meter | Rows grouped by inbox status |
| `litebus.outbox.queue.depth` | `LiteBus.Outbox` meter | Rows grouped by outbox status |
| `litebus.inbox.processor.state` | `LiteBus.Inbox` meter | Running, paused, or draining state |
| `litebus.outbox.processor.state` | `LiteBus.Outbox` meter | Running, paused, or draining state |
| `RetentionRunStatus` | Manager API | Last retention time, deletion count, and error |
| `StoreSchemaInfo` | Manager API | Logical or database schema version |
| `IDiagnosticCheck` | Host manifest | Storage, transport, or application readiness |

Management endpoints do not define a separate meter. ASP.NET Core request telemetry supplies HTTP status and latency at the host boundary.

## Invariants

- Manager contracts do not expose EF Core, Npgsql, or broker SDK types.
- Store queries use deterministic keyset ordering.
- Retention targets terminal rows only.
- Processor control changes leasing behavior without changing stored message state directly.

## Non-Goals

- Cross-region replication control.
- Built-in role or identity management.
- Direct broker consumer administration.
- A unified manager that hides inbox and outbox semantics.

## Test Coverage

### Covered Use Cases

| Test | Proves |
| --- | --- |
| `InboxManagerOperationsTests` | Query, missing get, status counts, schema, paged dead-letter replay, purge safety, retention success, and retention failure |
| `OutboxManagerOperationsTests` | The equivalent outbox manager contract |
| `InboxStoreContractTests.QueryAsync_ShouldFilterAndPageByCreatedAt` | Store adapters satisfy filter and keyset pagination behavior |
| `InboxStoreContractTests.PurgeAsync_ShouldDeleteMatchingRows` | Store purge honors typed predicates |
| `InboxProcessorControlTests` | Pause, resume, and drain behavior on the concrete inbox processor control |
| `OutboxProcessorControlTests` | Pause, resume, and drain behavior on the concrete outbox processor control |
| `PostgreSqlInboxHostingIntegrationTests.CleanupBackgroundService_ShouldPurgeCompletedRowsPastRetention` | Hosted PostgreSQL inbox retention deletes eligible rows |
| `ManagementEndpointAuthorizationIntegrationTests` | ASP.NET Core authentication and named-policy behavior |
| `ManagementEndpointPostgreSqlIntegrationTests` | HTTP query, purge, and diagnostic routes use PostgreSQL-backed managers |
| `ManagementEndpointOperationsTests` | Inbox and outbox route binding, processor controls, missing axes, and custom prefixes |
| `LiteBusHealthCheckIntegrationTests` | Manifest diagnostic checks map to ASP.NET Core health status |

### Untested Use Cases

- Long-running operator requests canceled by a disconnected HTTP client.
- Management endpoint HTTP 500 response content after an unexpected adapter exception.

### Out-of-Scope Use Cases

- Cross-region message replication management.
- Multi-cluster processor federation.
- Built-in identity provider configuration.

## Deep Docs

- [Operations and Management](../../operations/README.md)
- [ASP.NET Core Management Endpoints](../hosting/aspnet-management-endpoints.md)
- [Diagnostics and Health](../../operations/diagnostics-and-health.md)
- [Hosted Services](../../architecture/hosted-services.md)
