# Changelog

All notable changes to this project will be documented in this file.

## Unreleased

### Added

- Role-based project and package dependency policy enforced by `ArchitectureDependencyPolicyTests`.
- Root `Add*Transport` composition extensions for AMQP, Kafka, AWS SQS, Azure Service Bus, and in-memory transports.
- Container-specific dispatch-scope lifecycle coverage for Autofac.
- Axis-specific append results and outbox enqueue outcomes so receipts distinguish new rows from idempotent replays.
- File-backed SQLite and MySQL 8.4 provider contract matrices for both Entity Framework Core durable stores.
- Published `LiteBus.Transport.Testing` xUnit conformance tests for third-party transport adapter authors.
- Evaluated package inventory, source-linked compiled snippets, test-symbol discovery, and semantic documentation gates.
- Shared durable-store contract cases for empty batches, mixed terminal outcomes, complete filters, dead-letter replay,
  and strict idempotency conflicts.
- Broker readiness diagnostics for Kafka, AWS SQS, and Azure Service Bus, including live emulator coverage for each
  configured target.
- Concern-specific `LiteBus.Testing.Mediation`, `LiteBus.Testing.Transport`, `LiteBus.Testing.DurableMessaging`, and
  `LiteBus.Testing.Hosting` packages.

### Changed

- Error handlers now receive `MessageErrorContext<TMessage, TResult>` plus the caller's explicit cancellation token.
  The typed context shares handled outcome and fallback result state with the mediation pipeline.
- `ILiteBusBuilder` moved to `LiteBus.Runtime.Abstractions` and now exposes only `Modules`; feature packages provide
  `AddMessaging`, `AddCommands`, `AddQueries`, `AddEvents`, `AddInbox`, and `AddOutbox`.
- `LiteBus.Orchestration.Abstractions` became `LiteBus.DurableMessaging.Abstractions`, which owns shared durable
  metadata, retry, lease, processor, and hook contracts.
- Inbox and outbox implementation services and module builders moved from abstractions into their core packages.
- Microsoft DI and Autofac adapters now own dispatch-scope creation. Missing scope composition fails, while root
  provider dispatch requires explicit `RootMessageDispatchScopeFactory` registration.
- EF Core inbox and outbox stores use adapter-owned `IDbContextFactory<TContext>` operation contexts.
- Saga storage is selected exactly once inside `EnableSaga(...)`; in-memory storage is no longer an implicit fallback.
- Module dependency validation uses composite ownership and `IRequires<TModule>` without registration markers or
  dependency-registry scans during `Build()`.
- Outbox processor option precedence is independent of configuration call order.
- PostgreSQL inbox and outbox schemas are version 3; saga schema is version 2. Validation checks required column
  types as well as columns, indexes, and metadata.
- Inbox and outbox store append methods return ordered append results containing the source-of-truth envelope and
  insertion outcome.
- SQLite EF models store durable timestamps as UTC ticks, and MySQL leasing uses `READ COMMITTED` with a named
  chronological index.
- Test coverage uses one canonical collector configuration and an exact source-line union across every CI batch.
  Pull request and release jobs enforce 90 percent line coverage and treat Codecov upload failures as failures.
- Transport publishers resolve circuit breakers by destination. Ingress recovery no longer shares publisher
  failure state, and half-open recovery admits one probe after a monotonic break duration. Opaque operation permits
  prevent late completions from resetting a newer circuit generation.
- Transport consumers now separate provider-neutral `MaxInFlightMessages` from RabbitMQ and Azure prefetch, SQS
  `ReceiveBatchSize`, and Azure `MaxConcurrentCalls`. Every ingress adapter carries the same nested `Safety` record.
  In-memory destinations now apply configurable, lossless backpressure to queued and in-flight deliveries.
- ASP.NET Core health checks and the management health route expose shared per-probe timeout and parallelism limits.
  The `AddLiteBus()` health registration carries both `litebus` and `ready` tags.
- `LiteBus.Testing` is now a framework-neutral base package. Mediator, transport, durable, and host helpers no longer
  impose unrelated dependency graphs, Newtonsoft.Json, or an assertion library on consumers.

### Removed

- The duplicate `AddLiteBus(Action<IModuleRegistry>)` overloads from the Microsoft DI and Autofac adapters. Use the
  single `Action<ILiteBusBuilder>` callback and access `builder.Modules` for custom module registration.

### Fixed

- Typed error handlers can suppress a recoverable exception and return a fallback result without reimplementing an
  untyped synchronous interface. The runtime no longer discards their explicit cancellation token.
- Event parallel fault-mode documentation now matches runtime behavior: already-started sibling tasks settle before
  either one failure or an aggregate is surfaced, and sibling cancellation is never implied.
- The shared Generic Host orchestrator now runs as a supervised `BackgroundService`. An unexpected LiteBus background
  loop fault requests application shutdown immediately instead of leaving the host alive without that workload.
