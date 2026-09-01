# Scheduling and Visibility Metadata

**ID:** `durable-core.scheduling-metadata`
**Maturity:** GA

## Summary

Defer inbox execution and outbox publication until an absolute time or relative delay using visibility metadata on accept and enqueue items, without a separate scheduler service.

## What It Does

`MessageVisibility.Immediate` makes rows eligible for leasing as soon as stored. `MessageVisibility.At(DateTimeOffset)` hides rows until that instant. `MessageVisibility.After(TimeSpan)` hides rows until created time plus delay. Stores filter lease queries by `visible_after`. Ingress maps broker headers `litebus-visible-after` and `litebus-visible-after-delay` to the same model. There is no standalone scheduler interface on writer surfaces.

## Public Surface

### Metadata Value Objects

- **`MessageVisibility.Immediate`**: Default; row is lease-eligible as soon as stored (subject to processor poll).
- **`MessageVisibility.At(DateTimeOffset VisibleAfter)`**: Absolute UTC timestamp before which the row is invisible to lease queries.
- **`MessageVisibility.After(TimeSpan Delay)`**: Relative delay resolved against **`TimeProvider`** at accept or enqueue time.
- **`InboxAcceptMetadata.Visibility` / `OutboxEnqueueMetadata.Visibility`**: Per-message visibility variant on writer metadata records.

### Writer Invocation

- Set visibility on **`InboxAcceptItem`** / **`OutboxEnqueueItem`** metadata with record **`with`**, or use static helpers:
  - **`OutboxEnqueueItem<T>.ScheduledAt(message, visibleAfter)`** for absolute deferral
  - **`OutboxEnqueueItem<T>.ScheduledAfter(message, delay)`** for relative deferral
- Inbox accepts use the same **`MessageVisibility`** variants on **`InboxAcceptMetadata`**.

### Ingress Wire Mapping

- **`TransportHeaders.VisibleAfter`** (`litebus-visible-after`): ISO-8601 absolute timestamp mapped to **`MessageVisibility.At`**
- **`TransportHeaders.VisibleAfterDelay`** (`litebus-visible-after-delay`): Duration mapped to **`MessageVisibility.After`**
- When both headers are present, relative delay takes precedence over absolute timestamp
- Invalid absolute header values are ignored (immediate visibility)

### Store and Processor Behavior

- Writers persist **`visible_after`** column on insert
- Lease queries exclude rows where **`visible_after > now`**
- No delayed accept API; scheduling is store-side filtering only
- Execution timing depends on processor poll interval, lease batch size, and fleet clock sync

### Extension Points

- Custom stores honor **`visible_after`** in **`LeasePendingAsync`** (storage axis)
- Ingress adapters map broker-specific delay headers through **`TransportInboxIngressMapper`** (inbox-ingress capability)

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Messaging.Abstractions` | `MessageVisibility` |
| `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions` | Metadata records |
| `LiteBus.Inbox.Ingress` | Header parsing on ingress |
| `LiteBus.Transport.Abstractions` | `TransportHeaders.VisibleAfter`, `VisibleAfterDelay` |

## Requires

- Store persisting `visible_after` (all built-in stores do)
- Processor polling (rows invisible until threshold)
- Clock sync across fleet for absolute schedules (operational concern)

## Invariants

- Relative delay ingress header takes precedence when both absolute and relative headers present
- Scheduling is store-side filtering, not delayed accept API
- Visibility does not guarantee exact-time execution (depends on poll interval and lease batch)
- FIFO ordering within partition is by `created_at`, subject to visibility

## Non-Goals

- Cron-style recurring jobs
- Distributed scheduler cluster
- Priority queues separate from created-at ordering

## Observability

No dedicated scheduled-vs-ready metric. Deferred rows appear in queue depth while excluded from lease batches.

### Queue Depth (Pending with Future Visibility)

- **Instrument**: `litebus.inbox.queue.depth` / `litebus.outbox.queue.depth`
- **Constants**: `LiteBusInboxTelemetry.QueueDepthInstrumentName`, `LiteBusOutboxTelemetry.QueueDepthInstrumentName`
- **Kind**: Observable gauge
- **Tags**: `litebus.inbox.status` / `litebus.outbox.status` (`QueueStatusAttributeName`) includes pending rows regardless of **`visible_after`**
- **When emitted**: Periodic scrape after processor registration
- **Registration**: `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()`
- **Operational note**: Pending depth may include not-yet-due rows; filter with management query APIs using visibility predicates

### Processor Lease Behavior (Indirect)

- **Instrument**: `litebus.inbox.processor.leases_acquired` / `litebus.outbox.processor.leases_acquired`
- **Kind**: Counter
- **When incremented**: Row leased during processor pass (only when **`visible_after`** has elapsed)
- **Operational note**: Flat lease rate with rising pending depth may indicate many future-scheduled rows

### Management Queries

- **Kind**: Application or operator API (no dedicated meter)
- **When used**: **`IInboxManager`** / **`IOutboxManager`** query filters with visibility or status predicates
- **Operational note**: Use to distinguish ready vs scheduled backlog when poll interval dominates execution SLA

## Deep Docs

- [Inbox](../../reliable-messaging/inbox.md) (visibility, ingress headers)
- [Outbox](../../reliable-messaging/outbox.md) (deferred publication)
- [API Design](../../architecture/api-design.md) (metadata variants)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Outbox.UnitTests`
- `LiteBus.Storage.IntegrationTests`
- `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`
- `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

### Covered Use Cases

#### `MessageVisibilityTests.AcceptAsync_with_At_visibility_should_persist_visible_after`

- **Use case**: When accept uses absolute visibility, a future **`visible_after`** timestamp is stored
- **Test kind**: Unit
- **Description**: **`MessageVisibility.At`** on accept metadata
- **Behavior**: **`IInbox.AcceptAsync`** with deferred visibility
- **Expected outcome**: Future **`visible_after`** persisted on envelope row
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `MessageVisibilityTests.AcceptAsync_with_After_visibility_should_apply_relative_delay`

- **Use case**: When accept uses relative delay visibility, **`visible_after`** is computed from accept time plus delay
- **Test kind**: Unit
- **Description**: **`MessageVisibility.After`** on accept metadata
- **Behavior**: Accept with relative delay metadata
- **Expected outcome**: **`visible_after`** equals created time plus delay
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.AcceptAsync_ShouldPersistVisibleAfterFromMetadata`

