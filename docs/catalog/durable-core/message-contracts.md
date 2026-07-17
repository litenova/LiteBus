# Durable Message Contracts

**ID:** `durable-core.message-contracts`
**Maturity:** GA

## Summary

Every persisted envelope carries a stable contract name and version so storage, dispatch, and cross-service evolution do not depend on CLR type names alone.

## What It Does

Module builders register message types in `IMessageContractRegistry` with explicit name and version, or apply `[MessageContract]` and call `RegisterFromAssembly`. Writers resolve contracts at accept/enqueue time; dispatchers deserialize using stored contract metadata. Closed generic types require per-closed-shape registration. Analyzers enforce registration coverage in production code.

## Public Surface

### Consumer Contracts

- **`IMessageContractRegistry`**: Runtime singleton for contract resolution and compose-time registration. Implements `IContractWriter` (module builders) and `IContractReader` (dispatchers, envelope factories).
- **`IContractWriter` / `IContractReader`**: Split views of the registry for configuration vs runtime lookup.
- **`[MessageContract("name", version)]`**: Declares stable contract identity on message types for assembly scan and runtime on-demand resolution.
- **`MessageContractDescriptor`**: Resolved name, version, and CLR type returned from `GetContract(Type)`.
- **`MessageContractNotRegisteredException`**: Thrown when accept/enqueue or factory paths cannot resolve a registered contract for the message type.

### Invocation

- **`GetContract(Type messageType)`**: Runtime lookup used by envelope factories and dispatchers. Uses explicit registration first, then `[MessageContract]` on the type.
- **`Register<TMessage>(string name, int version = 1)`**: Compose-time registration on inbox or outbox module builder `Contracts` surface.
- **`RegisterFromAssembly(Assembly assembly)`**: Batch registration of attributed types from an assembly.

Contract lookup at accept/enqueue always uses `message.GetType()`, not only the generic type parameter on typed overloads.

### Registration

- **`inbox.Contracts.Register<T>(name, version)`** or **`outbox.Contracts.Register<T>(name, version)`** inside the matching module builder.
- **`builder.Contracts.RegisterFromAssembly(...)`** on shared contract builder or axis module builder.
- **`[MessageContract]`** on message types plus either explicit register or assembly scan (LB1017 enforces attributed types are registered).

Register before first accept/enqueue of each message type. Closed generic shapes each need their own registration entry.

### Configuration

- Per-type: contract **name** (stable string stored in envelope rows) and **version** (positive integer for evolution).
- No module-level options bag; contract identity lives on each registration or attribute.
- Payload encryption leaves contract name and version plaintext in storage (see payload-encryption capability).

### Extension Points

- Application defines message types and chooses contract names/versions for cross-service compatibility.
- Custom storage and dispatch adapters consume stored contract metadata; they do not define alternate registration APIs.
- Analyzer rules LB1007 (handled durable types without registration) and LB1017 (attributed types without register/scan) extend compile-time coverage.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Messaging.Abstractions` | `IMessageContractRegistry`, `[MessageContract]` |
| `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions` | Module builder `Contracts` surface |
| `LiteBus.Analyzers` | LB1007, LB1017 contract coverage |

## Requires

- Registration before first accept/enqueue of each message type
- Unique stable names per persisted contract version
- Handler registration for inbox command types

## Invariants

- Open generic contract definitions are rejected
- Contract name and version persist in envelope rows; payload encryption leaves them plaintext
- Persisted envelopes outlive deploying assembly versions; plan contract evolution carefully
- LB1007 warns handled durable types without registration; LB1017 warns attributed types without explicit register or assembly scan

## Non-Goals

- Automatic contract migration or payload upgrader hooks (planned)
- JSON Schema export CLI (planned)
- CLR assembly-qualified names as persisted identity

## Observability

Contract resolution failures surface as typed exceptions at accept, enqueue, or dispatch. There is no per-contract metric or counter.

### Contract Resolution Failures

- **Kind**: Exception (no dedicated meter)
- **When emitted**: `GetContract` fails at accept/enqueue (`MessageContractNotRegisteredException`) or dispatch cannot map stored name/version to a registered type
- **Operational note**: Alert on sustained contract-not-registered exceptions at API, ingress, or processor boundaries; indicates missing registration or deployment skew

### Diagnostics Queries

- **Kind**: Management store queries (not OpenTelemetry)
- **When used**: `IInboxManager` / `IOutboxManager` and diagnostics stores filter or group by contract name where supported
- **Operational note**: Use contract name filters when investigating failed or dead-lettered rows with unknown contract metadata

### Transport Wire Mapping

- **Kind**: Envelope headers on dispatch/ingress (see transport-envelope-mapping capability)
- **When emitted**: Dispatch maps contract name and version to canonical transport headers; ingress reads them before accept
- **Operational note**: Missing contract headers at ingress fail before store write (no orphan rows)

## Deep Docs

- [Inbox](../../reliable-messaging/inbox.md) (contract section)
- [Analyzers](../../reference/analyzers.md)
- [Architecture](../../architecture/README.md) (contract registration)

## Test Coverage

### Consolidated Test Projects

- `LiteBus.Inbox.UnitTests`
- `LiteBus.Outbox.UnitTests`
- `LiteBus.Durable.IntegrationTests`
- `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`
- `LiteBus.Analyzers.Tests`

### Covered Use Cases

#### `InboxEnvelopeFactoryTests.CreateAsync_should_match_inbox_writer_fields`

- **Use case**: When the envelope factory resolves a registered contract, stored fields match the writer insert shape
- **Test kind**: Unit
- **Description**: In-memory inbox with explicit contract registration
- **Behavior**: `CreateAsync` on envelope factory for a registered message type
- **Expected outcome**: Contract name and version on envelope match registration
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `OutboxTests.Register_ShouldRejectOpenGenericDurableContracts`

- **Use case**: When registration targets an open generic message type, configuration fails
- **Test kind**: Unit
- **Description**: Outbox module builder contract registration
- **Behavior**: `Contracts.Register` on open generic definition
- **Expected outcome**: Configuration failure; open generic rejected
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.AcceptAsync_WhenContractNotRegistered_ShouldThrowMessageContractNotRegisteredException`

