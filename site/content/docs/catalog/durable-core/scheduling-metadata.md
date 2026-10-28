# Scheduling and Visibility Metadata

**ID:** `durable-core.scheduling-metadata`
**Maturity:** GA

## Summary

In LiteBus, scheduling and visibility metadata defers inbox execution and outbox publication until an absolute time or relative delay. It lives on accept and enqueue items, so no separate scheduler service is needed.

## What It Does

`MessageVisibility.Immediate` makes rows eligible for leasing as soon as they are stored. `MessageVisibility.At(DateTimeOffset)` hides rows until that instant. `MessageVisibility.After(TimeSpan)` hides rows until the created time plus the delay.

Stores filter lease queries by `visible_after`. Ingress maps the broker headers `litebus-visible-after` and `litebus-visible-after-delay` to the same model. There is no standalone scheduler interface on writer surfaces.

## Public Surface

### Metadata Value Objects

- **`MessageVisibility.Immediate`**: This is the default. The row is lease-eligible as soon as it is stored, subject to processor poll.
- **`MessageVisibility.At(DateTimeOffset VisibleAfter)`**: This is an absolute UTC timestamp before which the row is invisible to lease queries.
- **`MessageVisibility.After(TimeSpan Delay)`**: This is a relative delay resolved against **`TimeProvider`** at accept or enqueue time.
- **`InboxAcceptMetadata.Visibility` / `OutboxEnqueueMetadata.Visibility`**: These hold the per-message visibility variant on writer metadata records.

### Writer Invocation

- Set visibility on **`InboxAcceptItem`** / **`OutboxEnqueueItem`** metadata with record **`with`**, or use static helpers:
  - **`OutboxEnqueueItem<T>.ScheduledAt(message, visibleAfter)`** / **`InboxAcceptItem<T>.ScheduledAt(message, visibleAfter)`** for absolute deferral
  - **`OutboxEnqueueItem<T>.ScheduledAfter(message, delay)`** / **`InboxAcceptItem<T>.ScheduledAfter(message, delay)`** for relative deferral
- Inbox accepts use the same **`MessageVisibility`** variants on **`InboxAcceptMetadata`**.

### Ingress Wire Mapping

- **`TransportHeaders.VisibleAfter`** (`litebus-visible-after`) is an ISO-8601 absolute timestamp mapped to **`MessageVisibility.At`**.
- **`TransportHeaders.VisibleAfterDelay`** (`litebus-visible-after-delay`) is a duration mapped to **`MessageVisibility.After`**.
- Visibility headers are honored only when `TrustApplicationHeaders` is `true`. Distrusted deliveries are immediately visible.
- When both headers are present, relative delay takes precedence over absolute timestamp.
- Ingress ignores invalid absolute header values, so the message stays immediately visible.

### Store and Processor Behavior

- Writers persist the **`visible_after`** column on insert.
- Lease queries exclude rows where **`visible_after > now`**.
- There is no delayed accept API. Scheduling is store-side filtering only.
- Execution timing depends on the processor poll interval, the lease batch size, and clock synchronization across the fleet.

### Extension Points

- Custom stores honor **`visible_after`** in **`LeasePendingAsync`** (storage axis).
- Ingress adapters map broker-specific delay headers through **`TransportInboxIngressMapper`** (inbox-ingress capability).

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.DurableMessaging.Abstractions` | `MessageVisibility` |
| `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions` | Metadata records |
| `LiteBus.Inbox.Ingress` | Header parsing on ingress |
| `LiteBus.Transport.Abstractions` | `TransportHeaders.VisibleAfter`, `VisibleAfterDelay` |

## Requires

- Use a store that persists `visible_after`. All built-in stores do.
- Run processor polling, since rows stay invisible until the threshold.
- Synchronize clocks across the fleet for absolute schedules. Clock synchronization is the operator's responsibility.

## Invariants

- The relative delay ingress header takes precedence when both absolute and relative headers are present.
- Scheduling is store-side filtering, not a delayed accept API.
- Visibility does not guarantee exact-time execution, because execution depends on the poll interval and the lease batch.
- FIFO ordering within a partition is by `created_at`, subject to visibility.

## Non-Goals

- This capability does not support cron expressions. Fixed-interval recurring commands are available separately through `CommandModuleBuilder.AddRecurring` (see [Recurring Commands](../../integrations/recurring-commands.md)).
- This capability does not provide a distributed scheduler cluster.
- This capability does not provide priority queues separate from created-at ordering.

## Observability

There is no dedicated scheduled-vs-ready metric. Deferred rows appear in queue depth while excluded from lease batches.

### Queue Depth (Pending with Future Visibility)

- **Instrument**: `litebus.inbox.queue.depth` / `litebus.outbox.queue.depth`
- **Constants**: `LiteBusInboxTelemetry.QueueDepthInstrumentName`, `LiteBusOutboxTelemetry.QueueDepthInstrumentName`
- **Kind**: Observable gauge
- **Tags**: `litebus.inbox.status` / `litebus.outbox.status` (`QueueStatusAttributeName`) include pending rows regardless of **`visible_after`**
- **When emitted**: Periodic scrape after processor registration
- **Registration**: `AddLiteBusInboxMetrics()` / `AddLiteBusOutboxMetrics()`
- **Operational note**: Pending depth may include not-yet-due rows. Filter with management query APIs using visibility predicates.

### Processor Lease Behavior (Indirect)

- **Instrument**: `litebus.inbox.processor.leases_acquired` / `litebus.outbox.processor.leases_acquired`
- **Kind**: Counter
- **When incremented**: A row is leased during a processor pass, only when **`visible_after`** has elapsed.
- **Operational note**: A flat lease rate with rising pending depth may indicate many future-scheduled rows.

### Management Queries

- **Kind**: Application or operator API (no dedicated meter)
- **When used**: Use **`IInboxManager`** / **`IOutboxManager`** query filters with visibility or status predicates.
- **Operational note**: Use them to distinguish ready backlog from scheduled backlog when the poll interval dominates the execution SLA (service-level agreement).

## References

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
- **Expected outcome**: A future **`visible_after`** is persisted on the envelope row
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `MessageVisibilityTests.AcceptAsync_with_After_visibility_should_apply_relative_delay`

- **Use case**: When accept uses relative delay visibility, **`visible_after`** is computed from the accept time plus the delay
- **Test kind**: Unit
- **Description**: **`MessageVisibility.After`** on accept metadata
- **Behavior**: Accept with relative delay metadata
- **Expected outcome**: **`visible_after`** equals the created time plus the delay
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.AcceptAsync_ShouldPersistVisibleAfterFromMetadata`

