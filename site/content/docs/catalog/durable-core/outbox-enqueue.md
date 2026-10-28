# Outbox Enqueue

**ID:** `durable-core.outbox-enqueue`
**Maturity:** GA

## Summary

Enqueue integration events and other registered message types into durable storage for later publication, returning a receipt instead of publishing immediately.

## What It Does

`IOutbox` serializes the event, resolves contract metadata, and appends an `OutboxEnvelope`. Publication happens later when `PipelinedOutboxProcessor` leases the row and calls `IOutboxDispatcher`. Batch enqueue stores multiple envelopes in one round trip with parallel serialization inside the envelope factory.

## Public Surface

### Consumer Contracts

- **`IOutbox`**: Application-facing writer for enqueue operations. Inject at command handlers, domain-event drains, or tools that need immediate store commit.
- **`OutboxEnqueueItem<TEvent>` / `OutboxEnqueueItem`**: Command input carrying payload plus `OutboxEnqueueMetadata`. Use the untyped variant when the runtime type varies in a batch.
- **`OutboxEnqueueMetadata`**: Per-message identity, idempotency, visibility, trace, tenant, and publication target variants. Start from `OutboxEnqueueMetadata.Immediate` and compose with `with`.
- **`OutboxReceipt` / `OutboxEnqueueOutcome`**: Enqueue result confirming `Enqueued` or `AlreadyEnqueued`. Not a publish acknowledgment.
- **`PublicationTarget`**: Routing metadata (topic, exchange, or default) stored on the envelope for dispatch.

### Invocation

- **`EnqueueAsync<TEvent>(OutboxEnqueueItem<TEvent>, CancellationToken)`**: Primary typed enqueue with full metadata.
- **`EnqueueAsync<TEvent>(TEvent, CancellationToken)`**: Body-only sugar equivalent to `OutboxEnqueueItem<T>.From(message)`.
- **`EnqueueAsync(OutboxEnqueueItem, CancellationToken)`**: Runtime-type enqueue for heterogeneous payloads; contract lookup uses `event.GetType()`.
- **`EnqueueBatchAsync(IReadOnlyList<OutboxEnqueueItem>, CancellationToken)`**: Batch enqueue in one store round trip (homogeneous or heterogeneous).
- **`OutboxEnqueueItem<T>.From(message)`**: Static factory for body-only construction.
- **`OutboxEnqueueItem<TEvent>.FromAmbient(message)`**: Body-only enqueue that stamps `MessageTrace.FromAmbient()` onto the item, so an event enqueued inside a mediation carries the ambient correlation, causation, and distributed trace context; outside a mediation the item equals `From(message)`.

Typical call sites: command post-handlers draining domain events, application services publishing integration events, operational replay tools.

### Registration

- **`AddOutbox()`** with **`Use*Storage()`** registered inside the same outbox module builder (required before first enqueue).
- **`Contracts.Register<TEvent>(name, version)`** or **`RegisterFromAssembly`** for each stored event type.
- **`UseAmbientTrace()`** on the outbox builder stamps `MessageTrace.FromAmbient()` onto every enqueued envelope whose item trace is `MessageTrace.None`; an explicit item trace always wins, and outside a mediation the envelope stays untraced.
- **`UseContractResolver(IMessageContractResolver)`** (outbox only) lets the envelope factory resolve the contract from the declared parameter type instead of the runtime instance type; `DeclaredTypeMessageContractResolver` is the shipped implementation.
- **`EnablePerHandlerDelivery()`** writes one envelope per subscribed main handler instead of one envelope per event (see Per-Handler Delivery below).
- Default **`IOutbox`** registers transient against the singleton non-transactional store.

### Configuration

- Per-call: metadata on **`OutboxEnqueueItem`** (`MessageIdentity`, `Idempotency`, `MessageVisibility`, trace, `TenantScope`, `PublicationTarget`).
- Compose-time: storage adapter options on the outbox builder (see durable-storage capability); not mixed into enqueue item metadata.

### Extension Points

- Custom storage implements **`IOutboxStore`** append role and returns **`OutboxAppendResult`** so the writer can preserve insertion outcomes (storage axis).
- For domain-transaction alignment use **`ITransactionalOutbox`** or **`ITransactionalOutbox<TContext>`** instead of **`IOutbox`** (transactional-writes capability).
- **`IOutboxEnvelopeFactory`** controls serialization and field mapping inside the writer pipeline.

## Per-Handler Delivery

