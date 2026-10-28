# Acceptance Idempotency

**ID:** `durable-core.idempotency`
**Maturity:** GA

## Summary

Acceptance idempotency removes duplicate accept and enqueue attempts at insert time. The store compares message identity and tenant-scoped idempotency keys, and returns the existing row instead of inserting a second one.

## What It Does

Writers store an optional idempotency key with each envelope. When a duplicate `(tenant_id, idempotency_key)` pair or a duplicate `message_id` reaches the store, the store returns the existing row with `AlreadyAccepted` or `AlreadyEnqueued` instead of inserting a second row. On the transactional EF path, writers in `ReturnExisting` mode resolve the existing row before staging, while writers in `Strict` mode stage the conflicting envelope and let `SaveChanges` reject the unit of work. Ingress builds broker-scoped idempotency keys from broker delivery ids. Handlers must still keep their own side effects idempotent, because the processor delivers each message at least once.

## Public Surface

### Metadata Value Objects

- **`Idempotency.None`**: Stores no insert-time deduplication key with the envelope, so a retried call without a key can insert a duplicate row.
- **`Idempotency.Keyed(string Key, IdempotencyConflictMode ConflictMode = ReturnExisting)`**: Stores an application-defined key with the envelope. When a duplicate arrives, `ReturnExisting` returns the prior row and `Strict` throws `IdempotencyConflictException`.
- **`Idempotency.FromCommandKey(string? suffix = null, IdempotencyConflictMode conflictMode = ReturnExisting)`**: Builds keyed idempotency from the key granted to the in-flight command. It reads the `ClaimedIdempotencyKey` that the idempotency shortcut recorded in `IExecutionContext.Data`. An optional suffix lets one command give several envelopes distinct keys (`{key}:{suffix}`). Outside a keyed command mediation it throws `InvalidOperationException` instead of inventing a key, and it throws `ArgumentException` for a blank suffix.
- **`MessageIdentity.Generated`**: The writer assigns a new message id at accept or enqueue time.
- **`MessageIdentity.Supplied(Guid)`**: A caller-supplied message id. When the id collides with an existing row, the store deduplicates it the same way it deduplicates idempotency keys.
- **`InboxAcceptMetadata.Idempotency` / `OutboxEnqueueMetadata.Idempotency`**: Carries the per-message idempotency variant on writer metadata records.
- **`InboxAcceptOutcome.AlreadyAccepted`**: The receipt reports this outcome when the store returns an existing row instead of inserting.
- **`OutboxEnqueueOutcome.AlreadyEnqueued`**: The receipt reports this outcome when the outbox store returns an existing row instead of inserting.

### Writer Invocation

- Set idempotency on **`InboxAcceptItem`** / **`OutboxEnqueueItem`** metadata, or use **`InboxAcceptItem<T>.WithIdempotency(message, key)`** / **`OutboxEnqueueItem<T>.WithIdempotency(message, key)`** sugar helpers.
- Combine the key with **`TenantScope.Isolated(tenantId)`** so the store deduplicates keys within each tenant instead of across all tenants.
- Read **`InboxReceipt.Outcome`** or **`OutboxReceipt.Outcome`** to distinguish the first append from a duplicate.

### Ingress Defaults

- When **`TransportInboxIngressOptions.Safety.RequireStableIdentity`** is true (the default), ingress maps the broker delivery id to **`MessageIdentity.Supplied`** and to **`Idempotency.Keyed($"ingress:{destination}:{brokerMessageId}")`**. When **`Safety.TrustApplicationHeaders`** allows a **`litebus-idempotency-key`** override, that header value replaces the generated key.
- When the broker id is missing and **`RequireStableIdentity`** is set, the mapper throws before writing to the store.

### Store and Transactional Behavior

- On the non-transactional **`IInbox`** / **`IOutbox`** path, the store answers a duplicate key or message id with the existing envelope and **`AlreadyAccepted`** or **`AlreadyEnqueued`**.
- On the transactional EF path, a writer in `ReturnExisting` mode resolves a tracked, pending, or persisted duplicate before staging. A writer in `Strict` mode stages the conflicting envelope, so **`SaveChanges`** rejects the unit of work.
- All built-in stores enforce tenant-scoped unique constraints on idempotency keys.

### Extension Points

