# Operations Composite Store

- **ID**: `storage.composite.operations-store`
- **Name**: Operations composite store
- **Maturity**: GA
- **Summary**: Dead-letter, retention, diagnostics, query, and purge roles for managers and operator tooling.

## What It Does

`IInboxOperationsStore` combines `IInboxDeadLetterStore`, `IInboxRetentionStore`, `IInboxDiagnosticsStore`, `IInboxMessageQuery`, and `IInboxPurgeStore`. `IOutboxOperationsStore` mirrors the same operational slices for outbox. `InboxManager` and `OutboxManager` depend on the operations composite for HTTP management, cleanup services, and schema info endpoints.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `IInboxOperationsStore` | Inbox dead-letter, retention, diagnostics, query, purge composite |
| `IOutboxOperationsStore` | Outbox operations composite |
| Fine-grained operations interfaces | Narrow dependencies when needed |
| `IInboxManager` / `IOutboxManager` | Application and ASP.NET operator facades |

### Invocation

| Method family | Typical caller |
| --- | --- |
| `RequeueAsync` | Managers, management HTTP |
| `DeleteCompletedOlderThanAsync` / `DeletePublishedOlderThanAsync` | Cleanup background services |
| `GetStatusCountsAsync` / `GetSchemaInfoAsync` | OpenTelemetry gauges, management HTTP |
| `QueryAsync` / `PurgeAsync` | Operator tooling, management HTTP |

### Registration

- Same singleton as append and processing roles; managers resolve `IInboxOperationsStore` / `IOutboxOperationsStore` from DI.

### Configuration

| Option surface | Purpose |
| --- | --- |
| Retention options on processor/host | Cleanup schedule for retention role |
| Application auth on management HTTP | Production purge and query protection |

### Extension Points

- Custom stores implement all operations roles on the registered singleton; composite is a typing aggregate.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | `IInboxOperationsStore` |
| `LiteBus.Outbox.Abstractions` | `IOutboxOperationsStore` |
| `LiteBus.Inbox`, `LiteBus.Outbox` | Manager implementations |

## Requires

- Storage adapter registered on parent module
- For HTTP operations: `LiteBus.Extensions.AspNetCore` and application auth

## Invariants

- Same singleton instance as append and processing roles.
- Operations APIs do not lease rows for dispatch.
- Fine-grained interfaces remain available for narrow dependencies.

## Non-Goals

- Does not start or pause processors (processor control APIs on managers are separate from store roles).
- Does not validate broker connectivity.

## Observability

| Meter | Instrument | Type | When observed | Tags | Operations role |
| --- | --- | --- | --- | --- | --- |
| `LiteBus.Inbox` | `litebus.inbox.queue.depth` | Observable gauge | OpenTelemetry scrape | `litebus.inbox.status` | Diagnostics `GetStatusCountsAsync` |
| `LiteBus.Outbox` | `litebus.outbox.queue.depth` | Observable gauge | OpenTelemetry scrape | `litebus.outbox.status` | Diagnostics `GetStatusCountsAsync` |
| `LiteBus.Inbox` | `litebus.inbox.cleanup.errors` | Counter | Retention service failure | none | Retention role |
| `LiteBus.Outbox` | `litebus.outbox.cleanup.errors` | Counter | Retention service failure | none | Retention role |

Management HTTP exposes human-readable schema info; purge returns affected counts without a dedicated meter.

## Deep Docs

- [Operations management](../durable-core/operations-management.md)
- [Custom stores and dispatchers](../../extending/custom-stores-and-dispatchers.md)

## Test Coverage

### Covered

#### `InboxStoreContractTests.RequeueDeadLetterAsync_ShouldReturnEnvelopeToPending`

- **Use case**: Inbox operations requeue dead-letter via contract suite
- **Test kind**: Contract
- **Description**: Dead-letter store role on operations composite
- **Behavior**: `RequeueAsync` on dead-lettered row
- **Expected outcome**: Row returns to pending
- **Remarks**: Dead-letter slice of operations composite

#### `InboxRetentionStoreContractTests.DeleteCompletedOlderThanAsync_ShouldRemoveEligibleRows`