`EnablePerHandlerDelivery` writes one envelope per subscribed main handler of each enqueued event instead of one envelope per event, so each reaction gets its own retry, dead letter, and replay: a failing reaction retries alone while the healthy reactions publish. Each envelope records the full name of the handler it is addressed to in the `Handler` column (`null` means broadcast), and in-process dispatch publishes each envelope to that main handler alone while pipeline observers (pre-stage, post, error, and completion stages) still run on every targeted dispatch.

```csharp
registry.AddOutbox(outbox =>
{
    outbox.EnablePerHandlerDelivery();
});
```

Enqueueing an event with no subscribed main handler throws `NoHandlerFoundException`, matching the default `RequireHandler` rule. Direct plus indirect main handlers count, deduplicated by handler full name, and per-handler envelopes are ordered by handler full name. A caller idempotency key is derived per handler as `{key}:{handler}` preserving the conflict mode, and a caller-supplied message identifier derives deterministic per-handler identifiers, so a retried enqueue resolves to the same rows. The whole fan-out of one enqueue persists in one batch call, so a mid-fan-out failure never leaves a partial set behind. The returned receipt describes the first per-handler envelope in handler-name order; batch receipts flatten one per envelope in item order. A handler added after enqueue does not receive already-enqueued events, and a handler removed before dispatch leaves its envelopes with no matching subscriber, which `RequireHandler` then decides. Use per-handler delivery only with in-process dispatch: broker dispatchers publish the full event per envelope, so per-handler envelopes would duplicate broker publication. Composition throws `DurableStorageConfigurationException` when per-handler delivery meets a transport dispatcher unless `AllowPerHandlerDeliveryWithTransportDispatch` acknowledges the duplication.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox.Abstractions` | `IOutbox`, `OutboxEnqueueItem`, `OutboxReceipt`, `OutboxAppendResult`, `PublicationTarget` |
| `LiteBus.Outbox` | Default `Outbox` writer |
| `LiteBus.Outbox.Storage.*` | Persistence (required at runtime) |

## Requires

- `AddOutbox` with `Use*Storage` inside the same builder
- Contract registration for each stored event type
- Exactly one `IOutboxDispatcher` when the processor is enabled

## Invariants

- Contract lookup uses `event.GetType()` for each instance
- A distributed trace context must be valid JSON text; creation throws `ArgumentException` otherwise, so every provider fails fast instead of persisting a row it cannot read back
- Default `IOutbox` commits immediately (separate from open domain transactions)
- `AlreadyEnqueued` returns the existing row for a duplicate message ID within the same tenant or a tenant-scoped idempotency key; a message ID reused across tenants throws `IdempotencyConflictException` instead of returning the foreign row, so supplied IDs must be unguessable per tenant or derived per tenant
- Do not call `IEventMediator.PublishAsync` for cross-process notifications that must survive crash or commit with domain state

## Non-Goals

- Immediate in-process fan-out (use `IEventMediator.PublishAsync`)
- Two-phase broker acknowledgment before terminal persistence
- Guaranteed exactly-once publication to downstream consumers

## Observability

No dedicated enqueue counter. Writer failures surface as store or contract-resolution exceptions. Committed rows appear in queue depth after the next observable scrape.

### `litebus.outbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusOutboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: Periodic observation (10-second cache) after processor registration reads `IOutboxDiagnosticsStore`
- **Tags/dimensions**: `litebus.outbox.status` (`LiteBusOutboxTelemetry.QueueStatusAttributeName`) groups `Pending`, `Publishing`, `Published`, `Failed`, `DeadLettered`
- **How to enable**: `AddLiteBusOutboxMetrics()` from `LiteBus.Outbox.Extensions.OpenTelemetry`
- **Operational note**: Rising `Pending` depth after enqueue bursts signals processor lag; enqueue itself does not increment a counter

### `litebus.outbox.processor.published`

- **Kind**: Counter (internal name; not on public telemetry type)
- **When emitted**: After a processor pass marks one or more rows published
- **Tags/dimensions**: None on the counter; pass-level activity may carry envelope context
- **How to enable**: `AddLiteBusOutboxMetrics()` subscribes meter `LiteBus.Outbox` (`LiteBusOutboxTelemetry.MeterName`)
- **Operational note**: Compare published rate to enqueue rate (via pending depth delta) to detect publication backlog

### `litebus.outbox.processor.failed`

