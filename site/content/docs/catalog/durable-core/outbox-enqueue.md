# Outbox Enqueue

**ID:** `durable-core.outbox-enqueue`
**Maturity:** GA

## Summary

Outbox enqueue is the LiteBus path that writes integration events and other registered message types to durable storage for later publication. It returns a receipt instead of publishing immediately.

## What It Does

`IOutbox` serializes the event, resolves contract metadata, and appends an `OutboxEnvelope`. Later, `PipelinedOutboxProcessor` leases the row and calls `IOutboxDispatcher` to publish it. Batch enqueue stores multiple envelopes in one round trip, and the envelope factory serializes them in parallel.

## Public Surface

### Consumer Contracts

- **`IOutbox`**: This is the application-facing writer for enqueue operations. Inject it in command handlers, domain-event drains, or tools that need an immediate store commit.
- **`OutboxEnqueueItem<TEvent>` / `OutboxEnqueueItem`**: This command input holds the payload with its `OutboxEnqueueMetadata`. Use the untyped variant when the runtime type varies in a batch.
- **`OutboxEnqueueMetadata`**: This record holds the per-message metadata: identity, idempotency, visibility, trace, tenant, and publication target variants. Start from `OutboxEnqueueMetadata.Immediate` and compose with `with`.
- **`OutboxReceipt` / `OutboxEnqueueOutcome`**: This is the enqueue result, which confirms `Enqueued` or `AlreadyEnqueued`. It is not a publish acknowledgment.
- **`PublicationTarget`**: This is the routing metadata stored on the envelope for dispatch: a topic, an exchange, or the default.

### Invocation

- **`EnqueueAsync<TEvent>(OutboxEnqueueItem<TEvent>, CancellationToken)`**: Call this to enqueue one typed event with full metadata.
- **`EnqueueAsync<TEvent>(TEvent, CancellationToken)`**: This body-only overload behaves exactly like `OutboxEnqueueItem<T>.From(message)`.
- **`EnqueueAsync(OutboxEnqueueItem, CancellationToken)`**: Call this to enqueue by runtime type for heterogeneous payloads. Contract lookup uses `event.GetType()`.
- **`EnqueueBatchAsync(IReadOnlyList<OutboxEnqueueItem>, CancellationToken)`**: Call this to enqueue a batch in one store round trip, with homogeneous or heterogeneous items.
- **`OutboxEnqueueItem<T>.From(message)`**: Call this to construct an item from the message body alone.
- **`OutboxEnqueueItem<TEvent>.FromAmbient(message)`**: Call this to build a body-only item stamped with `MessageTrace.FromAmbient()`. An event enqueued inside a mediation then carries the ambient correlation, causation, and distributed trace context. Outside a mediation, the item equals `From(message)`.

Typical call sites are command post-handlers draining domain events, application services publishing integration events, and operational replay tools.

### Registration

- Call **`AddOutbox()`** with **`Use*Storage()`** inside the same outbox module builder. Storage is required before the first enqueue.
- Call **`Contracts.Register<TEvent>(name, version)`** or **`RegisterFromAssembly`** for each stored event type.
- **`UseAmbientTrace()`** on the outbox builder stamps `MessageTrace.FromAmbient()` onto every enqueued envelope whose item trace is `MessageTrace.None`. An explicit item trace always takes precedence. Outside a mediation, the envelope stays untraced.
- **`UseContractResolver(IMessageContractResolver)`** (outbox only) makes the envelope factory resolve the contract from the declared parameter type instead of the runtime instance type. `DeclaredTypeMessageContractResolver` is the implementation included with LiteBus.
- **`EnablePerHandlerDelivery()`** writes one envelope per subscribed main handler instead of one envelope per event (see Per-Handler Delivery below).
- The module registers the default **`IOutbox`** as a transient service that uses the singleton non-transactional store.

### Configuration

- Each call carries metadata on **`OutboxEnqueueItem`**: `MessageIdentity`, `Idempotency`, `MessageVisibility`, trace, `TenantScope`, and `PublicationTarget`.
- At compose time, storage adapter options go on the outbox builder (see the durable-storage capability). They are not mixed into enqueue item metadata.

### Extension Points

- Custom storage implements the append role of **`IOutboxStore`** and returns **`OutboxAppendResult`** so the writer can preserve insertion outcomes (storage axis).
- For domain-transaction alignment, use **`ITransactionalOutbox`** or **`ITransactionalOutbox<TContext>`** instead of **`IOutbox`** (transactional-writes capability).
- **`IOutboxEnvelopeFactory`** controls serialization and field mapping inside the writer pipeline.

## Per-Handler Delivery

