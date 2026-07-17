# Inbox Acceptance

**ID:** `durable-core.inbox-acceptance`
**Maturity:** GA

## Summary

Accept commands and other registered message types into durable storage for later execution, returning an acceptance receipt instead of a handler result.

## What It Does

`IInbox` serializes the message, resolves its contract name and version, and appends an `InboxEnvelope` to the store. Acceptance is durable once the store transaction commits. A background processor executes the message later through a registered dispatcher. Batch acceptance stores multiple envelopes in one store round trip.

Receipts confirm storage only. They do not contain business results from command handlers.

## Public Surface

### Consumer Contracts

- **`IInbox`**: Application-facing writer for accept operations. Inject at API edges, ingress adapters, or tools that need immediate store commit.
- **`InboxAcceptItem<TMessage>` / `InboxAcceptItem`**: Command input carrying payload plus `InboxAcceptMetadata`. Use the untyped variant when the runtime type varies in a batch.
- **`InboxAcceptMetadata`**: Per-message identity, idempotency, visibility, trace, and tenant variants. Start from `InboxAcceptMetadata.Immediate` and compose with `with`.
- **`InboxReceipt` / `InboxAcceptOutcome`**: Acceptance result (`Accepted`, `AlreadyAccepted`). Not a handler result.

### Invocation

- **`AcceptAsync<TMessage>(InboxAcceptItem<TMessage>, CancellationToken)`**: Primary typed accept with full metadata.
- **`AcceptAsync<TMessage>(TMessage, CancellationToken)`**: Body-only sugar equivalent to `InboxAcceptItem<T>.From(message)`.
- **`AcceptAsync(InboxAcceptItem, CancellationToken)`**: Runtime-type accept for heterogeneous payloads; contract lookup uses `message.GetType()`.
- **`AcceptBatchAsync(IReadOnlyList<InboxAcceptItem>, CancellationToken)`**: Batch accept in one store round trip.

Typical call sites: HTTP controllers deferring work, ingress handlers after broker mapping, operational replay tools.

### Registration

- **`AddInbox()`** with **`Use*Storage()`** registered inside the same inbox module builder (required before first accept).
- **`Contracts.Register<TMessage>(name, version)`** or **`RegisterFromAssembly`** for each stored message type.
- Default **`IInbox`** registers as singleton against the non-transactional store.

### Configuration

- Per-call: metadata on **`InboxAcceptItem`** (`MessageIdentity`, `Idempotency`, `MessageVisibility`, trace, `TenantScope`).
- Compose-time: storage adapter options on the inbox builder (see durable-storage capability); not mixed into accept item metadata.

### Extension Points

- Custom storage implements **`IInboxStore`** append role and returns **`InboxAppendResult`** so the writer can preserve insertion outcomes (storage axis).
- Ingress maps broker deliveries to **`InboxAcceptItem`** (inbox-ingress capability).
- For domain-transaction alignment use **`ITransactionalInbox`** instead of **`IInbox`** (transactional-writes capability).

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | `IInbox`, `InboxAcceptItem`, `InboxReceipt`, `InboxAppendResult` |
| `LiteBus.Inbox` | Default `Inbox` writer implementation |
| `LiteBus.Inbox.Storage.*` | Persistence (required at runtime) |

## Requires

- `AddInbox` with `Use*Storage` registered inside the same builder
- Each stored message type registered in `IMessageContractRegistry`
- Message must not be `ICommand<TResult>` (analyzer LB1004)

## Invariants

- Contract lookup always uses `message.GetType()`, not only the generic type parameter
- Open generic contract definitions are rejected
- Default `IInbox` commits immediately through the singleton store (not joined to domain transactions)
- `AlreadyAccepted` outcome returns existing row for duplicate message id or tenant-scoped idempotency key without re-executing completed handlers

## Non-Goals

- Synchronous handler execution (use `ICommandMediator.SendAsync`)
- Exactly-once side effects (handlers must be idempotent)
- Owning domain transaction boundaries (use `ITransactionalInbox` instead)

## Observability

No dedicated accept counter. Writer failures surface as store exceptions.

### `litebus.inbox.queue.depth`

- **Kind**: Observable gauge (`LiteBusInboxTelemetry.QueueDepthInstrumentName`)
- **When emitted**: Periodic observation after processor registration reads diagnostics store
- **Tags**: `litebus.inbox.status` (`LiteBusInboxTelemetry.QueueStatusAttributeName`) groups pending, processing, completed, failed, dead-lettered
- **How to enable**: `AddLiteBusInboxMetrics()` from `LiteBus.Inbox.Extensions.OpenTelemetry`
- **Operational note**: Rising pending depth with flat processing rate signals processor lag or lease contention

