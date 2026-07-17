# Roadmap

LiteBus stays an in-process mediator for application-layer work. The command, query, and event APIs remain separate because they model different CQS concepts.

## v6 Reliability Platform (Implemented)

v6 restructures durable messaging into orthogonal packages and completes neutral inbox/outbox naming.

### Core Orchestration

- `IInbox.AcceptAsync` stores messages for later execution and returns `InboxReceipt<T>`.
- `IOutbox.EnqueueAsync` stores events for later publication and returns `OutboxReceipt<T>`.
- `ICommandMediator.SendAsync` always executes immediately in process.
- `IEventMediator.PublishAsync` always publishes to in-process handlers immediately.
- Core inbox and outbox modules register writers and processors only. Dispatchers are explicit.
- Processor background services validate that exactly one `IInboxDispatcher` or `IOutboxDispatcher` is registered.

### Storage Adapters (Implemented)

| Package | Role |
| --- | --- |
| `LiteBus.Storage.PostgreSql` | Shared PostgreSQL schema helpers |
| `LiteBus.Storage.EntityFrameworkCore` | Shared EF Core relational leasing SQL and model helpers |
| `LiteBus.Inbox.Storage.PostgreSql` | Npgsql inbox store |
| `LiteBus.Outbox.Storage.PostgreSql` | Npgsql outbox store |
| `LiteBus.Inbox.Storage.EntityFrameworkCore` | EF Core inbox store ([docs](../integrations/inbox-ef-core-storage.md)) |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | EF Core outbox store ([docs](../integrations/outbox-ef-core-storage.md)) |
| `LiteBus.Inbox.Storage.InMemory` | In-memory inbox store for tests |
| `LiteBus.Outbox.Storage.InMemory` | In-memory outbox store for tests |

PostgreSQL schema helpers support migration-owned DDL (`GetCreateScript`), explicit bootstrap (`EnsureAsync`), opt-in host bootstrap (`EnableSchemaInitialization` plus `EnsureSchemaCreationOnStartup`), version metadata, advisory-locked creation, and `ValidateAsync` (after ensure on the host, or standalone in deploy jobs). EF Core stores use application migrations; see the EF storage docs linked above.

### Dispatch Adapters (Implemented)

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch.InProcess` | Default inbox dispatcher through `ICommandMediator` |
| `LiteBus.Outbox.Dispatch.InProcess` | In-process outbox dispatcher through `IEventMediator` |
| `LiteBus.Inbox.Dispatch` | Shared transport inbox dispatcher and envelope mapping |
| `LiteBus.Inbox.Dispatch.Amqp` | `UseAmqpDispatch` |
| `LiteBus.Inbox.Dispatch.AzureServiceBus` | `UseAzureServiceBusDispatch` |
| `LiteBus.Inbox.Dispatch.AwsSqs` | `UseAwsSqsDispatch` |
| `LiteBus.Inbox.Dispatch.Kafka` | `UseKafkaDispatch` |
| `LiteBus.Inbox.Dispatch.InMemory` | `UseInMemoryDispatch` |
| `LiteBus.Outbox.Dispatch` | Shared transport outbox dispatcher and envelope mapping |
| `LiteBus.Outbox.Dispatch.Amqp` | `UseAmqpDispatch` |
| `LiteBus.Outbox.Dispatch.AzureServiceBus` | `UseAzureServiceBusDispatch` |
| `LiteBus.Outbox.Dispatch.AwsSqs` | `UseAwsSqsDispatch` |
| `LiteBus.Outbox.Dispatch.Kafka` | `UseKafkaDispatch` |
| `LiteBus.Outbox.Dispatch.InMemory` | `UseInMemoryDispatch` |

RabbitMQ and LavinMQ use the same AMQP 0.9.1 implementations through alias registration extensions.

### Transport Platform (Implemented)

| Package | Role |
| --- | --- |
| `LiteBus.Transport.Abstractions` | `IMessageTransport`, `IMessageConsumer`, `ITenantRoutingStrategy`, transport headers, `TransportHeaderValues` |
| `LiteBus.Transport` | Shared circuit breaker metrics, consumer handler invoker, publish failure policy |
| `LiteBus.Transport.Amqp` | RabbitMQ adapter |
| `LiteBus.Transport.AzureServiceBus` | Azure Service Bus adapter |
| `LiteBus.Transport.AwsSqs` | Amazon SQS adapter |
| `LiteBus.Transport.Kafka` | Apache Kafka (Confluent) adapter |
| `LiteBus.Transport.InMemory` | Channel-based transport for tests and CI |
| `LiteBus.Inbox.Ingress` | Generic transport ingress into `IInbox` |

Broker dispatch packages (`*.Dispatch.Amqp`, `*.Dispatch.InMemory`) register the matching transport module with the shared dispatcher. Register one transport module per process.

### Ingress Adapters (Implemented)

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Ingress.Amqp` | `UseAmqpIngress` |
| `LiteBus.Inbox.Ingress.AzureServiceBus` | `UseAzureServiceBusIngress` |
| `LiteBus.Inbox.Ingress.AwsSqs` | `UseAwsSqsIngress` |
| `LiteBus.Inbox.Ingress.Kafka` | `UseKafkaIngress` |
| `LiteBus.Inbox.Ingress.InMemory` | `UseInMemoryIngress` |
| `LiteBus.Inbox.Ingress` | Shared `TransportInboxIngressHandler` and `TransportInboxIngressConsumer` |
| `LiteBus.Transport.Amqp` | Shared connection, publish, and consume primitives |