- Closed generic handler registrations retain independent descriptors instead of colliding on one open generic
  definition.
- Inbox and outbox leases use a monotonic generation fence. Renewal and terminal persistence reject stale generations,
  including when the same configured owner reacquires an expired row.
- Direct PostgreSQL and relational EF Core leasing use the database clock for eligibility, expiry, and renewal so an
  application clock offset cannot claim future-visible work or extend a lease incorrectly.
- Inbox and outbox receipts now report exact message-ID and tenant-scoped idempotency replays as `AlreadyAccepted` or
  `AlreadyEnqueued` instead of inferring the outcome from envelope equality.
- MySQL EF leasing now binds nullable tenant filters with a typed provider parameter, reloads the actual identifier
  column, and claims disjoint ordered batches without range-lock starvation or update deadlocks.
- SQLite EF leasing and operator queries now translate timestamp comparisons and ordering instead of failing on
  `DateTimeOffset` expressions.
- Analyzer LB1004 now finds result-bearing commands in inbox batches expressed through local variables, arrays,
  target-typed lists, parenthesized or cast expressions, and collection spreads.
- Open transport circuits no longer extend their deadline when retry loops report another rejection. A failed
  destination cannot block healthy publisher destinations or ingress consumption.
- Azure Service Bus no longer treats prefetch as callback concurrency, SQS no longer silently clamps an overloaded
  prefetch field, and Kafka and in-memory ingress no longer advertise prefetch settings they ignore. Invalid safety,
  SQS receive, and Azure concurrency bounds now fail during module composition.
- In-memory transport publication no longer grows an unbounded channel. Publishers wait asynchronously at the
  configured per-destination capacity, cancellation removes waiting publishers, and requeue retains its reservation.
- Kafka readiness no longer runs a synchronous metadata call inside the diagnostic runner. Provider probes preserve
  caller cancellation, redact SDK error text, and isolate broker failures as unhealthy results.
- AMQP publishers now accept RabbitMQ's empty-name default exchange and scope its circuit by routing key. An already
  canceled publish stops before circuit lookup or broker access.
- ASP.NET management failures now return stable problem details with a request trace identifier. Exception messages
  remain in structured host logs and are not returned to management clients.

### Breaking changes

- `IMessageMediator.MediateAsync<TMessage, TResult>` was removed because task-returning strategies produced a nested
  `Task<Task>` API. Call `Mediate<TMessage, Task>` or `Mediate<TMessage, Task<TResult>>` and await its returned task.
- `IMessageErrorHandler.HandleError` and scalar `HandleErrorAsync` overloads were replaced by typed-context asynchronous
  methods. The obsolete `IMessageErrorHandler<TMessage, TResult>` marker and `LegacyErrorHandlerSupport` were removed.
- `IMessageTransport` was renamed to `ITransportPublisher`.
- `IRegistrableCommandConstruct`, `IRegistrableQueryConstruct`, and `IRegistrableEventConstruct` were removed.
- `OutboxEnvelope.AsPublished` now requires the publication timestamp.
- Broker dispatch and ingress adapters require one matching root transport module; broker connection settings were
  removed from ingress options and dispatch overloads.
- `LeaseRenewalRequest` now carries `LeaseGeneration`, `LeaseDuration`, and `RequestedExpiresAt` so relational stores
  can calculate expiry from their database clock while in-memory stores retain deterministic clock control.
- Existing v6 PostgreSQL tables must apply the ordered payload-text, lease-fencing, and saga duplicate-suppression SQL
  files before validation records inbox/outbox version 3 and saga version 2.
- Transport modules register `ITransportCircuitBreakerRegistry` instead of one process-wide
  `ITransportCircuitBreaker`. Custom publisher constructors now receive the registry, and the broad
  `TransportPublishFailurePolicy` classification API was removed. Circuit adapters call `AcquirePermit()` and pass
  the returned `TransportCircuitBreakerPermit` to `RecordSuccess` or `RecordFailure`.
- `TransportConsumerOptions.MaxConcurrentMessages` was renamed to `MaxConcurrentCalls`; `ReceiveBatchSize` and
  `MaxInFlightMessages` were added. `AwsSqsInboxIngressOptions.PrefetchCount` became `ReceiveBatchSize`; Kafka and
  in-memory ingress removed `PrefetchCount`. Provider-neutral ingress properties now live under each adapter's
  `Safety` record, including AMQP trust and batch settings.
- AWS SQS and Azure Service Bus root transport modules now register connectivity probes. Configure
  `ConnectivityCheckQueueUrl` or `ConnectivityCheckTarget`; otherwise the registered probe reports degraded instead
  of claiming an unopened SDK client is healthy.
