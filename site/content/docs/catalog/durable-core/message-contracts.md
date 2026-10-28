# Durable Message Contracts

**ID:** `durable-core.message-contracts`
**Maturity:** GA

## Summary

A durable message contract gives every persisted envelope a stable name and version, so storage, dispatch, and cross-service evolution do not depend on CLR type names alone.

## What It Does

Module builders register each message type in `IMessageContractRegistry` with an explicit name and version. They can also apply `[MessageContract]` to message types and call `RegisterFromAssembly` to register them in bulk. Writers resolve the contract when they accept or enqueue a message, and dispatchers deserialize the payload with the contract metadata stored on the envelope.

Each closed generic shape needs its own registration entry. Analyzers check that handled message types in production code have registration.

## Public Surface

### Consumer Contracts

- **`IMessageContractRegistry`**: The runtime singleton for contract resolution and compose-time registration. It implements `IContractWriter` for module builders and `IContractReader` for dispatchers and envelope factories.
- **`IContractWriter` / `IContractReader`**: These interfaces expose separate views of the registry, one for configuration-time registration and one for runtime lookup.
- **`[MessageContract("name", version)]`**: An attribute that declares the stable contract identity of a message type for assembly scans and on-demand runtime resolution.
- **`MessageContract`**: This is the resolved name, version, and CLR type that `GetContract(Type)` returns.
- **`MessageContractNotRegisteredException`**: When accept, enqueue, or factory paths cannot resolve a registered contract for the message type, they throw this exception.

### Invocation

- **`GetContract(Type messageType)`**: This is the runtime lookup that envelope factories and dispatchers use. It resolves explicit registrations only, and it throws `MessageContractNotRegisteredException` for unregistered types.
- **`TryGetContract(Type messageType)`**: This lookup does not throw. When no explicit registration exists, it falls back to `[MessageContract]` on the type.
- **`Register<TMessage>(string name, int version = 1)`**: Call this at compose time through the `Contracts` surface of the inbox or outbox module builder to register a message type.
- **`RegisterFromAssembly(Assembly assembly)`**: Call this to register every attributed message type in an assembly in one call.

When writers accept or enqueue a message, contract lookup always uses `message.GetType()`, not only the generic type parameter on typed overloads. The outbox envelope factory consults an optional `IMessageContractResolver`, registered with `OutboxModuleBuilder.UseContractResolver`. `DeclaredTypeMessageContractResolver` resolves from the declared parameter type instead of the runtime instance type. When no resolver is registered, writers use the runtime message instance type.

### Registration

- Call **`inbox.Contracts.Register<T>(name, version)`** or **`outbox.Contracts.Register<T>(name, version)`** inside the matching module builder.
- Call **`messaging.Contracts.RegisterFromAssembly(...)`** inside `AddMessaging`, or use the equivalent `Contracts` surface on an axis module builder.
- Apply **`[MessageContract]`** to message types and register them explicitly or by assembly scan, since LB1017 reports attributed types that have neither.

Register each message type before the first accept or enqueue of that type. Each closed generic shape needs its own registration entry.

### Configuration

- Each registration carries a contract **name**, the stable string stored in envelope rows, and a **version**, the positive integer used for evolution.
- There is no module-level options object. Contract identity lives on each registration or attribute.
- Payload encryption leaves the contract name and version as plaintext in storage. See the payload-encryption capability for details.

### Extension Points

