# Diagnostics Store Role

- **ID**: `storage.role.diagnostics`
- **Name**: Diagnostics store role
- **Maturity**: GA
- **Summary**: Reports status counts, per-contract failure summaries, and schema metadata for operators, health probes, and queue depth metrics.

## What It Does

`IInboxDiagnosticsStore` and `IOutboxDiagnosticsStore` expose `GetStatusCountsAsync` and `GetSchemaInfoAsync`. Counts feed OpenTelemetry observable gauges (`litebus.inbox.queue.depth`, `litebus.outbox.queue.depth`) tagged by status.

`IOutboxDiagnosticsStore` also exposes `GetFailureSummaryAsync`, which returns one row per contract name and version that has `Failed` or `DeadLettered` messages, with per-status counts plus the latest error text and attempt time. Health probes use it to separate messages that fail on every attempt from a backlog that is only slow. Schema information reports the table schema version, the store component, and the configured object names for management endpoints and operator tooling.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `IInboxDiagnosticsStore` | Inbox status counts and schema metadata |
| `IOutboxDiagnosticsStore` | Outbox status counts and schema metadata |
| `InboxStatusCounts` / `OutboxStatusCounts` | Per-status row totals |
| `OutboxFailureSummary` | Per-contract failing counts with the latest error and attempt time |
| Schema info DTOs | Component, expected and recorded versions, and object names |

### Invocation

| Method | Typical caller |
| --- | --- |
| `GetStatusCountsAsync` | OpenTelemetry observable gauge callbacks, managers |
| `GetFailureSummaryAsync` | Health probes, `IOutboxManager` |
| `GetSchemaInfoAsync` | `IInboxManager` / `IOutboxManager`, management HTTP endpoints |

### Registration

- The operations composite sits on the singleton store. OpenTelemetry packages register gauge observers that call the diagnostics store at scrape time.

### Configuration

| Option surface | Purpose |
| --- | --- |
| Store options | Schema and table names, reflected in `GetSchemaInfoAsync` |
| OpenTelemetry host registration | Host registration of `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()`, required for gauges |

### Extension Points

- Custom stores must return live counts (not indefinitely cached) and schema information matching the configured object names.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | `IInboxDiagnosticsStore` |
| `LiteBus.Outbox.Abstractions` | `IOutboxDiagnosticsStore` |
| `LiteBus.Inbox.Extensions.OpenTelemetry` | Queue depth gauges |
| `LiteBus.Outbox.Extensions.OpenTelemetry` | Queue depth gauges |

## Requires

- A registered operations composite store
- For PostgreSQL schema probes, the `storage.postgresql.schema-management` diagnostic checks, which are separate from counts

## Invariants

- Status counts reflect the store truth at query time. They are not cached indefinitely.
- For PostgreSQL, schema information reports the recorded version from `litebus_schema_versions` against the expected `CurrentSchemaVersion` (inbox 1, outbox 2).
- EF Core and InMemory stores report logical store version 1 with the configured object names.

## Non-Goals

- It does not replace per-message failure drill-down (query role and management APIs).
- It does not replace PostgreSQL `ValidateAsync` drift detection.
- It does not emit broker connectivity diagnostics.

## Observability

| Meter | Instrument | Type | When observed | Tags | Storage coupling |
| --- | --- | --- | --- | --- | --- |
| `LiteBus.Inbox` | `litebus.inbox.queue.depth` | Observable gauge | Each OpenTelemetry scrape | `litebus.inbox.status` (`Pending`, `Processing`, `Completed`, `Failed`, `DeadLettered`) | `GetStatusCountsAsync` on inbox diagnostics store |
| `LiteBus.Outbox` | `litebus.outbox.queue.depth` | Observable gauge | Each OpenTelemetry scrape | `litebus.outbox.status` (`Pending`, `Publishing`, `Published`, `Failed`, `DeadLettered`) | `GetStatusCountsAsync` on outbox diagnostics store |

| Probe / API | Source | Purpose |
| --- | --- | --- |
| `inbox.postgresql.schema` / `outbox.postgresql.schema` | `IDiagnosticCheck` | Schema drift detection (PostgreSQL adapters) |
| Management `GET` schema info | `GetSchemaInfoAsync` | Human-readable schema version and object names |