`EnablePerHandlerDelivery` writes one envelope per subscribed main handler of each enqueued event instead of one envelope per event. Each reaction then has its own retry, dead letter, and replay. A failing reaction retries alone while the healthy reactions publish.

Each envelope records the full name of the handler it is addressed to in the `Handler` column, where `null` means broadcast. In-process dispatch publishes each envelope to that main handler alone. Pipeline observers for the pre-stage, post, error, and completion stages still run on every targeted dispatch.

```csharp
registry.AddOutbox(outbox =>
{
    outbox.EnablePerHandlerDelivery();
});
```

Enqueueing an event with no subscribed main handler throws `NoHandlerFoundException`, matching the default `RequireHandler` rule. Direct and indirect main handlers both count, deduplicated by handler full name. Per-handler envelopes are ordered by handler full name.

The writer derives a caller idempotency key per handler as `{key}:{handler}` and preserves the conflict mode. From a caller-supplied message identifier, the writer derives deterministic per-handler identifiers, so a retried enqueue resolves to the same rows. The whole fan-out of one enqueue persists in one batch call, so a mid-fan-out failure never leaves a partial set behind.

The returned receipt describes the first per-handler envelope in handler-name order. Batch receipts flatten to one receipt per envelope in item order. A handler added after enqueue does not receive already-enqueued events. A handler removed before dispatch leaves its envelopes with no matching subscriber, and `RequireHandler` decides the outcome for those envelopes.

Use per-handler delivery only with in-process dispatch. Broker dispatchers publish the full event per envelope, so per-handler envelopes would duplicate broker publication. When per-handler delivery is combined with a transport dispatcher, composition throws `DurableStorageConfigurationException`, unless `AllowPerHandlerDeliveryWithTransportDispatch` acknowledges the duplication.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox.Abstractions` | `IOutbox`, `OutboxEnqueueItem`, `OutboxReceipt`, `OutboxAppendResult`, `PublicationTarget` |
| `LiteBus.Outbox` | Default `Outbox` writer |
| `LiteBus.Outbox.Storage.*` | Persistence (required at runtime) |

## Requires

- Call `AddOutbox` with `Use*Storage` inside the same builder.
- Register the contract for each stored event type.
- Register exactly one `IOutboxDispatcher` when the processor is enabled.

## Invariants

- Contract lookup uses `event.GetType()` for each instance.
- A distributed trace context must be valid JSON text. Any other value throws `ArgumentException` at creation, so every provider rejects bad input immediately instead of persisting a row it cannot read back.
- The default `IOutbox` commits immediately, separately from any open domain transaction.
- For a duplicate message ID within the same tenant, or a duplicate tenant-scoped idempotency key, the writer returns the existing row as `AlreadyEnqueued`. A message ID reused across tenants throws `IdempotencyConflictException` instead of returning the foreign row, so supplied IDs must be unguessable per tenant or derived per tenant.
- Do not call `IEventMediator.PublishAsync` for cross-process notifications that must survive a crash or commit with domain state.

## Non-Goals

- This capability does not provide immediate in-process fan-out (use `IEventMediator.PublishAsync`).
- This capability does not provide two-phase broker acknowledgment before terminal persistence.
- This capability does not guarantee exactly-once publication to downstream consumers.

## Observability

Enqueue has no dedicated counter. Writer failures surface as store or contract-resolution exceptions. Committed rows appear in queue depth after the next observable scrape.

### `litebus.outbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusOutboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: Periodic observation on a 10-second cache. After processor registration, the gauge reads `IOutboxDiagnosticsStore`.
- **Tags/dimensions**: `litebus.outbox.status` (`LiteBusOutboxTelemetry.QueueStatusAttributeName`) groups `Pending`, `Publishing`, `Published`, `Failed`, `DeadLettered`
- **How to enable**: `AddLiteBusOutboxMetrics()` from `LiteBus.Outbox.Extensions.OpenTelemetry`
- **Operational note**: Rising `Pending` depth after enqueue bursts signals processor lag. Enqueue itself does not increment a counter.

### `litebus.outbox.processor.published`

- **Kind**: Counter with an internal name, not on the public telemetry type
- **When emitted**: After a processor pass marks one or more rows published
- **Tags/dimensions**: None on the counter. Pass-level activity may carry envelope context.
- **How to enable**: `AddLiteBusOutboxMetrics()` subscribes meter `LiteBus.Outbox` (`LiteBusOutboxTelemetry.MeterName`)
- **Operational note**: Compare published rate to enqueue rate (via pending depth delta) to detect publication backlog.

### `litebus.outbox.processor.failed`