- The application defines message types and chooses contract names and versions for cross-service compatibility.
- Custom storage and dispatch adapters consume the stored contract metadata. They do not define alternate registration APIs.
- Analyzer rules LB1007 and LB1017 extend compile-time coverage. LB1007 covers handled durable types without registration, and LB1017 covers attributed types without an explicit register call or assembly scan.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Messaging.Abstractions` | `IMessageContractRegistry`, `[MessageContract]` |
| `LiteBus.Messaging`, `LiteBus.Inbox`, `LiteBus.Outbox` | Module builder `Contracts` surfaces |
| `LiteBus.Analyzers` | LB1007, LB1017 contract coverage |

## Requires

- Register each message type before its first accept or enqueue.
- Give each persisted contract version a unique stable name.
- Register handlers for inbox command types.

## Invariants

- Configuration rejects open generic contract definitions.
- The contract name and version persist in envelope rows, and payload encryption leaves them as plaintext.
- Persisted envelopes outlive each deployed assembly version. Plan contract evolution carefully.
- LB1007 warns about handled durable types without registration, and LB1017 warns about attributed types without an explicit register call or assembly scan.

## Non-Goals

- This capability does not provide automatic contract migration or payload upgrader hooks. Upgrader hooks for in-flight envelopes are planned (see [Roadmap](../../roadmap/README.md)).
- This capability does not provide a JSON Schema export CLI. A contract catalog CLI with JSON Schema export is planned (see [Roadmap](../../roadmap/README.md)).
- This capability does not use CLR assembly-qualified names as persisted identity.

## Observability

Contract resolution failures surface as typed exceptions at accept, enqueue, or dispatch. There is no per-contract metric or counter.

### Contract Resolution Failures

- **Kind**: Exception (no dedicated meter)
- **When emitted**: `GetContract` fails at accept or enqueue with `MessageContractNotRegisteredException`, or dispatch cannot map a stored name and version to a registered type
- **Operational note**: Alert on sustained contract-not-registered exceptions at API, ingress, or processor boundaries. They indicate missing registration or deployment skew.

### Diagnostics Queries

- **Kind**: Management store queries (not OpenTelemetry)
- **When used**: `IInboxManager` / `IOutboxManager` and diagnostics stores filter or group by contract name where the store supports it
- **Operational note**: When investigating failed or dead-lettered rows with unknown contract metadata, use contract name filters.

### Transport Wire Mapping

- **Kind**: Envelope headers on dispatch and ingress (see the transport-envelope-mapping capability)
- **When emitted**: Dispatch maps the contract name and version to canonical transport headers, and ingress reads them before accept.
- **Operational note**: When contract headers are missing at ingress, accept fails before the store write, so no orphan rows remain.

## References

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

- **Use case**: When the envelope factory resolves a registered contract, the stored fields match the shape of the writer insert
- **Test kind**: Unit
- **Description**: In-memory inbox with explicit contract registration
- **Behavior**: `CreateAsync` on the envelope factory for a registered message type
- **Expected outcome**: The contract name and version on the envelope match the registration
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `OutboxTests.Register_ShouldRejectOpenGenericDurableContracts`

- **Use case**: When registration targets an open generic message type, configuration fails
- **Test kind**: Unit
- **Description**: Outbox module builder contract registration
- **Behavior**: `Contracts.Register` called on an open generic definition
- **Expected outcome**: Configuration fails because the open generic is rejected.
- **Remarks**: `LiteBus.Outbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.AcceptAsync_WhenContractNotRegistered_ShouldThrowMessageContractNotRegisteredException`

- **Use case**: When accept runs without contract registration, resolution fails before the store write
- **Test kind**: Unit
- **Description**: Accept of an unregistered message type
- **Behavior**: `IInbox.AcceptAsync` called without a prior `Contracts.Register`
- **Expected outcome**: `MessageContractNotRegisteredException`
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `InboxProcessorEdgeCaseTests.ProcessPendingAsync_WhenContractNameUnknown_ShouldMarkFailed`

- **Use case**: When a stored row references an unknown contract name, the processor marks it failed
- **Test kind**: Unit
- **Description**: In-memory store row with unresolvable contract name
- **Behavior**: The dispatch and deserialize path of `ProcessPendingAsync`
- **Expected outcome**: The row transitions to failed status
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `EfCoreInboxProcessorUnknownContractEndToEndTests.ProcessPendingAsync_WhenContractIsUnknown_ShouldMarkFailedInDatabase`

- **Use case**: When EF-backed storage holds an unknown contract, the processor records the failure in the database
- **Test kind**: Integration
- **Description**: EF Core inbox store with invalid contract metadata on the row
- **Behavior**: A full processor pass through the EF store
- **Expected outcome**: The row has failed status in the database
- **Remarks**: `LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests`

#### `AmqpInboxIngressFailureTests.UnknownContract_ShouldNackWithoutRequeueAndSkipStore`

- **Use case**: When ingress receives a payload with an unregistered contract, it writes no row and nacks the broker message
- **Test kind**: Integration
- **Description**: AMQP ingress with unregistered contract header
- **Behavior**: Ingress accept path before store append
- **Expected outcome**: No inbox row is written, and the message is nacked without requeue.
- **Remarks**: `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`)

#### `CommandWithResultScheduledToInboxAnalyzerTests.ExplicitGenericAcceptAsyncWithCommandResult_ProducesDiagnostic`

- **Use case**: When a command-with-result type is accepted to the inbox, analyzer LB1004 reports a diagnostic
- **Test kind**: Analyzer
- **Description**: Source code that calls `AcceptAsync` on an `ICommand<TResult>` type
- **Behavior**: Analyzer compilation pass
- **Expected outcome**: The analyzer emits an LB1004 diagnostic
- **Remarks**: `LiteBus.Analyzers.Tests`

#### `MissingMessageContractRegistrationAnalyzerTests` (Suite)

- **Use case**: When a handled durable message type lacks contract registration, analyzer LB1007 reports a diagnostic
- **Test kind**: Analyzer
- **Description**: Multiple cases for inbox and outbox handlers without `Contracts.Register` or an assembly scan
- **Behavior**: Analyzer compilation pass on handler registrations
- **Expected outcome**: The analyzer emits an LB1007 diagnostic per uncovered type
- **Remarks**: `LiteBus.Analyzers.Tests`, with multiple test methods in the file

#### `ExplicitMessageContractRegistrationAnalyzerTests` (Suite)

- **Use case**: When a type carries `[MessageContract]` but is not registered or scanned, analyzer LB1017 reports a diagnostic
- **Test kind**: Analyzer
- **Description**: Attributed message type without matching register invocation
- **Behavior**: Analyzer matches only `IContractWriter` / `IMessageContractRegistry` `Register` calls
- **Expected outcome**: The analyzer emits an LB1017 diagnostic
- **Remarks**: `LiteBus.Analyzers.Tests`

#### `InboxTransportEnvelopeMapperTests.BuildHeaders_ShouldMapAllMetadataFields`

- **Use case**: When dispatch maps an inbox envelope to transport headers, the contract fields appear on the wire
- **Test kind**: Unit
- **Description**: Transport envelope mapper for inbox dispatch
- **Behavior**: `BuildHeaders` called with registered contract metadata
- **Expected outcome**: The contract name and version appear in the canonical headers
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Dispatch/`)