- `IInboxStore` and `IOutboxStore` append methods return `InboxAppendResult` and `OutboxAppendResult`. The redundant
  typed `IOutbox.EnqueueBatchAsync<TEvent>` overload is removed; use the non-generic item batch overload.
- EF application migrations must add `IX_LiteBus_Inbox_CreatedAt` and `IX_LiteBus_Outbox_CreatedAt`. Existing SQLite
  tables must convert durable timestamp columns to UTC ticks stored as `INTEGER`.

## v6.0.0

Greenfield release for durable messaging on **.NET 10** (`net10.0` only). Adopt v6 as a fresh integration: nested module builders, `AcceptAsync` /
`EnqueueAsync`, pipelined processors only, and current PostgreSQL schemas with no automatic upgrade from LiteBus v5
table shapes. Historical v4/v5 upgrade steps remain in [Migration Guide v4](docs/migration/v4.md)
and [Migration Guide v5](docs/migration/v5.md) only.

### Added

- `IInboxEnvelopeFactory` / `IOutboxEnvelopeFactory` shared by auto-commit writers, store-bound transactional writers,
  and EF interceptors.
- Non-generic `ITransactionalInbox` / `ITransactionalOutbox` with `StoreBoundTransactionalInbox` /
  `StoreBoundTransactionalOutbox`.
- PostgreSQL `CreateTransactionalStore`, `EnableAmbientTransactionProvider()`, and `IPostgreSqlTransactionProvider` for
  ambient participation.
- Writer item/metadata model: `InboxAcceptItem`, `InboxAcceptMetadata`, `OutboxEnqueueItem`, `OutboxEnqueueMetadata`,
  and shared durable value objects in `LiteBus.Messaging.Abstractions.DurableMessaging`.
- [Transactional messaging writes](docs/reliable-messaging/transactional-writes.md) scenario guide.
- `LiteBus.Testing` package with `Test*` mediators, inbox/outbox test doubles, and assertion helpers.
- `ICompositeModule` and nested `InboxModuleBuilder` / `OutboxModuleBuilder` with `UsePostgreSqlStorage`,
  `UseEntityFrameworkCoreStorage`, `UseInMemoryStorage`, `UseInProcessDispatch`, `UseAmqpDispatch`,
  and `UseAmqpIngress`.
- Contract registry split: `IContractWriter` / `IContractReader` on `IMessageContractRegistry`; durable runtime depends
  on read surface only.
- Message registry split: `IMessageWriter` / `IMessageReader` with O(1) `Find`; per-`IModuleConfiguration` registry
  instance (no `Clear()` or `MessageRegistryAccessor`).
- Manifest hosting: `IStartupTask`, `IBackgroundService`, `IDiagnosticCheck` via `IModuleConfiguration`; generic host
  bridges in `LiteBus.Runtime.Extensions.*.Hosting`.
- PostgreSQL storage with current-version create scripts, ordered v6 migration files, indexes, an optional
  LISTEN/NOTIFY trigger, and `GetCreateScript`, `EnsureAsync`, and `ValidateAsync`. No automatic upgrade exists from v5.
- EF Core and InMemory inbox/outbox storage; `LiteBus.Storage.Testing` contract harnesses.
- Transport platform: `LiteBus.Transport.Amqp`, Kafka, `LiteBus.Transport.AwsSqs`, Azure Service Bus, InMemory; inbox/outbox dispatch and
  AMQP ingress packages.
- `PipelinedInboxProcessor` / `PipelinedOutboxProcessor` with batch terminal updates, OpenTelemetry meters, retention
  cleanup, dead-letter replay APIs.
- Transactional outbox: `LiteBusOutboxSaveChangesInterceptor`, `ITransactionalOutbox<TContext>`, aligned PostgreSQL
  connection and EF `UseExistingDbContext` participation APIs.
- `LiteBus.Analyzers` rules LB1001, LB1003, LB1004, LB1005, LB1007, LB1008, LB1009, LB1010, LB1011, LB1012, LB1013,
  LB1014 (processor without dispatcher), LB1015-LB1016 (transactional EF/interceptor and DbContext), LB1017 (explicit
  contract registration for attributed types). See [Analyzers](docs/reference/analyzers.md).
- Saga inbox integration (`inbox.EnableSaga()`), payload encryption hooks, tenant lease filters, management and health
  extensions.
- Failure-mode coverage for real worker process termination, Generic Host drain during active dispatch, broker-backed
  shutdown persistence policy, and per-message scoped `DbContext` isolation.
- Repository-owned docs corpus under `docs/` with [Documentation Index](docs/README.md), [Migration Guide v6](docs/migration/v6.md),
  [v6 feature index](docs/reference/feature-index-v6.md), and [Capability catalog](docs/reference/capability-catalog.md).

### Breaking changes

