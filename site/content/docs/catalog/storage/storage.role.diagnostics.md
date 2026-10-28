# Diagnostics Store Role

- **ID**: `storage.role.diagnostics`
- **Name**: Diagnostics store role
- **Maturity**: GA
- **Summary**: Status counts, per-contract failure summaries, and schema metadata for operators, health probes, and queue depth metrics.

## What It Does

`IInboxDiagnosticsStore` and `IOutboxDiagnosticsStore` expose `GetStatusCountsAsync` and `GetSchemaInfoAsync`. Counts feed OpenTelemetry observable gauges (`litebus.inbox.queue.depth`, `litebus.outbox.queue.depth`) tagged by status. Schema info reports table schema version, store component, and configured object names for management endpoints and operator tooling.
`IOutboxDiagnosticsStore` also exposes `GetFailureSummaryAsync`, which returns one row per contract name and version that has `Failed` or `DeadLettered` messages, with per-status counts plus the latest error text and attempt time, so health probes can separate messages that fail on every attempt from a backlog that is merely slow.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `IInboxDiagnosticsStore` | Inbox status counts and schema metadata |
| `IOutboxDiagnosticsStore` | Outbox status counts and schema metadata |
| `InboxStatusCounts` / `OutboxStatusCounts` | Per-status row totals |
| `OutboxFailureSummary` | Per-contract failing counts with the latest error and attempt time |
| Schema info DTOs | Component, expected and recorded version, object names |

### Invocation

| Method | Typical caller |
| --- | --- |
| `GetStatusCountsAsync` | OpenTelemetry observable gauge callbacks, managers |
| `GetFailureSummaryAsync` | Health probes, `IOutboxManager` |
| `GetSchemaInfoAsync` | `IInboxManager` / `IOutboxManager`, management HTTP endpoints |

### Registration

- Operations composite on singleton store; OpenTelemetry packages register gauge observers that call diagnostics store at scrape time.

### Configuration

| Option surface | Purpose |
| --- | --- |
| Store options | Schema and table names reflected in `GetSchemaInfoAsync` |
| OpenTelemetry host registration | `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()` required for gauges |

### Extension Points

- Custom stores must return live counts (not indefinitely cached) and schema info matching configured object names.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | `IInboxDiagnosticsStore` |
| `LiteBus.Outbox.Abstractions` | `IOutboxDiagnosticsStore` |
| `LiteBus.Inbox.Extensions.OpenTelemetry` | Queue depth gauges |
| `LiteBus.Outbox.Extensions.OpenTelemetry` | Queue depth gauges |

## Requires

- Operations composite store registered
- For PostgreSQL schema probes: `storage.postgresql.schema-management` diagnostic checks (separate from counts)

## Invariants

- Status counts reflect store truth at query time (not cached indefinitely).
- Schema info for PostgreSQL reports the recorded version from `litebus_schema_versions` against the expected `CurrentSchemaVersion` (inbox 1, outbox 2).
- EF Core and InMemory stores report logical store version 1 with the configured object names.

## Non-Goals

- Does not replace per-message failure drill-down (query role + management APIs).
- Does not replace PostgreSQL `ValidateAsync` drift detection.
- Does not emit broker connectivity diagnostics.

## Observability

| Meter | Instrument | Type | When observed | Tags | Storage coupling |
| --- | --- | --- | --- | --- | --- |
| `LiteBus.Inbox` | `litebus.inbox.queue.depth` | Observable gauge | Each OpenTelemetry scrape | `litebus.inbox.status` (`Pending`, `Processing`, `Completed`, `Failed`, `DeadLettered`) | `GetStatusCountsAsync` on inbox diagnostics store |
| `LiteBus.Outbox` | `litebus.outbox.queue.depth` | Observable gauge | Each OpenTelemetry scrape | `litebus.outbox.status` (`Pending`, `Publishing`, `Published`, `Failed`, `DeadLettered`) | `GetStatusCountsAsync` on outbox diagnostics store |

| Probe / API | Source | Purpose |
| --- | --- | --- |
| `inbox.postgresql.schema` / `outbox.postgresql.schema` | `IDiagnosticCheck` | Schema drift detection (PostgreSQL adapters) |
| Management `GET` schema info | `GetSchemaInfoAsync` | Human-readable schema version and object names |

