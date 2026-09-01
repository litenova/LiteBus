# Acceptance Idempotency

**ID:** `durable-core.idempotency`
**Maturity:** GA

## Summary

Collapse duplicate accept and enqueue attempts at store insert time using message identity and tenant-scoped idempotency keys.

## What It Does

Writers persist optional idempotency keys with envelopes. On duplicate `(tenant_id, idempotency_key)` or duplicate `message_id`, stores return the existing row with `AlreadyAccepted` or `AlreadyEnqueued`. Transactional EF writers resolve existing rows in `ReturnExisting` mode; `Strict` mode leaves the conflict for `SaveChanges` to reject. Ingress generates broker-scoped idempotency keys from delivery ids. Handlers remain responsible for idempotent side effects because processing is still at-least-once.

## Public Surface

### Metadata Value Objects

- **`Idempotency.None`**: No insert-time deduplication key. Duplicate rows are possible when callers retry without a key.
- **`Idempotency.Keyed(string Key, IdempotencyConflictMode ConflictMode = ReturnExisting)`**: Application-defined key stored with the envelope. `ReturnExisting` returns the prior row; `Strict` throws `IdempotencyConflictException`.
- **`MessageIdentity.Generated`**: Writer assigns a new message id at accept or enqueue time.
- **`MessageIdentity.Supplied(Guid)`**: Caller-supplied message id; collisions deduplicate like idempotency keys.
- **`InboxAcceptMetadata.Idempotency` / `OutboxEnqueueMetadata.Idempotency`**: Per-message idempotency variant on writer metadata records.
- **`InboxAcceptOutcome.AlreadyAccepted`**: Receipt outcome when the store returned an existing row instead of inserting.
- **`OutboxEnqueueOutcome.AlreadyEnqueued`**: Receipt outcome when the outbox store returned an existing row instead of inserting.

### Writer Invocation

- Set idempotency on **`InboxAcceptItem`** / **`OutboxEnqueueItem`** metadata, or use **`InboxAcceptItem<T>.WithIdempotency(message, key)`** / **`OutboxEnqueueItem<T>.WithIdempotency(message, key)`** sugar helpers.
- Combine with **`TenantScope.Isolated(tenantId)`** so keys deduplicate per tenant, not globally.
- Read **`InboxReceipt.Outcome`** or **`OutboxReceipt.Outcome`** to distinguish the first append from a duplicate.

### Ingress Defaults

- When **`TransportInboxIngressOptions.Safety.RequireStableIdentity`** is true (default), ingress maps broker delivery id to **`MessageIdentity.Supplied`** and **`Idempotency.Keyed($"ingress:{destination}:{brokerMessageId}")`** unless **`Safety.TrustApplicationHeaders`** allows a **`litebus-idempotency-key`** override.
- Missing broker id with **`RequireStableIdentity`** throws before store write.

### Store and Transactional Behavior

- Non-transactional **`IInbox`** / **`IOutbox`**: duplicate key or message ID returns the existing envelope with **`AlreadyAccepted`** or **`AlreadyEnqueued`**.
- Transactional EF path: `ReturnExisting` resolves a tracked, pending, or persisted duplicate before staging. `Strict` stages the conflicting envelope so **`SaveChanges`** rejects the unit of work.
- All built-in stores enforce tenant-scoped unique constraints on idempotency keys.

### Extension Points