- **Target framework:** `net10.0` only (.NET 8 and 9 dropped).
- **Writer APIs:** `IInbox.AcceptAsync` and `IOutbox.EnqueueAsync` replace `AddAsync` / scheduler aliases. Removed
  `InboxOptions`, `OutboxOptions`, `IInboxScheduler`, and `IOutboxScheduler`. Deferred visibility uses `MessageVisibility`
  on `*Metadata`. No obsolete shims.
- **Writer construction:** removed `InboxAcceptItems` and `OutboxEnqueueItems` companion types. Build items with static
  factories on `InboxAcceptItem` / `OutboxEnqueueItem` (`From`, `WithTopic`, `WithIdempotency`, and related helpers).
- **In-process dispatch:** `UseInProcessDispatch()` replaces flat `AddInboxInProcessDispatcher` / event publisher dispatch paths.
- **Processors:** pipelined processors only; sequential legacy loops removed.
- **PostgreSQL schema:** version **1** greenfield DDL. Drop legacy LiteBus tables and apply v1 create scripts; no
  `GetUpgradeScript` path from v5 shapes.
- **Store roles:** `IInboxTerminalStateStore`, retention, and diagnostics interfaces replace monolithic state stores.
- **Registry:** process-wide `MessageRegistry` and `Clear()` removed; one registry per module configuration.
- **Removed APIs:** `IEventPublisher`, `IIdempotentCommand`, v5 `ICommandScheduler` / `AddCommandInboxModule` aliases,
  `ISagaHandler<TCommand,TState>` (use `ISagaContext` in command handlers). See [Saga](docs/reliable-messaging/saga.md).
- **Registration:** flat storage/dispatch/ingress registrars removed; compose inside `AddInboxModule` /
  `AddOutboxModule` only.
- **Composition packages:** removed `LiteBus.Extensions.All`. Use the per-module Microsoft DI packages or the
  `LiteBus.Extensions.Microsoft.DependencyInjection` aggregate package.

### Changed

- Package layout: `LiteBus.Storage.PostgreSql`, `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*`,
  `LiteBus.Transport.Amqp`, `*.Dispatch.InProcess`.
- Neutral inbox/outbox naming: `litebus_inbox_messages.message_id`, `InboxEnvelope`, `OutboxEnvelope`, message-neutral
  processor and store XML.
- `IEventMediator.PublishAsync` is the sole in-process event API.
- `MessageContractAttribute` lives in `LiteBus.Messaging.Abstractions`; explicit `Contracts.Register` recommended for
  durable types.
- Module configuration throws when two modules register the same service type with different bindings.
- PostgreSQL store options default `EnsureSchemaCreationOnStartup = true`; optional validate-only startup for
  migration-owned DDL.
- Analyzer LB1004 targets `IInbox.AcceptAsync`, `AcceptBatchAsync`, `ITransactionalInbox`, and `InboxAcceptItem` rather
  than scheduler APIs.
- Saga: per-dispatch `AsyncLocal` scope, `SagaDefinitionId` and tenant-scoped primary keys, versioned `SagaCompleteItem`,
  dirty-conflict propagation and completion-only retry in `SagaProcessorHook`, `ISagaStore.QueryAsync` / `PurgeAsync`,
  removed `ISaga<TState>`.
- **v6.0 API renames (complete in shipping libraries):** see [Migration Guide v6](docs/migration/v6.md) for the
  legacy-to-v6 inventory.

### Fixed

- InMemory outbox lease expiry handling for null lease timestamps.
- EF inbox/outbox modules register one singleton store for writer, lease, and state roles.
- PostgreSQL advisory lock keys use independent stable hashes.
- EF in-memory/SQLite leasing filters pending rows before `Take`.
- Thread-safe outbox dispatcher recording for deterministic background processor tests.
- Saga dirty-state conflicts no longer reload and persist stale handler snapshots after a concurrent version advance.
- Transport CI result isolation and skipped-test detection for current VSTest TRX output; live Azure tests use a
  separate opt-in category.

### Docs

- Imported the documentation into the main repository and removed the GitHub wiki submodule.
- Added [Documentation Index](docs/README.md) as the canonical manual entry point.
- Added a compile-checked application sample covering command, query, event, inbox, and outbox composition.
- Added repository checks for relative links, plain ASCII typography, trailing whitespace, and writing-rule phrases.
- Added release checks for benchmark discovery, package metadata, symbol packages, and changelog-derived release notes.

## v5.0.0

### Changed

- `ICommandMediator.SendAsync` now always executes commands immediately in process.
- Durable command scheduling moved to `ICommandScheduler.ScheduleAsync`, which stores `ICommand` envelopes and returns
  `CommandReceipt<TCommand>`.
- Durable event publication now uses `IOutboxWriter.AddAsync` or `IIntegrationOutbox.AddAsync`, which store event
  envelopes and return `OutboxReceipt<TEvent>`.