- **Use case**: When accept runs without contract registration, resolution fails before store write
- **Test kind**: Unit
- **Description**: Accept of unregistered message type
- **Behavior**: `IInbox.AcceptAsync` without `Contracts.Register`
- **Expected outcome**: `MessageContractNotRegisteredException`
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxTests.ProcessPendingAsync_WhenContractNameUnknown_ShouldMarkFailed`

- **Use case**: When a stored row references an unknown contract name, the processor marks it failed
- **Test kind**: Unit
- **Description**: In-memory store row with unresolvable contract name
- **Behavior**: `ProcessPendingAsync` dispatch/deserialize path
- **Expected outcome**: Row transitions to failed status
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `EfCoreInboxProcessorUnknownContractEndToEndTests.ProcessPendingAsync_WhenContractIsUnknown_ShouldMarkFailedInDatabase`

- **Use case**: When EF-backed storage holds an unknown contract, processor failure persists in the database
- **Test kind**: Integration
- **Description**: EF Core inbox store with invalid contract metadata on row
- **Behavior**: Full processor pass through EF store
- **Expected outcome**: Failed status in database
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `AmqpInboxIngressFailureTests.UnknownContract_ShouldNackWithoutRequeueAndSkipStore`

- **Use case**: When ingress receives a payload with an unregistered contract, no row is written and the broker message is nacked
- **Test kind**: Integration
- **Description**: AMQP ingress with unregistered contract header
- **Behavior**: Ingress accept path before store append
- **Expected outcome**: No inbox row; nack without requeue
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`)

#### `CommandWithResultScheduledToInboxAnalyzerTests.ExplicitGenericAcceptAsyncWithCommandResult_ProducesDiagnostic`

- **Use case**: When a command-with-result type is accepted to inbox, analyzer LB1004 reports a diagnostic
- **Test kind**: Analyzer
- **Description**: Source with `AcceptAsync` on `ICommand<TResult>` type
- **Behavior**: Analyzer compilation pass
- **Expected outcome**: LB1004 diagnostic emitted
- **Remarks**: `LiteBus.Analyzers.Tests`

#### `MissingMessageContractRegistrationAnalyzerTests` (Suite)

- **Use case**: When a handled durable message type lacks contract registration, analyzer LB1007 reports a diagnostic
- **Test kind**: Analyzer
- **Description**: Multiple cases for inbox/outbox handlers without `Contracts.Register` or assembly scan
- **Behavior**: Analyzer compilation pass on handler registrations
- **Expected outcome**: LB1007 diagnostic per uncovered type
- **Remarks**: `LiteBus.Analyzers.Tests`; multiple test methods in file

#### `ExplicitMessageContractRegistrationAnalyzerTests` (Suite)

- **Use case**: When a type carries `[MessageContract]` but is not registered or scanned, analyzer LB1017 reports a diagnostic
- **Test kind**: Analyzer
- **Description**: Attributed message type without matching register invocation
- **Behavior**: Analyzer matches only `IContractWriter` / `IMessageContractRegistry` `Register` calls
- **Expected outcome**: LB1017 diagnostic
- **Remarks**: `LiteBus.Analyzers.Tests`

#### `InboxTransportEnvelopeMapperTests.BuildHeaders_ShouldMapAllMetadataFields`

- **Use case**: When dispatch maps an inbox envelope to transport headers, contract fields appear on the wire
- **Test kind**: Unit
- **Description**: Transport envelope mapper for inbox dispatch
- **Behavior**: `BuildHeaders` with registered contract metadata
- **Expected outcome**: Contract name and version in canonical headers
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Dispatch/`)

#### `ContractVersionEvolutionTests.ProcessPendingAsync_WithTwoContractVersions_ShouldDispatchEachRegisteredShape`

- **Use case**: Two versions of one stable contract name remain processable during a rolling contract transition
- **Test kind**: Component integration
- **Description**: Registers version 1 and version 2 against distinct CLR command shapes, then accepts both into one inbox
- **Behavior**: The processor resolves each stored name-version pair, deserializes the matching shape, and invokes its handler
- **Expected outcome**: Both handlers run; both envelopes complete while retaining contract versions 1 and 2
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `AmqpInboxIngressHandlerTests.AcceptAsync_WhenContractHeaderMissing_ShouldThrow`

- **Use case**: When ingress cannot resolve contract from broker headers, accept fails before store write
- **Test kind**: Unit
- **Description**: AMQP ingress handler with missing contract header
- **Behavior**: Ingress contract resolution at accept boundary
- **Expected outcome**: Exception before store append
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`)

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| `RegisterFromAssembly` discovers all assembly types | Yes | Covered indirectly in analyzer tests | Analyzer | Low |
| Contract name collision across assemblies | Yes | No durable-specific collision test | Unit | Low |

### Out-of-Scope Use Cases

- Automatic payload upgrader hooks
- JSON Schema export CLI
- CLR assembly-qualified names as persisted identity