- A custom store deduplicates in its append role: **`AddAsync`** / **`AddBatchAsync`** return one **`InboxAppendResult`** / **`OutboxAppendResult`** per input slot (storage axis).
- Application HTTP layers map client **`Idempotency-Key`** headers to **`Idempotency.Keyed`** (application-owned, with no framework middleware).

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.DurableMessaging.Abstractions` | `Idempotency`, `MessageIdentity`, `IdempotencyConflictMode` |
| `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions` | Metadata on accept and enqueue items |
| `LiteBus.Inbox.Ingress` | Broker-scoped ingress keys |
| All storage packages | Unique constraints and upsert behavior |

## Requires

- Set keyed idempotency explicitly for application-level deduplication; the store does not deduplicate automatically.
- Set a tenant scope when keys must stay isolated per tenant.
- Write idempotent handlers, because execution and publication can still deliver duplicates.

## Invariants

- Idempotency keys are scoped per tenant, so different tenants may reuse the same key string
- A null or whitespace tenant counts as the empty string
- When an accept repeats the key of a completed inbox row, the store still returns the existing row instead of inserting
- Callers set idempotency keys on inbox and outbox metadata records
- In transactional EF `Strict` mode, a duplicate key fails the entire `SaveChanges`, with no silent deduplication. In `ReturnExisting` mode the writer resolves the duplicate before staging instead

## Non-Goals

- Mediation idempotency is a separate capability: `IdempotencyDeclaration` with `IIdempotencyStore` answers a replayed command without running the handler again (see [Idempotency](../../concepts/idempotency.md)). This page covers insert-time deduplication only
- The store cannot deduplicate across services unless the application supplies keys

## Observability

Idempotency has no dedicated meter. The store treats duplicate accepts as successful outcomes, not errors.

### Writer and Receipt Path

- **Kind**: Return value only (no counter)
- **When emitted**: The store returns the existing row for a duplicate message id or tenant-scoped idempotency key
- **Signal**: **`InboxAcceptOutcome.AlreadyAccepted`** on **`InboxReceipt`**
- **Operational note**: Do not alert on **`AlreadyAccepted`**. A rising rate points to client retries or ingress redelivery, which the idempotent accept absorbs by design

### Queue Depth (Indirect)

- **Instrument**: `litebus.inbox.queue.depth` / `litebus.outbox.queue.depth` (`LiteBusInboxTelemetry.QueueDepthInstrumentName`, `LiteBusOutboxTelemetry.QueueDepthInstrumentName`)
- **Kind**: An observable gauge tagged by `litebus.inbox.status` / `litebus.outbox.status`
- **When emitted**: A periodic scrape after processor registration
- **Operational note**: Ingress deduplication prevents duplicate pending rows, so depth stays flat on broker redelivery

### Ingress Redelivery Absorption

- **Instrument**: `ingress.ack_failed_after_accept` (`LiteBusInboxIngressTelemetry.AckFailedAfterAcceptInstrumentName`)
- **Kind**: Counter on meter `LiteBus.Inbox`
- **When incremented**: The broker ack fails after the store accept succeeded, so the broker redelivers into the idempotent accept
- **Registration**: `AddLiteBusInboxMetrics()` from `LiteBus.Inbox.Extensions.OpenTelemetry`
- **Operational note**: Correlate with EventId 3004 (`AckFailedAfterAccept`) in the logs. The redelivery must not create a second row

### Strict Mode and Transactional Failures

- **Kind**: Exception + failed transaction (no idempotency-specific metric)
- **When emitted**: A conflict in **`IdempotencyConflictMode.Strict`** mode, or an EF duplicate key on transactional accept
- **Operational note**: Alert on a sustained rate of strict conflicts or SaveChanges aborts at the writer boundary

## References

- [Inbox](../../reliable-messaging/inbox.md) (how idempotency and ingress defaults work)
- [Reliable messaging semantics](../../reliable-messaging/semantics.md)
- [Idempotency](../../concepts/idempotency.md) (mediation idempotency, including declarations, the idempotency store, and the `FromCommandKey` source)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Outbox.UnitTests`
- `LiteBus.Enterprise.UnitTests`
- `LiteBus.Durable.IntegrationTests`
- `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/`, `PostgreSql/`)

### Covered Use Cases

#### `InboxTests.AcceptAsync_WhenIdempotencyKeyMatchesExisting_ShouldReturnExistingReceipt`

- **Use case**: When the same idempotency key is accepted twice on the non-transactional writer path, the existing receipt is returned
- **Test kind**: Unit
- **Description**: Two accepts with **`Idempotency.Keyed(...)`** on the same tenant
- **Behavior**: Second **`AcceptAsync`** with duplicate key
- **Expected outcome**: Existing receipt returned, and no duplicate row
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
- **Expected outcome**: Existing row returned, and a single row in store
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
- **Test kind**: Integration (the ingress module is broker-neutral, and the Kafka Docker suite does not duplicate this scenario)
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
- **Expected outcome**: The transaction fails, and silent deduplication is not applied
- **Remarks**: `LiteBus.Storage.IntegrationTests` (`EntityFrameworkCore/Inbox/`)

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
- **Behavior**: A duplicate broker delivery through accept deduplication, then a processor pass
- **Expected outcome**: Handler invoked once
- **Remarks**: `LiteBus.Storage.IntegrationTests (`PostgreSql/`)

#### `CommandKeyIdempotencyTests.Inside_a_keyed_command_FromCommandKey_returns_the_claimed_key`

- **Use case**: When a keyed command builds durable metadata from its claim, the envelope carries the claimed key
- **Test kind**: Unit
- **Description**: `Idempotency.FromCommandKey()` inside a keyed command mediation
- **Behavior**: Key factory reads the recorded `ClaimedIdempotencyKey`
- **Expected outcome**: Keyed idempotency with the claimed key value
- **Remarks**: `LiteBus.Mediator.UnitTests`. Companion tests cover the conflict mode, the suffix form, and the throws outside a keyed mediation

#### `CommandKeyIdempotencyTests.Inside_an_undeclared_command_FromCommandKey_throws`

- **Use case**: When no idempotency key was claimed, the factory throws instead of inventing a key
- **Test kind**: Unit
- **Description**: `Idempotency.FromCommandKey()` inside a command with no idempotency declaration
- **Behavior**: Key factory with no recorded claim
- **Expected outcome**: `InvalidOperationException`, with no generated fallback that would make every delivery look distinct
- **Remarks**: `LiteBus.Mediator.UnitTests``

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| Mediation idempotency record store | Yes | Shipped as `IIdempotencyStore` and covered on the mediation axis, not here | N/A | N/A |
| Client HTTP Idempotency-Key header mapping | Yes | `WithIdempotencyKey` endpoint filter, covered on the ASP.NET Core axis, not here | N/A | N/A |

### Out-of-Scope Use Cases

- Handler-side business deduplication tables
- Cross-service deduplication without application keys
- Processing-time exactly-once (accept dedup only)