- Durable inbox and outbox payloads now use stable message contracts with names and versions.
- Durable inbox stores now expose `ICommandInboxWriter`, `ICommandInboxLeaseStore`, and `ICommandInboxStateStore`
  instead of one broad store contract.
- Durable outbox stores now expose `IOutboxMessageWriter`, `IOutboxMessageLeaseStore`, and `IOutboxMessageStateStore`
  instead of one broad store contract.
- Stable outbox message ids now come from `OutboxOptions.MessageId`.
- Event handler predicates now apply to both `PublishAsync(IEvent, settings)` and
  `PublishAsync<TEvent>(TEvent, settings)`.
- Message descriptor resolution failures now throw `MessageDescriptorNotFoundException` with lookup details.
- Message registry namespace filtering now skips only `System` and `System.*` namespaces.
- Unsupported open generic handler shapes now throw `UnsupportedOpenGenericHandlerException`.
- Durable contract registration now supports closed generic message types and rejects open generic message types.
- Persisted contract registration and resolution now use `IMessageContractRegistry` only (`Register`, `GetContract`,
  `GetMessageType`).
- Closed generic messages with concrete handlers now resolve the registered handler type without closing it again.
- The repository now uses `LiteBus.slnx` instead of `LiteBus.sln`.
- CI workflows now restore, build, and test `LiteBus.slnx`, and report Docker availability before PostgreSQL
  Testcontainers tests.

### Added

- Added `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox`, and `LiteBus.Inbox.Storage.PostgreSql`.
- Added `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox`, and `LiteBus.Outbox.Storage.PostgreSql`.
- Added raw Npgsql inbox and outbox stores with leasing, retry visibility, dead-letter state, and Testcontainers
  coverage.
- Added canonical `.sql` schema files in `LiteBus.Storage.PostgreSql`, `LiteBus.Inbox.Storage.PostgreSql`, and
  `LiteBus.Outbox.Storage.PostgreSql` for copy-paste migration ownership.
- Added `IPostgreSqlSchemaLogger` to `LiteBus.Storage.PostgreSql` (Npgsql-only dependency) for optional schema operation
  logging.
- Added `PostgreSqlInboxSchema` / `PostgreSqlOutboxSchema` APIs: `GetCreateScript`, `GetUpgradeScript`, `EnsureAsync`,
  and `ValidateAsync`.
- Added `LiteBus.Inbox.Storage.PostgreSql.Extensions.Microsoft.Hosting` and
  `LiteBus.Outbox.Storage.PostgreSql.Extensions.Microsoft.Hosting` for opt-in schema bootstrap on generic host startup.
- Added `LiteBus.Inbox.Extensions.Microsoft.Hosting` and `LiteBus.Outbox.Extensions.Microsoft.Hosting` for optional
  generic-host processor loops and health checks.
- Added `LiteBus.Storage.PostgreSql.IntegrationTests` with Testcontainers coverage for inbox/outbox stores, schema
  bootstrap and upgrades, drift validation, module registration, and end-to-end processor flows.
- Added `AGENTS.md`, `src/.editorconfig`, and StyleCop documentation analyzers (`GenerateDocumentationFile`) for all
  `src/` projects.
- Added XML documentation on all library types, members, and private/internal fields under `src/`.

### Removed

- Removed the v4 attribute-based command inbox API and related command inbox abstractions.
- Removed `LiteBus.Commands.Extensions.Microsoft.Hosting` because it was tied to the old inbox host.
- Removed `LiteBus.Inbox.Extensions.Autofac` and `LiteBus.Outbox.Extensions.Autofac` because hosting registration lives
  in the Microsoft hosting extension packages (Autofac apps use the same hosting modules through the runtime adapter).
- Removed `IIdentifiedIntegrationEvent`; event identity now belongs to outbox envelope options.
- Removed inbox/outbox processor host interfaces and `UseProcessorHost`; hosting is configured through
  `AddInboxProcessorHosting` / `AddOutboxProcessorHosting` on the hosting extension packages.
- Removed `IMessageContractRegistrar`; contract registration is part of `IMessageContractRegistry`.

### Changed (hosting)

- Moved inbox and outbox processor hosting out of core modules into separate extension packages with self-contained
  `BackgroundService` loops.
- Core inbox/outbox modules now register processors only; they no longer reference Microsoft hosting or health-check
  packages.

### Docs

- Added v5 reliability roadmap, domain event and unit-of-work guidance, and architecture decision records.
- Updated command inbox docs for explicit scheduling semantics, storage metadata, retry, dead-letter, and idempotency
  guidance.
- Added durable outbox docs for writer, processor, dispatcher, PostgreSQL storage, and transaction boundaries.
- Added [PostgreSQL Schema Management](docs/integrations/postgresql-schema-management.md) covering migration-owned DDL, explicit
  bootstrap, opt-in host bootstrap, multi-instance safety, and future upgrade paths.