- **Kind**: Counter
- **When emitted**: After dispatch throws and the row is scheduled for retry with updated `visible_after`
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusOutboxMetrics()`
- **Operational note**: Sustained failures often indicate broker or dispatcher misconfiguration, not writer errors

### `litebus.outbox.processor.dead_lettered`

- **Kind**: Counter
- **When emitted**: When retry attempts exceed `MaxAttempts` and the row moves to dead-letter status
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusOutboxMetrics()`
- **Operational note**: Dead-letter growth after successful enqueue means downstream dispatch or handler failure, not enqueue rejection

## Deep Docs

- [Outbox](../../reliable-messaging/outbox.md)
- [Domain events and unit of work](../../reliable-messaging/domain-events-and-unit-of-work.md)

## Test Coverage

### Covered Use Cases

#### `OutboxTests.OutboxWriter_ShouldStoreEventWithExplicitMessageId`

- **Use case**: Enqueue with explicit message id
- **Test kind**: Unit
- **Description**: In-memory outbox module with registered contract
- **Behavior**: `MessageIdentity.Supplied` on enqueue item
- **Expected outcome**: Row stored with supplied id
- **Remarks**:

#### `OutboxProcessorEdgeCaseTests.AddAsync_WhenDuplicateMessageId_ShouldReturnExistingEnvelope`

- **Use case**: Duplicate message id returns existing envelope
- **Test kind**: Unit
- **Description**: Retry enqueue with same supplied message id
- **Behavior**: Second enqueue with duplicate id
- **Expected outcome**: Existing row returned with `AlreadyEnqueued`
- **Remarks**:

#### `OutboxProcessorEdgeCaseTests.AddAsync_WhenMessageIdOmitted_ShouldGenerateId`

- **Use case**: Generated message id when omitted
- **Test kind**: Unit
- **Description**: Enqueue with default identity metadata
- **Behavior**: `MessageIdentity.Generated` path
- **Expected outcome**: New guid assigned
- **Remarks**:

#### `OutboxProcessorEdgeCaseTests.AddAsync_WhenContractNotRegistered_ShouldThrowMessageContractNotRegisteredException`

- **Use case**: Enqueue rejects unregistered contract
- **Test kind**: Unit
- **Description**: Enqueue of unregistered event type
- **Behavior**: Missing contract registration
- **Expected outcome**: Typed exception
- **Remarks**:

#### `OutboxTests.AddAsync_ShouldPersistVisibleAfterFromOptions`

- **Use case**: Enqueue persists visibility metadata
- **Test kind**: Unit
- **Description**: Enqueue with deferred publication metadata
- **Behavior**: `MessageVisibility.At` or `After` on metadata
- **Expected outcome**: `visible_after` set
- **Remarks**:

#### `OutboxTests.EnqueueBatchAsync_ShouldStoreAllEventsWithRuntimeTypes`

- **Use case**: Batch enqueue with runtime types
- **Test kind**: Unit
- **Description**: Heterogeneous batch of event types
- **Behavior**: `EnqueueBatchAsync` with mixed items
- **Expected outcome**: All rows stored with correct contracts
- **Remarks**:

#### `OutboxEnvelopeFactoryTests.CreateAsync_should_match_outbox_writer_fields`

- **Use case**: Envelope factory matches writer fields
- **Test kind**: Unit
- **Description**: Factory output compared to writer-produced envelope
- **Behavior**: `CreateAsync` on envelope factory
- **Expected outcome**: Fields aligned
- **Remarks**:

#### `OutboxTests.Register_ShouldRejectOpenGenericDurableContracts`

- **Use case**: Open generic contract registration rejected
- **Test kind**: Unit
- **Description**: Module builder register open generic
- **Behavior**: `Contracts.Register` with open generic type
- **Expected outcome**: Configuration exception
- **Remarks**:

#### `OutboxStoreContractTests.AddAsync_ShouldReturnExistingMessageForDuplicateIdempotencyKey`

- **Use case**: Store duplicate idempotency key dedup
- **Test kind**: Contract
- **Description**: Store-level append dedup (shared contract suite across backends)
- **Behavior**: Insert dedup on tenant-scoped idempotency key
- **Expected outcome**: Existing envelope with `AlreadyEnqueued`
- **Remarks**: Shared contract suite

#### `OutboxStoreContractTests.AddAsync_ShouldPersistTopicMetadataAndVisibleAfter`

- **Use case**: Store persists topic and visibility
- **Test kind**: Contract
- **Description**: Publication target and visibility metadata at store layer
- **Behavior**: Enqueue with topic and `visible_after`
- **Expected outcome**: Columns populated
- **Remarks**:

#### `TransactionalOutboxEnqueueTests.EnqueueAsync_should_stage_envelope_with_contract_and_metadata`

- **Use case**: EF transactional enqueue stages envelope
- **Test kind**: Unit
- **Description**: Shared DbContext transactional writer
- **Behavior**: `ITransactionalOutbox<TContext>` enqueue before SaveChanges
- **Expected outcome**: Row staged until SaveChanges
- **Remarks**:

#### `StoreBoundTransactionalOutboxTests.EnqueueAsync_should_write_to_bound_store_only`

- **Use case**: Manual bind transactional enqueue
- **Test kind**: Integration
- **Description**: PostgreSQL manual store bind
- **Behavior**: `CreateTransactionalStore` bind
- **Expected outcome**: Writes only on bound connection
- **Remarks**:

#### `EfCoreOutboxFindExistingTests.EnqueueAsync_when_id_and_idempotency_key_match_different_rows_should_prefer_message_id_row`

- **Use case**: Id and idempotency collision resolution
- **Test kind**: Unit
- **Description**: Conflicting message id and idempotency key on EF path
- **Behavior**: Enqueue with keys pointing at different existing rows
- **Expected outcome**: Message id row wins
- **Remarks**: EF-specific

#### `AmbientEnqueueItemTests.FromAmbient_inside_a_processor_built_scope_stamps_the_distributed_trace`

- **Use case**: Ambient trace stamped on enqueue item
- **Test kind**: Unit
- **Description**: `OutboxEnqueueItem<T>.FromAmbient(message)` inside a traced mediation
- **Behavior**: Ambient trace factory on the enqueue item
- **Expected outcome**: Item trace holds the ambient correlation, causation, and trace context
- **Remarks**: Companion tests cover the workflow fallback, the preserved ambient causation, and the untraced result outside any scope

#### `OutboxAmbientTraceTests.Enqueue_inside_a_traced_mediation_should_stamp_ambient_trace_when_opted_in`

- **Use case**: Module ambient-trace opt-in on enqueue
- **Test kind**: Unit
- **Description**: Outbox module with `UseAmbientTrace` enabled
- **Behavior**: Enqueue with `MessageTrace.None` inside a traced mediation
- **Expected outcome**: Stored envelope carries the ambient trace; explicit item traces are kept
- **Remarks**: `LiteBus.Outbox.UnitTests` (`AmbientTrace/`)

#### `PerHandlerDeliveryTests.EnqueueAsync_should_write_one_envelope_per_subscribed_handler`

- **Use case**: Per-handler envelopes on enqueue
- **Test kind**: Unit
- **Description**: Outbox module with `EnablePerHandlerDelivery` and two subscribed handlers
- **Behavior**: `EnqueueAsync` of one event
- **Expected outcome**: One stored envelope per handler, each addressed on `Handler`
- **Remarks**: `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`)

#### `PerHandlerDeliveryTests.EnqueueAsync_without_a_subscribed_handler_should_throw`

- **Use case**: Per-handler enqueue with no subscriber fails
- **Test kind**: Unit
- **Description**: Event with no registered main handler under per-handler delivery
- **Behavior**: `EnqueueAsync`
- **Expected outcome**: `NoHandlerFoundException`
- **Remarks**: `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`)

#### `PerHandlerDeliveryTests.DeadLetter_and_requeue_should_apply_per_handler`

- **Use case**: Per-handler retry, dead letter, and replay
- **Test kind**: Unit
- **Description**: One failing reaction beside a healthy reaction on the same event
- **Behavior**: Processor passes then operator requeue
- **Expected outcome**: Only the failing reaction dead-letters and replays
- **Remarks**: `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`)

#### `PerHandlerDeliveryTests.Targeted_dispatch_should_run_observers_on_every_envelope`

- **Use case**: Pipeline observers run on targeted dispatch
- **Test kind**: Unit
- **Description**: Per-handler envelope dispatched in-process
- **Behavior**: Targeted publish to the recorded handler
- **Expected outcome**: Pre, post, error, and completion observers still run
- **Remarks**: `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`)

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Enqueue with AMQP topic routing in production topology | Yes | Topic routing covered in dispatch tests, not writer | Integration | Low |
| Enqueue batch of 1000+ events | Yes | No stress test for batch serialization | Integration | Low |
| Cross-process enqueue without storage | No | Not supported | N/A | N/A |

### Out-of-Scope Use Cases

- Immediate `IEventMediator.PublishAsync` fan-out
- Two-phase broker acknowledgment before terminal persist
- Exactly-once publication to downstream consumers