- **Use case**: When visibility metadata is supplied on accept, the writer persists **`visible_after`** on the row
- **Test kind**: Unit
- **Description**: Writer metadata path through default inbox
- **Behavior**: Accept with visibility variant on item
- **Expected outcome**: **`visible_after`** column set
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.ProcessPendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseCommand`

- **Use case**: When a row's visibility is still in the future, the processor does not lease it
- **Test kind**: Unit
- **Description**: Processor pass with future **`visible_after`**
- **Behavior**: **`ProcessPendingAsync`** before visibility threshold
- **Expected outcome**: Row not leased; remains pending
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.ProcessPendingAsync_WhenVisibleAfterReached_ShouldProcessCommand`

- **Use case**: When visibility threshold elapses, the processor leases and dispatches the command
- **Test kind**: Unit
- **Description**: Processor pass after **`visible_after`**
- **Behavior**: **`ProcessPendingAsync`** when row becomes due
- **Expected outcome**: Command processed to completed status
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxStoreContractTests.LeasePendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseCommand`

- **Use case**: When the store lease query runs before **`visible_after`**, the row is excluded from the lease batch
- **Test kind**: Contract
- **Description**: Store-level lease filter
- **Behavior**: **`LeasePendingAsync`** with future visibility
- **Expected outcome**: No lease returned for deferred row
- **Remarks**: Store contract suite

#### `PostgreSqlOutboxEndToEndTests.AddAsync_WithVisibleAfter_ShouldDeferPublishingUntilDue`

- **Use case**: When outbox enqueue defers publication, the event is not published until visibility elapses
- **Test kind**: Integration
- **Description**: PostgreSQL outbox with scheduled enqueue and processor
- **Behavior**: Enqueue with **`MessageVisibility.At`**, processor passes before and after due time
- **Expected outcome**: No publish before due; publish after due
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `EfCoreOutboxProcessorDeferredVisibilityEndToEndTests.AddAsync_WithVisibleAfter_ShouldDeferPublishingUntilDue`

- **Use case**: When EF Core outbox stores deferred visibility, publication waits until the row is due
- **Test kind**: Integration
- **Description**: EF outbox processor deferred path
- **Behavior**: Scheduled enqueue through EF store
- **Expected outcome**: Deferred lease and publish timing honored
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `EfCoreInboxProcessorDeferredVisibilityEndToEndTests` (Deferred Command)

- **Use case**: When EF Core inbox stores deferred visibility, command execution waits until the row is due
- **Test kind**: Integration
- **Description**: EF inbox processor deferred path
- **Behavior**: Scheduled accept through EF store
- **Expected outcome**: Deferred processing until visibility elapsed
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `TransportInboxIngressMapperTests.ToInboxAcceptMetadata_ShouldMapVisibleAfterDelayHeader`

- **Use case**: When ingress receives the relative delay header, metadata maps to **`MessageVisibility.After`**
- **Test kind**: Unit
- **Description**: **`litebus-visible-after-delay`** on transport message
- **Behavior**: **`ToInboxAcceptMetadata`** mapper
- **Expected outcome**: **`MessageVisibility.After`** on accept metadata
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `TransportInboxIngressMapperTests.ToInboxAcceptMetadata_ShouldRoundTripDispatchHeaders`

- **Use case**: When dispatch headers include visibility metadata, ingress round-trips them onto accept metadata
- **Test kind**: Unit
- **Description**: Visibility and related headers on wire message
- **Behavior**: Ingress mapper round-trip
- **Expected outcome**: Metadata restored including visibility variant
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `InboxStoreContractTests.AddAsync_ShouldPersistMetadataAndVisibleAfter`

- **Use case**: When the store accepts a row with visibility metadata, **`visible_after`** is persisted on insert
- **Test kind**: Contract
- **Description**: Writer persist at store layer
- **Behavior**: **`AddAsync`** with visibility metadata
- **Expected outcome**: **`visible_after`** column populated
- **Remarks**: Store contract suite

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Absolute vs relative header precedence on ingress | Yes | Documented rule; no conflicting-header integration test | Integration | Low |
| Sub-second exact-time execution | Yes | Poll interval dominates; no SLA test |: | Low |
| Cron-style recurring schedules | No | Not supported |: |: |

### Out-of-Scope Use Cases

- Cron or recurring job scheduler
- Distributed scheduler cluster
- Priority queues separate from created-at ordering