- Added architecture, dependency graph, and v5 migration docs.
- Added a cookbook recipe for PostgreSQL inbox and outbox registration with processor hosting.
- Added `AGENTS.md` and Cursor rules for XML documentation standards on `src/**/*.cs`.

### Improved

- Expanded PostgreSQL integration tests and fixed cross-test isolation for parallel CI runs.

### Notes

- Inbox and outbox processors deliver **at-least-once** semantics. Handlers and dispatch targets must be idempotent, or
  you must enforce idempotency with application keys such as `CommandScheduleOptions.IdempotencyKey` and
  `OutboxOptions.MessageId`.
- v5 ships durable storage for **PostgreSQL only** (`LiteBus.Inbox.Storage.PostgreSql`,
  `LiteBus.Outbox.Storage.PostgreSql`). Entity Framework Core and SQL Server store packages shipped in **v6**
  (`LiteBus.Inbox.Storage.EntityFrameworkCore`, `LiteBus.Outbox.Storage.EntityFrameworkCore`); dedicated SQL Server
  Npgsql-style packages remain on the [Roadmap](docs/roadmap/README.md).

## v4.4.0

### Added

- **Post-Handler Result Override:** Post-handlers can now override the result returned to the caller
  by writing a replacement value to `AmbientExecutionContext.Current.MessageResult`. The mediator
  reads this property after the post-handler chain completes and returns it in place of the main
  handler result when non-null. Last write wins when multiple post-handlers write to this property.
  Applies to commands with results and queries. Void commands and events are unaffected.

### Improved

- **Testing Docs (`WebApplicationFactory` isolation):** Added a dedicated wiki section documenting
  the `MessageRegistryAccessor.Instance.Clear()` workaround required when using `WebApplicationFactory`
  in integration tests. Without this call the static `MessageRegistry` retains stale handler
  registrations across tests in the same process, causing intermittent `InvalidOperationException`
  failures on CI.

### Updated

- **Dependencies:** Bumped `Microsoft.Extensions.*` packages to 10.0.8, `Microsoft.SourceLink.GitHub`
  to 10.0.300, `Microsoft.NET.Test.Sdk` to 18.5.1, and `coverlet.*` to 10.0.1.
- **CI:** Updated `softprops/action-gh-release` from v2 to v3 (Node 24 runtime).

## v4.3.0

### Added

- **Open Generic Handler Support:** LiteBus now supports open generic pre-handlers, post-handlers, and error handlers
  (e.g., `MyPreHandler<T> : ICommandPreHandler<T> where T : ICommand`). When registered, LiteBus automatically closes
  the generic for every concrete message type that satisfies its constraints at startup. This enables cross-cutting
  concerns like logging, validation, metrics, and authorization to be implemented once and applied universally, without
  modifying existing messages or handlers. Registration order does not matter. All standard C# generic constraints
  (interface, class, struct, new()) are fully respected.

## v4.2.0

### Added

- **Event Contextual Data:** Added the `Items` property back to `EventMediationSettings`, allowing contextual data to be
  passed through the event mediation pipeline, similar to commands and queries.

### Improved

- **.NET 10 Support:** Added support for .NET 10 across all relevant projects.
- **Developer Experience:** Made assembly signing conditional on the existence of the `LiteBus.snk` file. This
  simplifies the build process for contributors who fork the repository, as they no longer need to generate a strong
  name key to build the project locally.

## v4.1.0

### Added

- **Type-Safe Stream Query Post-Handler:** Introduced the new `IStreamQueryPostHandler<TQuery, TQueryResult>` interface.
  This provides a strongly-typed post-handler for stream queries, giving access to the original query and the
  `IAsyncEnumerable<TQueryResult>` result stream, aligning its design with regular command and query post-handlers.

### Fixed

- **Stream Query Context Preservation:** Fixed a critical bug in the stream query mediation strategy where the
  `AmbientExecutionContext` was lost during stream enumeration. This prevented stream handlers from accessing the
  context (e.g., `Items` collection) in logic that executed after yielding all results, and also prevented stream
  post-handlers from accessing the context. The context is now correctly preserved throughout the entire streaming
  lifecycle.

## v4.0.0

This is a major release with a fundamental architectural redesign to decouple the library from specific Dependency
Injection (DI) containers, introduce a durable Command Inbox, and provide advanced control over event mediation.

### Features

- **Dependency Injection Abstraction (`LiteBus.Runtime`):** The entire library has been refactored to be DI-agnostic,
  introducing a new runtime layer. This decouples the core logic from any specific DI container and allows for
  integrations via a lightweight adapter pattern.
- **Autofac Support:** Added first-class integration with Autofac via the new `LiteBus.Extensions.Autofac` package and
  its companions.