### Writer Path

- **Kind**: Log + exception (no accept-specific meter)
- **When emitted**: Contract resolution failure, store constraint violation, strict idempotency conflict
- **Operational note**: Alert on sustained accept exception rate at API or ingress boundary

## Deep Docs

- [Inbox](../../reliable-messaging/inbox.md)
- [Reliable messaging](../../reliable-messaging/README.md)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Inbox.Abstractions.UnitTests`
- `LiteBus.Inbox.Storage.InMemory.UnitTests`
- `LiteBus.Durable.IntegrationTests`
- `LiteBus.Storage.IntegrationTests`
- `LiteBus.Enterprise.UnitTests`

### Covered Use Cases

#### `InboxTests.AcceptAsync_ShouldStoreTypedEnvelopeAndReturnReceipt`

- **Use case**: When an application accepts a typed command, the envelope is stored and a receipt is returned
- **Test kind**: Unit
- **Description**: In-memory inbox module with registered contract and handler
- **Behavior**: `IInbox.AcceptAsync<T>` with explicit item metadata
- **Expected outcome**: Receipt contains message id; row is pending in store
- **Remarks**: Same fixture also exercises body-only sugar overload sharing the factory path

#### `InboxTests.AcceptAsync_WhenIdempotencyKeyMatchesExisting_ShouldReturnExistingReceipt`

- **Use case**: When the same idempotency key is accepted twice on the non-transactional path, the existing receipt is returned
- **Test kind**: Unit
- **Description**: Two accepts with `Idempotency.Keyed(...)` on the same tenant
- **Behavior**: Second `AcceptAsync` with duplicate key
- **Expected outcome**: `AlreadyAccepted` outcome; no duplicate row
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxTests.AcceptBatchAsync_ShouldUseRuntimeTypeNotDeclaredGenericParameter`

- **Use case**: When a heterogeneous batch is accepted, each row uses the runtime message type for contract lookup
- **Test kind**: Unit
- **Description**: Batch containing multiple concrete command types
- **Behavior**: `AcceptBatchAsync` with mixed `InboxAcceptItem` entries
- **Expected outcome**: Each stored row resolves contract from `message.GetType()`
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.AcceptAsync_WhenContractNotRegistered_ShouldThrowMessageContractNotRegisteredException`

- **Use case**: When accept runs without contract registration, resolution fails before store write
- **Test kind**: Unit
- **Description**: Accept of unregistered message type
- **Behavior**: `AcceptAsync` without `Contracts.Register`
- **Expected outcome**: `MessageContractNotRegisteredException`
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.AcceptAsync_ShouldStoreIdempotencyKeyFromMetadata`

- **Use case**: When idempotency metadata is supplied on accept, the key is persisted on the envelope row
- **Test kind**: Unit
- **Description**: Accept with `Idempotency.Keyed(...)` on item
- **Behavior**: Writer insert through default inbox
- **Expected outcome**: Idempotency key column populated
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.AcceptAsync_ShouldPersistVisibleAfterFromMetadata`

- **Use case**: When visibility metadata defers processing, `visible_after` is stored at accept time
- **Test kind**: Unit
- **Description**: Accept with `MessageVisibility.At` or `After`
- **Behavior**: Writer persist path
- **Expected outcome**: `visible_after` column set on row
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxEnvelopeFactoryTests.CreateAsync_should_match_inbox_writer_fields`