- **Kind**: Counter
- **When emitted**: After dispatch throws and the row is scheduled for retry with updated `visible_after`
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusOutboxMetrics()`
- **Operational note**: Sustained failures often indicate broker or dispatcher misconfiguration, not writer errors.

### `litebus.outbox.processor.dead_lettered`

- **Kind**: Counter
- **When emitted**: When retry attempts exceed `MaxAttempts` and the row moves to dead-letter status
- **Tags/dimensions**: None
- **How to enable**: `AddLiteBusOutboxMetrics()`
- **Operational note**: Dead-letter growth after successful enqueue means downstream dispatch or handler failure, not enqueue rejection.

## References

- [Outbox](../../reliable-messaging/outbox.md)
- [Domain events and unit of work](../../reliable-messaging/domain-events-and-unit-of-work.md)

## Test Coverage

### Covered Use Cases

#### `OutboxTests.OutboxWriter_ShouldStoreEventWithExplicitMessageId`

- **Use case**: Enqueue with explicit message id
- **Test kind**: Unit
- **Description**: In-memory outbox module with registered contract
- **Behavior**: `MessageIdentity.Supplied` on enqueue item
- **Expected outcome**: The row is stored with the supplied id
- **Remarks**:

#### `OutboxProcessorEdgeCaseTests.AddAsync_WhenDuplicateMessageId_ShouldReturnExistingEnvelope`

- **Use case**: Enqueue with a duplicate message id returns the existing envelope
- **Test kind**: Unit
- **Description**: A retried enqueue with the same supplied message id
- **Behavior**: A second enqueue with a duplicate id
- **Expected outcome**: The existing row is returned with `AlreadyEnqueued`
- **Remarks**:

#### `OutboxProcessorEdgeCaseTests.AddAsync_WhenMessageIdOmitted_ShouldGenerateId`

- **Use case**: The writer generates a message id when it is omitted
- **Test kind**: Unit
- **Description**: Enqueue with default identity metadata
- **Behavior**: `MessageIdentity.Generated` path
- **Expected outcome**: A new guid is assigned
- **Remarks**:

#### `OutboxProcessorEdgeCaseTests.AddAsync_WhenContractNotRegistered_ShouldThrowMessageContractNotRegisteredException`

- **Use case**: Enqueue rejects an unregistered contract
- **Test kind**: Unit
- **Description**: Enqueue of an unregistered event type
- **Behavior**: Missing contract registration
- **Expected outcome**: The call throws a typed exception
- **Remarks**:

#### `OutboxTests.AddAsync_ShouldPersistVisibleAfterFromOptions`

- **Use case**: Enqueue persists visibility metadata
- **Test kind**: Unit
- **Description**: Enqueue with deferred publication metadata
- **Behavior**: `MessageVisibility.At` or `After` on metadata
- **Expected outcome**: `visible_after` is set
- **Remarks**:

#### `OutboxTests.EnqueueBatchAsync_ShouldStoreAllEventsWithRuntimeTypes`

- **Use case**: Batch enqueue with runtime types
- **Test kind**: Unit
- **Description**: Heterogeneous batch of event types
- **Behavior**: `EnqueueBatchAsync` with mixed items
- **Expected outcome**: All rows are stored with the correct contracts
- **Remarks**:

#### `OutboxEnvelopeFactoryTests.CreateAsync_should_match_outbox_writer_fields`

- **Use case**: The envelope factory matches the writer fields
- **Test kind**: Unit
- **Description**: The factory output is compared to the writer-produced envelope
- **Behavior**: `CreateAsync` on the envelope factory
- **Expected outcome**: The fields align
- **Remarks**:

#### `OutboxTests.Register_ShouldRejectOpenGenericDurableContracts`

- **Use case**: Open generic contract registration is rejected
- **Test kind**: Unit
- **Description**: The module builder registers an open generic
- **Behavior**: `Contracts.Register` with an open generic type
- **Expected outcome**: The call throws a configuration exception
- **Remarks**:

#### `OutboxStoreContractTests.AddAsync_ShouldReturnExistingMessageForDuplicateIdempotencyKey`

- **Use case**: The store deduplicates a duplicate idempotency key
- **Test kind**: Contract
- **Description**: Store-level append deduplication, in the shared contract suite across backends
- **Behavior**: Insert deduplication on a tenant-scoped idempotency key
- **Expected outcome**: The existing envelope is returned with `AlreadyEnqueued`
- **Remarks**: Shared contract suite

#### `OutboxStoreContractTests.AddAsync_ShouldPersistTopicMetadataAndVisibleAfter`

- **Use case**: The store persists topic and visibility
- **Test kind**: Contract
- **Description**: Publication target and visibility metadata at the store layer
- **Behavior**: Enqueue with topic and `visible_after`
- **Expected outcome**: The columns are populated
- **Remarks**:

#### `TransactionalOutboxEnqueueTests.EnqueueAsync_should_stage_envelope_with_contract_and_metadata`

- **Use case**: EF transactional enqueue stages the envelope
- **Test kind**: Unit
- **Description**: Shared DbContext transactional writer
- **Behavior**: Enqueue through `ITransactionalOutbox<TContext>` before SaveChanges
- **Expected outcome**: The row stays staged until SaveChanges
- **Remarks**:

#### `StoreBoundTransactionalOutboxTests.EnqueueAsync_should_write_to_bound_store_only`

- **Use case**: Manual bind transactional enqueue
- **Test kind**: Integration
- **Description**: PostgreSQL manual store bind
- **Behavior**: A `CreateTransactionalStore` bind
- **Expected outcome**: The write goes only to the bound connection
- **Remarks**:

#### `EfCoreOutboxFindExistingTests.EnqueueAsync_when_id_and_idempotency_key_match_different_rows_should_prefer_message_id_row`

- **Use case**: Id and idempotency collision resolution
- **Test kind**: Unit
- **Description**: Conflicting message id and idempotency key on the EF path
- **Behavior**: Enqueue with keys pointing at different existing rows
- **Expected outcome**: The message id row takes precedence
- **Remarks**: EF-specific

#### `AmbientEnqueueItemTests.FromAmbient_inside_a_processor_built_scope_stamps_the_distributed_trace`

- **Use case**: The ambient trace is stamped on the enqueue item
- **Test kind**: Unit
- **Description**: Calling `OutboxEnqueueItem<T>.FromAmbient(message)` inside a traced mediation
- **Behavior**: Ambient trace factory on the enqueue item
- **Expected outcome**: The item trace holds the ambient correlation, causation, and trace context
- **Remarks**: Companion tests cover the workflow fallback, the preserved ambient causation, and the untraced result outside any scope

#### `OutboxAmbientTraceTests.Enqueue_inside_a_traced_mediation_should_stamp_ambient_trace_when_opted_in`

- **Use case**: Module ambient-trace opt-in on enqueue
- **Test kind**: Unit
- **Description**: Outbox module with `UseAmbientTrace` enabled
- **Behavior**: Enqueue with `MessageTrace.None` inside a traced mediation
- **Expected outcome**: The stored envelope holds the ambient trace, and explicit item traces are kept.
- **Remarks**: `LiteBus.Outbox.UnitTests` (`AmbientTrace/`)

#### `PerHandlerDeliveryTests.EnqueueAsync_should_write_one_envelope_per_subscribed_handler`

- **Use case**: Per-handler envelopes on enqueue
- **Test kind**: Unit
- **Description**: Outbox module with `EnablePerHandlerDelivery` and two subscribed handlers
- **Behavior**: `EnqueueAsync` of one event
- **Expected outcome**: The store holds one envelope per handler, each addressed on `Handler`
- **Remarks**: `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`)

#### `PerHandlerDeliveryTests.EnqueueAsync_without_a_subscribed_handler_should_throw`

- **Use case**: A per-handler enqueue with no subscriber fails
- **Test kind**: Unit
- **Description**: An event with no registered main handler under per-handler delivery
- **Behavior**: `EnqueueAsync`
- **Expected outcome**: `NoHandlerFoundException`
- **Remarks**: `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`)

#### `PerHandlerDeliveryTests.DeadLetter_and_requeue_should_apply_per_handler`

- **Use case**: Per-handler retry, dead letter, and replay
- **Test kind**: Unit
- **Description**: One failing reaction beside a healthy reaction on the same event
- **Behavior**: Processor passes followed by an operator requeue
- **Expected outcome**: Only the failing reaction moves to dead letter and replays
- **Remarks**: `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`)

#### `PerHandlerDeliveryTests.Targeted_dispatch_should_run_observers_on_every_envelope`

- **Use case**: Pipeline observers run on targeted dispatch
- **Test kind**: Unit
- **Description**: A per-handler envelope dispatched in-process
- **Behavior**: Targeted publish to the recorded handler
- **Expected outcome**: Pre, post, error, and completion observers still run
- **Remarks**: `LiteBus.Outbox.UnitTests` (`PerHandlerDelivery/`)

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Enqueue with AMQP topic routing in production topology | Yes | Topic routing is covered in dispatch tests, not in writer tests | Integration | Low |
| Enqueue batch of 1000+ events | Yes | No stress test for batch serialization | Integration | Low |
| Cross-process enqueue without storage | No | Not supported | N/A | N/A |

### Out-of-Scope Use Cases

- Immediate `IEventMediator.PublishAsync` fan-out
- Two-phase broker acknowledgment before terminal persist
- Exactly-once publication to downstream consumers