- **Durable Command Inbox:** Introduced the v4 command inbox feature for deferred command execution. This API was
  replaced in v5 by the explicit `ICommandScheduler` and inbox processor contracts.
- **Advanced Event Mediation:** Overhauled event mediation with explicit priority, concurrency, and filtering controls:
- The new `[HandlerPriority]` attribute replaces `[HandlerOrder]` for defining execution priority.
- Added configurable concurrency for both priority groups (`PriorityGroupsConcurrencyMode`) and handlers within the same
  group (`HandlersWithinSamePriorityConcurrencyMode`).
- Enhanced `HandlerPredicate` that receives a full `IHandlerDescriptor` for advanced filtering logic based on handler
  type, priority, tags, and message type.

### Improvements

- **Simplified Module Registration:** The `AddCommandModule`, `AddEventModule`, and `AddQueryModule` extensions now
  automatically register the core `MessageModule`, reducing boilerplate configuration.
- **Registration-Independent Message Registry:** The internal `MessageRegistry` has been re-engineered for improved performance and
  correctness, ensuring handlers are correctly associated with messages regardless of registration order.
- **API Clarity:** Renamed several properties for better intent, such as `Order` to `Priority` on descriptors and
  `Handlers` to `MainHandlers` on `IMessageDependencies`.
- **Testability:** Added `IMessageRegistry.Clear()` to allow resetting the registry state, which is useful in test
  environments.

### Breaking Changes

- **Project Structure & NuGet Packages:** The project structure and package names have been completely refactored. You
  must update your `.csproj` files to reference the new packages (e.g.,
  `LiteBus.Extensions.Microsoft.DependencyInjection`, `LiteBus.Commands.Extensions.Microsoft.DependencyInjection`).
- **DI Registration API:** The `AddLiteBus` registration process is now part of the new DI-specific extension packages.
  Module registration extensions (`AddCommandModule`, etc.) have moved to their respective core namespaces (e.g.,
  `LiteBus.Commands`).
- **Attribute Renaming:** `[HandlerOrder]` has been replaced by `[HandlerPriority]`. The `Order` property on
  `IHandlerDescriptor` is now `Priority`.
- **Mediation Settings `Items` Key:** The key type for the `Items` dictionary on `CommandMediationSettings`,
  `QueryMediationSettings`, and `ExecutionContext` has been changed from `object` to `string`.
- **`EventMediationSettings` Redesign:** The structure of `EventMediationSettings` has been completely changed to
  support the new priority and concurrency features. The `Filters` property is now `Routing`, and a new `Execution`
  property has been added.
- **`IMessageDependencies` Renaming:** The `Handlers` and `IndirectHandlers` properties have been renamed to
  `MainHandlers` and `IndirectMainHandlers`, respectively. This affects custom mediation strategies.

> **Note:** Due to the large architectural changes, please refer to the **v4 Migration Guide** in the release
> notes for detailed instructions on upgrading your project.

## v3.1.0

- **Added**: Support for passing contextual metadata through the mediation pipeline. The `CommandMediationSettings`,
  `QueryMediationSettings`, and `EventMediationSettings` now include an `Items` dictionary (
  `IDictionary<object, object?>`) that can be used to pass data to all handlers (pre-handlers, main handlers,
  post-handlers, and error-handlers) via `AmbientExecutionContext.Current.Items`.

## v3.0.0

- **Breaking Change**: All LiteBus assemblies are now strong-named to support usage in enterprise applications and
  projects that require signed dependencies. This is a breaking change that requires a major version update.

## v2.2.3

- **Fixed**: Remove extra DI container registration

## v2.2.2

- **Fixed**: DI container registration now properly filters out interfaces and abstract classes during service
  registration. Previously, `RegisterFromAssembly()` would cause DI container errors when trying to register
  non-instantiable types. LiteBus message registry continues to accept all types to support polymorphic dispatch, but
  only concrete classes are registered with the DI container.

## v2.2.1

- **Fixed**: Support for record structs as message types (commands, queries, events). Previously record structs couldn't
  be registered due to a type filtering condition that only allowed class types.
- **Improved**: Message registration to handle all non-System types, allowing for greater flexibility in message
  definitions.

## v2.2.0

- **Added**: Support for incremental registration allowing for breaking down LiteBus configuration in different parts of
  the application.

## v2.1.0

- **Added**: .NET 9 support while maintaining backward compatibility with .NET 8
- **Updated**: All dependencies to their latest .NET 9 compatible versions
- **Improved**: Multi-targeting build process for both .NET 8 and .NET 9

## v2.0.0

- **Breaking Change**: Removed nullable annotations from mediator interfaces. Nullability should now be expressed in
  message contracts instead. See [Migration Guides](docs/migration/README.md) for
  details.

## v1.1.0

- Add `IQueryValidator`

## v1.0.0