Instrument names are public constants on `LiteBusInboxTelemetry` and `LiteBusOutboxTelemetry`; renames are breaking changes.

## Deep Docs

- [Architecture: OpenTelemetry metric catalog](../../architecture/README.md#opentelemetry-metric-catalog)
- [Hosted services: Diagnostic probes](../../architecture/hosted-services.md)

## Test Coverage

### Covered

#### `InboxStoreContractTests.GetStatusCountsAsync_ShouldGroupByStatus`

- **Use case**: Group inbox status counts by status
- **Test kind**: Contract
- **Description**: Store rows in multiple statuses then query counts
- **Behavior**: `GetStatusCountsAsync`
- **Expected outcome**: Counts match rows per status bucket
- **Remarks**: `LiteBus.Storage.Testing`; inherited by all inbox backends

#### `OutboxStoreContractTests.GetStatusCountsAsync_ShouldGroupByStatus`

- **Use case**: Group outbox status counts by status
- **Test kind**: Contract
- **Description**: Store rows in multiple statuses then query counts
- **Behavior**: `GetStatusCountsAsync`
- **Expected outcome**: Counts match rows per status bucket
- **Remarks**: Inherited by PostgreSQL, EF, and InMemory outbox suites

#### `OutboxStoreContractTests.GetFailureSummaryAsync_WithFailedAndDeadLetteredContracts_ShouldGroupCountsByContract`

- **Use case**: Group failing outbox messages by contract
- **Test kind**: Contract
- **Description**: Seed failed and dead-lettered rows for two contracts, then query the failure summary
- **Behavior**: `GetFailureSummaryAsync`
- **Expected outcome**: One row per failing contract with its `Failed` and `DeadLettered` counts
- **Remarks**: Inherited by PostgreSQL, EF, and InMemory outbox suites

#### `OutboxStoreContractTests.GetFailureSummaryAsync_WithTwoFailuresForOneContract_ShouldReportMostRecentError`

- **Use case**: Report the latest error per failing contract
- **Test kind**: Contract
- **Description**: Fail one contract's messages twice with different errors, then query the failure summary
- **Behavior**: `GetFailureSummaryAsync`
- **Expected outcome**: `LastError` is the error of the most recently attempted failing message
- **Remarks**: Inherited by PostgreSQL, EF, and InMemory outbox suites

#### `OutboxStoreContractTests.GetFailureSummaryAsync_WhenNothingIsFailing_ShouldReturnEmpty`

- **Use case**: Report nothing when no message is failing
- **Test kind**: Contract
- **Description**: Query the failure summary on a store with no failed or dead-lettered rows
- **Behavior**: `GetFailureSummaryAsync`
- **Expected outcome**: An empty list
- **Remarks**: Inherited by PostgreSQL, EF, and InMemory outbox suites

#### `InMemoryInboxGetAllFilterTests.GetAll_WithStatusAndContractName_ShouldFilterResults`

- **Use case**: Filter InMemory inbox diagnostics via GetAll
- **Test kind**: Unit
- **Description**: InMemory test helper filters by status and contract
- **Behavior**: `GetAll` with status and contract name predicates
- **Expected outcome**: Filtered subset returned
- **Remarks**: `LiteBus.Storage.UnitTests`; test-only surface

#### `InMemoryOutboxGetAllFilterTests.GetAll_WithStatusAndContractName_ShouldFilterResults`

- **Use case**: Filter InMemory outbox diagnostics via GetAll
- **Test kind**: Unit
- **Description**: InMemory test helper filters by status and contract
- **Behavior**: `GetAll` with predicates
- **Expected outcome**: Filtered subset returned
- **Remarks**: `LiteBus.Storage.UnitTests`; test-only surface

### Untested

- Dedicated `GetSchemaInfoAsync` contract tests on each adapter (schema info exercised indirectly via management and PostgreSQL diagnostic checks).
- OpenTelemetry queue depth gauge registration and scrape values (hosting axis OpenTelemetry packages).

### Out-of-Scope

- Per-message failure history beyond the latest error per contract (query role and management APIs).
- PostgreSQL `ValidateAsync` drift detection (schema-management capability).
- Broker connectivity diagnostics.