#### `ContractVersionEvolutionTests.ProcessPendingAsync_WithTwoContractVersions_ShouldDispatchEachRegisteredShape`

- **Use case**: The processor handles two versions of one stable contract name during a rolling contract transition
- **Test kind**: Component integration
- **Description**: The test registers version 1 and version 2 against distinct CLR command shapes, then accepts both into one inbox
- **Behavior**: The processor resolves each stored name-version pair, deserializes the matching shape, and invokes its handler
- **Expected outcome**: Both handlers run, and both envelopes complete while retaining contract versions 1 and 2.
- **Remarks**: `LiteBus.Inbox.UnitTests`

#### `AmqpInboxIngressHandlerTests.AcceptAsync_WhenContractHeaderMissing_ShouldThrow`

- **Use case**: When ingress cannot resolve the contract from broker headers, accept fails before the store write
- **Test kind**: Unit
- **Description**: AMQP ingress handler with missing contract header
- **Behavior**: Ingress contract resolution at the accept boundary
- **Expected outcome**: Accept fails before the store append.
- **Remarks**: `LiteBus.Inbox.UnitTests` (`Ingress/Amqp/`)

### Untested Use Cases

| Use case | Supported? | Gap | Suggested test kind | Priority |
| --- | --- | --- | --- | --- |
| `RegisterFromAssembly` discovers all assembly types | Yes | Covered indirectly by analyzer tests | Analyzer | Low |
| Contract name collision across assemblies | Yes | No durable-specific collision test | Unit | Low |

### Out-of-Scope Use Cases

- Automatic payload upgrader hooks
- JSON Schema export CLI
- CLR assembly-qualified names as persisted identity