- Added: Comprehensive wiki documentation
- Added: Source Link support for improved debugging
- Added: Automated release workflow with GitVersion integration
- Added: Handler tags for contextual scenario handling
- Changed: Updated repository structure for the supported .NET project layout
- Improved: Code documentation and examples
- Fixed: Various minor issues from previous versions

## v0.25.1

- Add `ICommandValidator`

## v0.25.0

- Enable `Nullable` for all projects.

## v0.24.4

- Improve XML comments in the codebase.

## v0.24.3

- Don't throw error by default if no handlers were found for plain event message types

## v0.24.2

- Allow aborting the execution of handlers by calling `Abort` on the execution context.

## v0.24.1

- Add `Tags` to `IExecutionContext`.

## v0.24.0

- Upgraded to .NET 8.

## v0.23.1

- Add `QueryMediatorExtensions` for backward compatibility.
- Add `CommandMediatorExtensions` for backward compatibility.
- Add `EventMediatorExtensions` for backward compatibility.

## v0.23.0

- Fix the missing `Exception` parameter in `IAsyncMessageErrorHandler[TMessage, TMessageResult]` and
  `IAsyncMessageErrorHandler[TMessage]` interfaces.

## v0.22.0

- Introduce tag-based handler filtering through `HandlerTag` and `HandlerTags` attributes.
- Add `CommandMediationSettings` to `ICommandMediator` to allow configuring command mediation.
- Add `QueryMediationSettings` to `IQueryMediator` to allow configuring query mediation.

## v0.21.0

- Fixed Query, Event, and Command error handlers returning `object` instead of `Task`.

## v0.20.2

- Refined Handle Descriptors
- Removed Any Usage of Reflection in `MessageDependencies`
- Removed Some Redundant Code From Descriptors

## v0.20.1

- Rename `AddMessaging` method to `AddMessageModule`.

## v0.20.0

- Revert TargetFramework to NET 7

## v0.19.1

- Add `ThrowOnNoHandlers` to `EventMediationSettings` to allow throwing an exception when no handlers are found for an
  event.
- Fixed a bug where the pre and post handlers were being executed even when no main handlers were found.

## v0.19.0

- Upgraded to .NET 8.

## v0.18.4

- Rename `FilterHandler` to `HandlerFilter` on `EventMediationSettings` as it is more concise and directly states that
  it is a filter for handlers.

## v0.18.3

- Add `EventMediationSettings` to IEventMediator to allow configuring event mediation.
- Add `FilterHandler` to `EventMediationSettings` to allow filtering event handlers.

## v0.18.2

- Preserve the stack trace when rethrowing an exception in case there are no error handlers.

## v0.18.1

- Make execution of event handlers synchronous by default.

## v0.18.0

- All post handlers expose message result as the second parameter.
- Fixed a bug where IEventPreHandler was not asynchronous.
- Added more unit tests.

## v0.17.1

- Add `Items` property to `IExecutionContext` to allow passing data between handlers.

## v0.17.0

- Rename `AddCommands` method to `AddCommandModule`.
- Rename `AddEvents` method to `AddEventModule`.
- Rename `AddQueries` method to `AddQueryModule`.

## v0.16.0

- Introduced execution context using AsyncLocal functionality, accessible through AmbientExecutionContext.
- Renamed `RegisterFrom` to `RegisterFromAssembly` in module builders.
- Standardized namespace for all files in the `LiteBus.Messaging.Abstractions` project to
  `LiteBus.Messaging.Abstractions`, irrespective of folder path.
- Removed `HandleContext` as a parameter from post and pre handlers.

## v0.15.1

- Removed `IEvent` constraint from event handlers, allowing objects to be passed as events without implementing the
  `IEvent` interface.

## v0.15.0

- Added overload method to event publisher for passing an object as a message.
- Removed `LiteBus` prefix from module constructor names.

## v0.14.1

- Upgraded dependency packages.

## v0.14.0

- Upgraded to .NET 7.

## v0.13.0

- Replaced `ICommandBase` with `ICommand`.
- Replaced `IQueryBase` with `IQuery`.
- Renamed `ILiteBusModule` to `IModule`.
- Removed methods `RegisterPreHandler`, `RegisterHandler`, and `RegisterPostHandler`, replacing them with `Register`.
- Removed superfluous base interfaces.

## v0.12.0

- Added support to message registry for registering any class type as a message.

## v0.11.3

- Fixed bug: Execute error handlers instead of pre handlers during error phase.

## v0.11.2

- Fixed bug: Considered the count of indirect error handlers when determining if an exception should be rethrown.

## v0.11.1

- Disabled nullable reference types.
- Ensured error handlers cover errors in pre and post handlers.

## v0.11.0

- Introduced non-generic message registration overloads for events, queries, and messaging configuration.
- Removed the sample project.
- Added unit tests for events and queries.