Instrument names are public constants on `LiteBusInboxTelemetry` and `LiteBusOutboxTelemetry`. Renames are breaking changes.

## Further Reading

- [Architecture: OpenTelemetry metric catalog](../../architecture/README.md#opentelemetry-metric-catalog)
- [Hosted services: Diagnostic probes](../../architecture/hosted-services.md)

## Test Coverage

### Covered

#### `InboxStoreContractTests.GetStatusCountsAsync_ShouldGroupByStatus`

- **Use case**: Group inbox status counts by status
- **Test kind**: Contract
- **Description**: Stores rows in multiple statuses, then queries counts
- **Behavior**: Runs `GetStatusCountsAsync`
- **Expected outcome**: Counts match rows per status bucket
- **Remarks**: `LiteBus.Storage.Testing`; inherited by all inbox backends

#### `OutboxStoreContractTests.GetStatusCountsAsync_ShouldGroupByStatus`

- **Use case**: Group outbox status counts by status
- **Test kind**: Contract
- **Description**: Stores rows in multiple statuses, then queries counts
- **Behavior**: Runs `GetStatusCountsAsync`
- **Expected outcome**: Counts match rows per status bucket
- **Remarks**: Inherited by PostgreSQL, EF, and InMemory outbox suites

#### `OutboxStoreContractTests.GetFailureSummaryAsync_WithFailedAndDeadLetteredContracts_ShouldGroupCountsByContract`

- **Use case**: Group failing outbox messages by contract
- **Test kind**: Contract
- **Description**: Seeds failed and dead-lettered rows for two contracts, then queries the failure summary
- **Behavior**: Runs `GetFailureSummaryAsync`
- **Expected outcome**: Each failing contract gets one row with its `Failed` and `DeadLettered` counts
- **Remarks**: Inherited by PostgreSQL, EF, and InMemory outbox suites

#### `OutboxStoreContractTests.GetFailureSummaryAsync_WithTwoFailuresForOneContract_ShouldReportMostRecentError`

- **Use case**: Report the latest error per failing contract
- **Test kind**: Contract
- **Description**: Fails one contract's messages twice with different errors, then queries the failure summary
- **Behavior**: Runs `GetFailureSummaryAsync`
- **Expected outcome**: `LastError` is the error of the most recently attempted failing message
- **Remarks**: Inherited by PostgreSQL, EF, and InMemory outbox suites

#### `OutboxStoreContractTests.GetFailureSummaryAsync_WhenNothingIsFailing_ShouldReturnEmpty`

- **Use case**: Report nothing when no message is failing
- **Test kind**: Contract
- **Description**: Queries the failure summary on a store with no failed or dead-lettered rows
- **Behavior**: Runs `GetFailureSummaryAsync`
- **Expected outcome**: The summary is an empty list
- **Remarks**: Inherited by PostgreSQL, EF, and InMemory outbox suites

#### `InMemoryInboxGetAllFilterTests.GetAll_WithStatusAndContractName_ShouldFilterResults`

- **Use case**: Filter InMemory inbox diagnostics via GetAll
- **Test kind**: Unit
- **Description**: InMemory test helper filters by status and contract
- **Behavior**: Runs `GetAll` with status and contract name predicates
- **Expected outcome**: The filtered subset is returned
- **Remarks**: `LiteBus.Storage.UnitTests`; test-only surface

#### `InMemoryOutboxGetAllFilterTests.GetAll_WithStatusAndContractName_ShouldFilterResults`

- **Use case**: Filter InMemory outbox diagnostics via GetAll
- **Test kind**: Unit
- **Description**: InMemory test helper filters by status and contract
- **Behavior**: Runs `GetAll` with predicates
- **Expected outcome**: The filtered subset is returned
- **Remarks**: `LiteBus.Storage.UnitTests`; test-only surface

### Untested

- Dedicated `GetSchemaInfoAsync` contract tests on each adapter. Schema information is exercised indirectly through management and PostgreSQL diagnostic checks.
- OpenTelemetry queue depth gauge registration and scrape values in the hosting axis OpenTelemetry packages.

### Out-of-Scope

- Per-message failure history beyond the latest error per contract, which belongs to the query role and management APIs.
- PostgreSQL `ValidateAsync` drift detection, which belongs to the schema-management capability.
- Broker connectivity diagnostics.