- **Use case**: Inbox operations retention via contract suite
- **Test kind**: Contract
- **Description**: Retention store role on operations composite
- **Behavior**: `DeleteCompletedOlderThanAsync`
- **Expected outcome**: Eligible terminal rows removed
- **Remarks**: Retention slice

#### `InboxStoreContractTests.GetStatusCountsAsync_ShouldGroupByStatus`

- **Use case**: Inbox operations diagnostics counts via contract suite
- **Test kind**: Contract
- **Description**: Diagnostics store role on operations composite
- **Behavior**: `GetStatusCountsAsync`
- **Expected outcome**: Per-status counts returned
- **Remarks**: Diagnostics slice

#### `InboxStoreContractTests.QueryAsync_ShouldFilterAndPageByCreatedAt`

- **Use case**: Inbox operations query via contract suite
- **Test kind**: Contract
- **Description**: Query role on operations composite
- **Behavior**: `QueryAsync` with filters
- **Expected outcome**: Paged matching envelopes
- **Remarks**: Query slice

#### `InboxStoreContractTests.PurgeAsync_ShouldDeleteMatchingRows`

- **Use case**: Inbox operations purge via contract suite
- **Test kind**: Contract
- **Description**: Purge role on operations composite
- **Behavior**: `PurgeAsync` with predicate
- **Expected outcome**: Matching rows deleted
- **Remarks**: Purge slice

#### `OutboxStoreContractTests.RequeueDeadLetterAsync_ShouldReturnMessageToPending`

- **Use case**: Outbox operations requeue dead-letter via contract suite
- **Test kind**: Contract
- **Description**: Outbox dead-letter role
- **Behavior**: `RequeueAsync`
- **Expected outcome**: Row pending again
- **Remarks**: Outbox operations composite

#### `OutboxRetentionStoreContractTests.DeletePublishedOlderThanAsync_ShouldRemoveEligibleRows`

- **Use case**: Outbox operations retention via contract suite
- **Test kind**: Contract
- **Description**: Outbox retention role
- **Behavior**: `DeletePublishedOlderThanAsync`
- **Expected outcome**: Eligible rows removed
- **Remarks**: Retention slice

#### `OutboxStoreContractTests.GetStatusCountsAsync_ShouldGroupByStatus`

- **Use case**: Outbox operations diagnostics counts via contract suite
- **Test kind**: Contract
- **Description**: Outbox diagnostics role
- **Behavior**: `GetStatusCountsAsync`
- **Expected outcome**: Per-status counts
- **Remarks**: Diagnostics slice

#### `OutboxStoreContractTests.QueryAsync_ShouldFilterAndPageByCreatedAt`

- **Use case**: Outbox operations query via contract suite
- **Test kind**: Contract
- **Description**: Outbox query role
- **Behavior**: `QueryAsync`
- **Expected outcome**: Paged results
- **Remarks**: Query slice

#### `OutboxStoreContractTests.PurgeAsync_ShouldDeleteMatchingRows`

- **Use case**: Outbox operations purge via contract suite
- **Test kind**: Contract
- **Description**: Outbox purge role
- **Behavior**: `PurgeAsync`
- **Expected outcome**: Matching rows deleted
- **Remarks**: Purge slice

#### `InboxRetentionStoreContractTests` in `LiteBus.Inbox.Storage.InMemory.UnitTests`

- **Use case**: InMemory inbox retention contract on operations store
- **Test kind**: Contract
- **Description**: Full retention contract on InMemory inbox store
- **Behavior**: Inherited retention tests
- **Expected outcome**: Same outcomes as abstract contract
- **Remarks**: InMemory operations composite

#### `OutboxRetentionStoreContractTests` in `LiteBus.Outbox.Storage.InMemory.UnitTests`

- **Use case**: InMemory outbox retention contract on operations store
- **Test kind**: Contract
- **Description**: Full retention contract on InMemory outbox store
- **Behavior**: Inherited retention tests
- **Expected outcome**: Same outcomes as abstract contract
- **Remarks**: InMemory operations composite

### Untested

- `InboxManager` / `OutboxManager` facade unit tests isolated from store roles (manager tests live in inbox/outbox core packages).
- Processor start/stop control via manager APIs.

### Out-of-Scope

- Leasing rows for dispatch (processing composite).
- Broker connectivity validation.
- Splitting operations roles across different store instances.
