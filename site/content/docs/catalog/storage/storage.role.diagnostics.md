# Diagnostics Store Role

- **ID**: `storage.role.diagnostics`
- **Name**: Diagnostics store role
- **Maturity**: GA
- **Summary**: Status counts and schema metadata for operators, health probes, and queue depth metrics.

## What It Does

`IInboxDiagnosticsStore` and `IOutboxDiagnosticsStore` expose `GetStatusCountsAsync` and `GetSchemaInfoAsync`. Counts feed OpenTelemetry observable gauges (`litebus.inbox.queue.depth`, `litebus.outbox.queue.depth`) tagged by status. Schema info reports table schema version, store technology, and configured object names for management endpoints and operator tooling.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `IInboxDiagnosticsStore` | Inbox status counts and schema metadata |
| `IOutboxDiagnosticsStore` | Outbox status counts and schema metadata |
| `InboxStatusCounts` / `OutboxStatusCounts` | Per-status row totals |
| Schema info DTOs | Version, technology, object names |

### Invocation

| Method | Typical caller |
| --- | --- |
| `GetStatusCountsAsync` | OpenTelemetry observable gauge callbacks, managers |
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
- Schema info for PostgreSQL includes expected version 1 for greenfield v6.
- InMemory stores report synthetic schema info suitable for tests.

## Non-Goals

- Does not explain per-message failure reasons (query role + management APIs).
- Does not replace PostgreSQL `ValidateAsync` drift detection.
- Does not emit broker connectivity diagnostics.

## Observability

| Meter | Instrument | Type | When observed | Tags | Storage coupling |
| --- | --- | --- | --- | --- | --- |
| `LiteBus.Inbox` | `litebus.inbox.queue.depth` | Observable gauge | Each OpenTelemetry scrape | `litebus.inbox.status` (pending, processing, failed, completed, dead_lettered) | `GetStatusCountsAsync` on inbox diagnostics store |
| `LiteBus.Outbox` | `litebus.outbox.queue.depth` | Observable gauge | Each OpenTelemetry scrape | `litebus.outbox.status` (pending, publishing, failed, published, dead_lettered) | `GetStatusCountsAsync` on outbox diagnostics store |

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

#### `InMemoryInboxGetAllFilterTests.GetAll_WithStatusAndContractName_ShouldFilterResults`

- **Use case**: Filter InMemory inbox diagnostics via GetAll
- **Test kind**: Unit
- **Description**: InMemory test helper filters by status and contract
- **Behavior**: `GetAll` with status and contract name predicates
- **Expected outcome**: Filtered subset returned
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests`; test-only surface

#### `InMemoryOutboxGetAllFilterTests.GetAll_WithStatusAndContractName_ShouldFilterResults`

- **Use case**: Filter InMemory outbox diagnostics via GetAll
- **Test kind**: Unit
- **Description**: InMemory test helper filters by status and contract
- **Behavior**: `GetAll` with predicates
- **Expected outcome**: Filtered subset returned
- **Remarks**: `LiteBus.Outbox.Storage.InMemory.UnitTests`; test-only surface

### Untested

- Dedicated `GetSchemaInfoAsync` contract tests on each adapter (schema info exercised indirectly via management and PostgreSQL diagnostic checks).
- OpenTelemetry queue depth gauge registration and scrape values (hosting axis OpenTelemetry packages).

### Out-of-Scope

- Per-message failure reason drill-down (query role and management APIs).
- PostgreSQL `ValidateAsync` drift detection (schema-management capability).
- Broker connectivity diagnostics.