- **Use case**: When the envelope factory builds a row, fields align with the writer insert shape
- **Test kind**: Unit
- **Description**: Factory output compared to writer-produced envelope
- **Behavior**: `CreateAsync` on envelope factory
- **Expected outcome**: Contract name, version, payload, and metadata fields match
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxAcceptItemTests.From_with_message_type_should_set_contract_lookup_type`

- **Use case**: When an untyped accept item is built with an explicit message type, contract lookup uses that type
- **Test kind**: Unit
- **Description**: `InboxAcceptItem.From(messageType, ...)` construction
- **Behavior**: Contract resolution type on untyped item
- **Expected outcome**: Lookup type matches supplied runtime type
- **Remarks**: `LiteBus.Inbox.Abstractions.UnitTests`

#### `InboxAcceptMetadataTests.Immediate_with_should_override_single_concern`

- **Use case**: When metadata is composed with record `with`, only the targeted concern changes
- **Test kind**: Unit
- **Description**: `InboxAcceptMetadata.Immediate with { ... }` on one field
- **Behavior**: Metadata record copy
- **Expected outcome**: Other metadata fields unchanged
- **Remarks**: `LiteBus.Inbox.Abstractions.UnitTests`

#### `InboxCompositeModuleTests.NestedConfiguration_ShouldAcceptAndProcessCommand`

- **Use case**: When inbox is configured through nested composite modules, accept and process completes end-to-end
- **Test kind**: Unit
- **Description**: Full nested module wiring with in-process dispatch
- **Behavior**: Accept then processor pass
- **Expected outcome**: Row reaches completed status
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `TransportInboxIngressHandlerTests.AcceptAsync_ShouldWriteEnvelopeToInboxStore`

- **Use case**: When ingress maps a broker delivery to accept, a row appears in the singleton store
- **Test kind**: Unit
- **Description**: Transport ingress handler with in-memory store
- **Behavior**: Ingress `AcceptAsync` path
- **Expected outcome**: Envelope row persisted
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `TransportInboxIngressHandlerTests.AcceptBatchAsync_WithSingleMessage_ShouldWriteEnvelopeToInboxStore`

- **Use case**: When ingress batch accept receives one delivery, behavior matches single accept
- **Test kind**: Unit
- **Description**: Batch ingress path with one message
- **Behavior**: `AcceptBatchAsync` from ingress handler
- **Expected outcome**: Same store row as single accept
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/`)

#### `InboxStoreContractTests.AddAsync_ShouldReturnExistingCommandForDuplicateIdempotencyKey`

- **Use case**: When the store receives a duplicate idempotency key insert, the existing envelope is returned
- **Test kind**: Contract
- **Description**: Store-level append dedup (shared contract suite across backends)
- **Behavior**: Second `AddAsync` with same tenant-scoped key
- **Expected outcome**: Existing envelope returned without duplicate row
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests` and adapter parity tests

#### `InboxStoreContractTests.AddAsync_WhenCommandIdAlreadyExists_ShouldReturnExistingRow`

- **Use case**: When a supplied message id collides, the store returns the existing row
- **Test kind**: Contract
- **Description**: Supplied `MessageIdentity` collision at store
- **Behavior**: Duplicate message id insert
- **Expected outcome**: Existing row returned
- **Remarks**: Store contract suite

#### `InboxStoreContractTests.AddBatchAsync_ShouldReturnStoredEnvelopesInInputOrder`

- **Use case**: When multiple items are accepted in one batch, receipt order matches input order
- **Test kind**: Contract
- **Description**: Multi-item batch append
- **Behavior**: `AddBatchAsync`
- **Expected outcome**: Results ordered like input
- **Remarks**: Store contract suite

#### `InboxAcceptOutcomeTests.AcceptAsync_WhenStrictIdempotencyConflicts_ShouldThrow`

- **Use case**: When strict idempotency mode detects a conflicting duplicate, accept throws instead of returning `AlreadyAccepted`
- **Test kind**: Unit
- **Description**: Enterprise strict store mode configuration
- **Behavior**: Second accept with conflicting duplicate semantics
- **Expected outcome**: Exception thrown
- **Remarks**: `LiteBus.Enterprise.UnitTests`

#### `PostgreSqlProcessCrashIntegrationTests.AcceptedCommand_WhenWorkerProcessIsKilled_ShouldRecoverAfterLeaseExpiry`

- **Use case**: An accepted command survives termination of the first processor host
- **Test kind**: Integration
- **Description**: PostgreSQL inbox row accepted before a child worker is started and killed
- **Behavior**: The first worker terminates while the command handler holds the lease
- **Expected outcome**: The durable row remains available for replacement-worker recovery after lease expiry
- **Remarks**: The recovered row completes with `AttemptCount = 2`

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| HTTP API controller accept with client idempotency header | Yes | No ASP.NET integration test for accept endpoint | Integration | Low |
| Accept of open generic command without closed registration | No (by design) | Analyzer LB1007/LB1017 only; no runtime accept test for open generic | Unit | Low |

### Out-of-Scope Use Cases

- Synchronous `ICommandMediator.SendAsync` (mediator axis)
- Handler-level side-effect idempotency (application concern)
- SQL Server transactional accept (not shipped)