### Developer Tooling (Implemented)

| Package | Role |
| --- | --- |
| `LiteBus.Analyzers` | Roslyn rules for duplicate handlers, query impurity, missing contracts, inbox misuse, processor/dispatcher configuration, transactional EF setup, and attributed contract registration |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | ASP.NET Core `IHealthCheck` adapter for probes registered on `LiteBusHostManifest` (`AddLiteBus` on `IHealthChecksBuilder`) |

Analyzer rules include LB1001 through LB1017 (LB1002 is intentionally unused). See [Analyzers](../reference/analyzers.md) for the full inventory (duplicate handlers, missing handlers, query impurity, inbox result commands, contract registration, orphan tags, transactional storage and dispatcher configuration, LB1014 processor-without-dispatcher, LB1015-LB1016 transactional EF/interceptor guards, LB1017 attributed contract registration).

### Saga Orchestration (Implemented)

| Package | Role |
| --- | --- |
| `LiteBus.Saga.Abstractions` | `ISagaStore`, `ISagaContext`, `SagaCorrelation`, `SagaSaveItem` |
| `LiteBus.Saga.InboxIntegration` | `EnableSaga()`, `SagaProcessorHook`, in-memory store default |
| `LiteBus.Saga.Storage.PostgreSql` | PostgreSQL saga instance persistence |

Call `EnableSaga()` on `InboxModuleBuilder` to register the inbox processor hook that loads and persists saga state around dispatch. Map saga definition identifiers and contracts in the optional configure callback. Use `UsePostgreSqlSagaStorage()` when PostgreSQL persistence is required.

### Payload Encryption and Tenant Isolation (Implemented)

- `IPayloadEncryptor` and `UsePayloadEncryption()` on inbox and outbox module builders encrypt payloads at rest while contract name and version stay plaintext.
- `ITenantRoutingStrategy.ResolveRoute` on transport dispatch adapters computes publish routes from tenant metadata. Inbox and outbox processor options own tenant lease filters.
- `InboxAcceptMetadata.Tenant`, `OutboxEnqueueMetadata.Tenant`, and processor `TenantId` scope lease queries in storage implementations.

## Domain Events and Unit of Work