- **Use case**: When visibility metadata is supplied on accept, the writer persists **`visible_after`** on the row
- **Test kind**: Unit
- **Description**: The writer metadata path through the default inbox
- **Behavior**: Accept with a visibility variant on the item
- **Expected outcome**: The **`visible_after`** column is set
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.ProcessPendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseCommand`

- **Use case**: When a row's visibility is still in the future, the processor does not lease it
- **Test kind**: Unit
- **Description**: A processor pass with a future **`visible_after`**
- **Behavior**: **`ProcessPendingAsync`** before the visibility threshold
- **Expected outcome**: The row is not leased and remains pending.
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.ProcessPendingAsync_WhenVisibleAfterReached_ShouldProcessCommand`

- **Use case**: When the visibility threshold elapses, the processor leases and dispatches the command
- **Test kind**: Unit
- **Description**: A processor pass after **`visible_after`**
- **Behavior**: **`ProcessPendingAsync`** when the row becomes due
- **Expected outcome**: The command is processed to completed status
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxStoreContractTests.LeasePendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseCommand`

- **Use case**: When the store lease query runs before **`visible_after`**, the row is excluded from the lease batch
- **Test kind**: Contract
- **Description**: Store-level lease filter
- **Behavior**: **`LeasePendingAsync`** with future visibility
- **Expected outcome**: No lease is returned for the deferred row
- **Remarks**: Store contract suite

#### `PostgreSqlOutboxEndToEndTests.AddAsync_WithVisibleAfter_ShouldDeferPublishingUntilDue`

- **Use case**: When outbox enqueue defers publication, the event is not published until visibility elapses
- **Test kind**: Integration
- **Description**: A PostgreSQL outbox with scheduled enqueue and processor
- **Behavior**: Enqueue with **`MessageVisibility.At`**, with processor passes before and after the due time
- **Expected outcome**: Nothing is published before the due time, and the processor publishes after it.
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

#### `EfCoreOutboxProcessorDeferredVisibilityEndToEndTests.AddAsync_WithVisibleAfter_ShouldDeferPublishingUntilDue`

- **Use case**: When the EF Core outbox stores deferred visibility, publishing waits until the row is due
- **Test kind**: Integration
- **Description**: The EF outbox processor deferred path
- **Behavior**: A scheduled enqueue through the EF store
- **Expected outcome**: The deferred lease and publish timing are honored
- **Remarks**: `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `EfCoreInboxProcessorDeferredVisibilityEndToEndTests` (Deferred Command)

- **Use case**: When the EF Core inbox stores deferred visibility, command execution waits until the row is due
- **Test kind**: Integration
- **Description**: The EF inbox processor deferred path
- **Behavior**: A scheduled accept through the EF store
- **Expected outcome**: Processing is deferred until visibility elapses
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `TransportInboxIngressMapperTests.ToInboxAcceptMetadata_ShouldMapVisibleAfterDelayHeader`

- **Use case**: When ingress receives the relative delay header, the metadata maps to **`MessageVisibility.After`**
- **Test kind**: Unit
- **Description**: **`litebus-visible-after-delay`** on a transport message
- **Behavior**: **`ToInboxAcceptMetadata`** mapper
- **Expected outcome**: **`MessageVisibility.After`** on accept metadata
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `TransportInboxIngressMapperTests.ToInboxAcceptMetadata_ShouldRoundTripDispatchHeaders`

- **Use case**: When dispatch headers include visibility metadata, ingress round-trips them onto accept metadata
- **Test kind**: Unit
- **Description**: Visibility and related headers on the wire message
- **Behavior**: Ingress mapper round-trip
- **Expected outcome**: The metadata is restored, including the visibility variant
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `InboxStoreContractTests.AddAsync_ShouldPersistMetadataAndVisibleAfter`

- **Use case**: When the store accepts a row with visibility metadata, **`visible_after`** is persisted on insert
- **Test kind**: Contract
- **Description**: The writer persistence at the store layer
- **Behavior**: **`AddAsync`** with visibility metadata
- **Expected outcome**: The **`visible_after`** column is populated
- **Remarks**: Store contract suite

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Absolute and relative header precedence on ingress | Yes | Documented rule, with no conflicting-header integration test | Integration | Low |
| Sub-second exact-time execution | Yes | The poll interval dominates, and there is no SLA test | N/A | Low |
| Cron-style recurring schedules | No | Cron expressions are not supported. Fixed-interval commands are available separately. | N/A | N/A |

### Out-of-Scope Use Cases

- Cron expressions or a recurring job scheduler on this axis are out of scope. Fixed-interval commands are available as `LiteBus.Commands.Recurring`.
- Distributed scheduler cluster
- Priority queues separate from created-at ordering