- Custom stores implement append-role dedup in **`AddAsync`** / **`AddBatchAsync`** and return **`InboxAppendResult`** / **`OutboxAppendResult`** for each input slot (storage axis).
- Application HTTP layers map client **`Idempotency-Key`** headers to **`Idempotency.Keyed`** (application-owned; no framework middleware).

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Messaging.Abstractions` | `Idempotency`, `MessageIdentity`, `IdempotencyConflictMode` |
| `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions` | Metadata on accept and enqueue items |
| `LiteBus.Inbox.Ingress` | Broker-scoped ingress keys |
| All storage packages | Unique constraints and upsert behavior |

## Requires

- Explicit keyed idempotency for application-level dedup (not automatic)
- Tenant scope when isolating keys per tenant
- Idempotent handlers for execution and publication duplicates

## Invariants

- Idempotency keys scoped per tenant; different tenants may reuse the same key string
- Null or whitespace tenant normalizes to empty string
- Completed inbox rows remain deduplicated on repeat accept with same key
- Idempotency keys live in inbox and outbox metadata
- Transactional EF: duplicate key fails entire `SaveChanges` (no silent dedup)

## Non-Goals

- Handler-level idempotency record store (planned)
- Automatic idempotency middleware in the mediator pipeline
- Cross-service deduplication without application keys

## Observability

No dedicated idempotency meter. Duplicate accepts are success paths, not errors.

### Writer and Receipt Path

- **Kind**: Return value only (no counter)
- **When emitted**: Store returns existing row for duplicate message id or tenant-scoped idempotency key
- **Signal**: **`InboxAcceptOutcome.AlreadyAccepted`** on **`InboxReceipt`**
- **Operational note**: Do not alert on **`AlreadyAccepted`**; rising rate may indicate client retries or ingress redelivery (expected when idempotency is configured)

### Queue Depth (Indirect)

- **Instrument**: `litebus.inbox.queue.depth` / `litebus.outbox.queue.depth` (`LiteBusInboxTelemetry.QueueDepthInstrumentName`, `LiteBusOutboxTelemetry.QueueDepthInstrumentName`)
- **Kind**: Observable gauge tagged by `litebus.inbox.status` / `litebus.outbox.status`
- **When emitted**: Periodic scrape after processor registration
- **Operational note**: Ingress dedup prevents duplicate pending rows; depth stays flat on broker redelivery

### Ingress Redelivery Absorption

- **Instrument**: `ingress.ack_failed_after_accept` (`LiteBusInboxIngressTelemetry.AckFailedAfterAcceptInstrumentName`)
- **Kind**: Counter on meter `LiteBus.Inbox`
- **When incremented**: Broker ack fails after store accept succeeded; redelivery hits idempotent accept
- **Registration**: `AddLiteBusInboxMetrics()` from `LiteBus.Inbox.Extensions.OpenTelemetry`
- **Operational note**: Pair with logs EventId 3004 (`AckFailedAfterAccept`); redelivery should not create a second row

### Strict Mode and Transactional Failures

- **Kind**: Exception + failed transaction (no idempotency-specific metric)
- **When emitted**: **`IdempotencyConflictMode.Strict`** conflict, or EF duplicate key on transactional accept
- **Operational note**: Alert on sustained strict-conflict or SaveChanges abort rate at the writer boundary

## Deep Docs

- [Inbox](../../reliable-messaging/inbox.md) (idempotency, ingress defaults)
- [Reliable messaging semantics](../../reliable-messaging/semantics.md)
- [Roadmap](../../roadmap/README.md) (handler-level idempotency store)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Outbox.UnitTests`
- `LiteBus.Enterprise.UnitTests`
- `LiteBus.Durable.IntegrationTests`
- `LiteBus.Storage.IntegrationTests`
- `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

### Covered Use Cases

#### `InboxTests.AcceptAsync_WhenIdempotencyKeyMatchesExisting_ShouldReturnExistingReceipt`

- **Use case**: When the same idempotency key is accepted twice on the non-transactional writer path, the existing receipt is returned
- **Test kind**: Unit
- **Description**: Two accepts with **`Idempotency.Keyed(...)`** on the same tenant
- **Behavior**: Second **`AcceptAsync`** with duplicate key
- **Expected outcome**: Existing receipt returned; no duplicate row
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxAcceptOutcomeTests.AcceptAsync_WhenDuplicateIdempotencyKey_ShouldReturnAlreadyAcceptedOutcome`

- **Use case**: When a duplicate idempotency key is accepted, the receipt reports **`AlreadyAccepted`**
- **Test kind**: Unit
- **Description**: Enterprise writer configuration with outcome enum assertion
- **Behavior**: Second accept with same keyed idempotency
- **Expected outcome**: **`InboxAcceptOutcome.AlreadyAccepted`**
- **Remarks**: `LiteBus.Enterprise.UnitTests`

#### `InboxStoreContractTests.AddAsync_SameIdempotencyKeyDifferentTenants_ShouldPersistBoth`

- **Use case**: When the same idempotency key string is used under different tenants, both rows persist independently
- **Test kind**: Contract
- **Description**: Store append with tenant-scoped keys
- **Behavior**: Two **`AddAsync`** calls, different **`TenantScope`**, same key string
- **Expected outcome**: Two independent rows
- **Remarks**: Store contract suite across in-memory and adapter backends

#### `InboxStoreContractTests.AddAsync_SameTenantSameIdempotencyKey_ShouldDedup`

- **Use case**: When the same tenant and idempotency key collide at the store, only one row exists
- **Test kind**: Contract
- **Description**: Tenant isolation within a single partition
- **Behavior**: Second insert with same tenant-scoped key
- **Expected outcome**: Existing row returned; single row in store
- **Remarks**: Store contract suite

#### `OutboxStoreContractTests.AddAsync_ShouldReturnExistingMessageForDuplicateIdempotencyKey`

- **Use case**: When outbox enqueue repeats the same idempotency key, the store returns the existing envelope
- **Test kind**: Contract
- **Description**: Outbox append dedup at store layer
- **Behavior**: Second **`AddAsync`** with duplicate keyed idempotency
- **Expected outcome**: Existing outbox envelope returned
- **Remarks**: Store contract suite