LiteBus can publish POCO events, so domain models do not need a LiteBus dependency. The recommended pattern is documented in [Domain Events and Unit of Work](../concepts/domain-events-and-unit-of-work.md): aggregates collect domain events, the application layer drains them near the transaction boundary, and `IOutbox.EnqueueAsync` stores cross-process notifications in the same database transaction as the state change.

Command handlers own state changes. Query handlers should not send commands, publish events, or write through repositories. Event handlers should be idempotent when retries or durability are part of the application.

## Idempotency

At-least-once processing requires duplicate-safe handlers. Applications supply idempotency keys through `InboxAcceptMetadata.Idempotency` for acceptance-level deduplication. Event handlers that update projections or call external systems should use a similar local key.

The v5 `IIdempotentCommand` marker was removed. Idempotency keys belong in `InboxAcceptMetadata.Idempotency` or application logic, not command type hierarchies.

## Future Work

These items extend the v6 reliability platform. Scope is CQRS infrastructure only; LiteBus does not ship consumer domain types, aggregates, or application-specific business LiteBus.

### Storage and Persistence

- **Deep storage dedup (`RelationalMessageStore<T>`)**: collapse duplicated inbox/outbox PostgreSQL and EF Core store logic into shared relational cores under `LiteBus.Storage.* /Stores/`; thin axis facades only. High priority for long-term maintenance. See [Roadmap: deep storage dedup](storage-deduplication.md).
- `LiteBus.Inbox.Storage.SqlServer` and `LiteBus.Outbox.Storage.SqlServer` with the same schema helper and migration-owned DDL model as PostgreSQL.
- Contract migration and payload upgrader hooks for evolving envelope contracts without breaking in-flight messages.
- Optional idempotency record store abstraction for handler-level deduplication beyond inbox accept metadata.

### Dispatch and Ingress

- `LiteBus.Inbox.Dispatch.InProcess` for event replay through `IEventMediator` when inbox envelopes target in-process event handlers.
- Webhook and gRPC outbox dispatch adapters for HTTP callbacks and RPC publication targets.
- HTTP webhook ingress adapter that writes accepted payloads to `IInbox`.

### Mediator Pipeline (Application Layer Hooks)

- `LiteBus.Validation` FluentValidation bridge for command and query pre-execution validation.
- `LiteBus.Authorization` pipeline behaviors for permission checks before handler execution.
- `LiteBus.Caching` for query result caching with explicit invalidation hooks.
- Ambient transaction and unit-of-work hook interfaces only (no ORM coupling; applications wire EF Core or other providers).

### Observability

- Additional OpenTelemetry activities on dispatch and ingress paths beyond the stable meters documented in [Architecture](../architecture/README.md).
- Storage and broker probes beyond manifest diagnostics remain application-owned via `AddDiagnosticCheck<T>()` on module builders; map results through `LiteBus.Extensions.Diagnostics.HealthChecks` or custom `IHealthCheck` wiring.

### Developer Experience

- Nested or guarded module registration on `IModuleRegistry` (for example `EnableOutboxProcessor` only when `AddOutboxModule` ran first, or inbox dispatch extensions that require inbox storage).
- Flat `ILiteBusBuilder` helpers (`RegisterFromAssembly`, `UsePostgreSqlInbox`, `UsePostgreSqlOutbox`) that bypass nested module builders. Deferred; apps install only the packages they need and call the matching `AddInboxModule` / `AddOutboxModule` extensions in order.
- Source generators and AOT-friendly registration as an opt-in alternative to reflection-based discovery.
- Contract catalog CLI and JSON Schema export for envelope contract documentation and CI validation.
- Additional analyzers for registration guardrails not yet covered (LB1014 already reports processor enabled without a dispatcher).

## Compatibility

Reflection-based registration remains the default developer path. AOT and trimming work should stay opt-in and linker-friendly.

## Next

Read [Migration Guide v6](../migration/v6.md) to upgrade from v5, or [Contributing](../contributing/README.md) to help with future packages.