#### `TransportInboxIngressHandlerTests.AcceptAsync_WhenBrokerRedelivers_ShouldNotCreateDuplicateRows`

- **Use case**: When a broker redelivers the same delivery id, ingress accept does not create a second inbox row
- **Test kind**: Unit
- **Description**: Ingress handler with broker-scoped idempotency key
- **Behavior**: Two accept calls simulating redelivery with same broker message id
- **Expected outcome**: Single row in store
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `InMemoryIngressIdempotencyIntegrationTests.DuplicateMessageId_ShouldCreateSingleInboxRow`

- **Use case**: When in-memory ingress receives duplicate broker message ids end-to-end, only one inbox row is created
- **Test kind**: Integration
- **Description**: Full ingress module with in-memory transport
- **Behavior**: Publish same delivery twice through ingress
- **Expected outcome**: Single inbox row
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`)

#### `InMemoryIngressIdempotencyIntegrationTests.DuplicateMessageId_ShouldCreateSingleInboxRow` (Kafka Broker)

- **Use case**: When Kafka redelivers the same message id, ingress deduplication prevents duplicate rows
- **Test kind**: Integration (broker-neutral ingress module; Kafka Docker suite does not duplicate this scenario)
- **Description**: In-memory ingress path exercises the same accept deduplication as broker ingress
- **Behavior**: Duplicate publish with stable message id
- **Expected outcome**: Single inbox row
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`)

#### `AwsSqsIngressIdempotencyIntegrationTests.DuplicateMessageId_ShouldCreateSingleInboxRow`

- **Use case**: When SQS redelivers the same message, ingress deduplication prevents duplicate rows
- **Test kind**: Integration
- **Description**: AWS SQS ingress path
- **Behavior**: Duplicate delivery with same broker id
- **Expected outcome**: Single inbox row
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/AwsSqs/`)

#### `TransportInboxIngressMapperTests.ToInboxAcceptMetadata_ShouldDefaultToBrokerScopedIdempotency`

- **Use case**: When ingress maps a delivery with stable broker id, the default idempotency key is broker-scoped
- **Test kind**: Unit
- **Description**: Mapper with default **`RequireStableIdentity`**
- **Behavior**: **`ToInboxAcceptMetadata`** on transport message
- **Expected outcome**: **`Idempotency.Keyed`** with pattern `ingress:{destination}:{id}`
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `EfCoreInboxSaveChangesInterceptorAbortTests.SaveChangesAsync_WhenDuplicateIdempotencyKeyConflicts_ShouldAbortTransaction`

- **Use case**: When transactional EF accept hits a duplicate idempotency key, the entire unit of work aborts
- **Test kind**: Integration
- **Description**: EF Core transactional inbox with conflicting duplicate
- **Behavior**: **`SaveChangesAsync`** after duplicate keyed accept in same transaction
- **Expected outcome**: Transaction fails; silent dedup not applied
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `InboxAcceptOutcomeTests.AcceptAsync_WhenStrictIdempotencyConflicts_ShouldThrow`

- **Use case**: When strict idempotency mode detects a duplicate, accept throws instead of returning **`AlreadyAccepted`**
- **Test kind**: Unit
- **Description**: **`IdempotencyConflictMode.Strict`** on keyed metadata
- **Behavior**: Second accept with conflicting duplicate
- **Expected outcome**: Exception thrown
- **Remarks**: `LiteBus.Enterprise.UnitTests`

#### `InboxStoreContractTests.AddBatchAsync_ShouldReturnExistingRowForDuplicateIdempotencyKey`

- **Use case**: When a batch accept contains a duplicate idempotency key, the store returns the existing row for that entry
- **Test kind**: Contract
- **Description**: Batch append dedup semantics
- **Behavior**: **`AddBatchAsync`** with duplicate keyed item in batch
- **Expected outcome**: Existing row in batch result for duplicate entry
- **Remarks**: Store contract suite

#### `PostgreSqlReliableMessagingEndToEndTests.DuplicateBrokerDelivery_ShouldExecuteHandlerOnce`

- **Use case**: When a broker redelivers after successful accept, deduplication limits handler execution to one call
- **Test kind**: Integration
- **Description**: PostgreSQL store plus ingress redelivery simulation
- **Behavior**: Duplicate broker delivery through accept dedup then processor pass
- **Expected outcome**: Handler invoked once
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)`

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Handler-level idempotency store | No (planned) | N/A |: |: |
| Automatic mediator idempotency middleware | No | Planned capability | Unit | Medium |
| Client HTTP Idempotency-Key header mapping | Yes | Application-owned; no framework test |: | Low |

### Out-of-Scope Use Cases

- Handler-side business deduplication tables
- Cross-service deduplication without application keys
- Processing-time exactly-once (accept dedup only)
