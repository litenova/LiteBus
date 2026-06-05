# Changelog

All notable changes to this project will be documented in this file.

## v6.0.0

### Added

- Added `ICompositeModule`, composite module registration in `ModuleRegistry`, and nested inbox/outbox configuration through `InboxModuleBuilder` / `OutboxModuleBuilder` with `UsePostgreSqlStorage`, `UseEfCoreStorage`, `UseInMemoryStorage`, `UseInProcessDispatcher`, `UseAmqpDispatcher`, and `UseAmqpIngress` extension methods.
- Added `IContractWriter`, `IContractReader`, and `MessageContractBuilder`; `IMessageContractRegistry` now composes read and write surfaces. Runtime services (`Inbox`, `Outbox`, dispatchers) depend on `IContractReader` only.
- Added `IMessageWriter`, `IMessageReader`, and O(1) `Find` on the message registry; `IMessageRegistry` now composes read and write surfaces. `MessageMediator` and resolve strategies depend on `IMessageReader` only; on-the-spot registration uses `IMessageWriter`. Removed `Clear()` and the global `MessageRegistryAccessor`; each `IModuleConfiguration` owns its own `MessageRegistry` instance.
- Added `InboxProcessorHostOptions`, `OutboxProcessorHostOptions`, and cleanup host options to the inbox/outbox abstractions packages.
- Added unit tests for composite module ordering, contract registry try-methods, `MessageContractBuilder`, and nested inbox/outbox DI composition.
- Added transactional outbox participation APIs: `LiteBusOutboxSaveChangesInterceptor`, `OutboxDbContextExtensions.AddLiteBusOutboxInterceptor`, `EfCoreOutboxStorageModuleBuilder.EnableSaveChangesInterceptor`, `EfCoreOutboxStore.UseExistingDbContext<TContext>`, and `PostgreSqlOutboxStore.UseExistingConnection`. Documented that default `IOutbox.AddAsync` commits in a separate store transaction unless callers use these APIs. Added PostgreSQL and EF Core integration tests for atomic commit and rollback with domain state.
- Added `LiteBus.Transport.Amqp` with `AmqpConnectionOptions`, `IAmqpConnectionManager`, `IAmqpPublisher`, `IAmqpConsumer`, `AmqpPublishRequest`, `AmqpReceivedMessage`, and stable LiteBus AMQP header constants for RabbitMQ and LavinMQ.
- Added `tests/LiteBus.Transport.Amqp.IntegrationTests` with Testcontainers coverage against `rabbitmq:4-management` and `cloudamqp/lavinmq`.
- Added `LiteBus.Storage.PostgreSql` (renamed from `LiteBus.PostgreSql`) with shared PostgreSQL schema infrastructure.
- Added `LiteBus.Inbox.Storage.PostgreSql` (renamed from `LiteBus.Inbox.PostgreSql`) with `PostgreSqlInboxStore`, `AddPostgreSqlInboxStorage()`, and schema APIs (`GetCreateScript`, `GetUpgradeScript`, `EnsureAsync`, `ValidateAsync`).
- Added `LiteBus.Outbox.Storage.PostgreSql` (renamed from `LiteBus.Outbox.PostgreSql`) with `PostgreSqlOutboxStore`, `AddPostgreSqlOutboxStorage()`, and matching schema APIs.
- Added `tests/LiteBus.Storage.PostgreSql.IntegrationTests` (renamed from `LiteBus.PostgreSql.IntegrationTests`) with Testcontainers coverage and explicit `AddInboxInProcessDispatcher` / `AddOutboxInProcessDispatcher` in end-to-end tests.
- Added `LiteBus.Storage.Testing` with abstract `InboxStoreContractTests` and `OutboxStoreContractTests` shared by in-memory, PostgreSQL, and future EF Core stores.
- Added `LiteBus.Outbox.Storage.InMemory.UnitTests` exercising the in-memory store against the shared outbox store contract tests.
- Added `docs/Testing.md` with guidance for in-memory storage and Testcontainers-based integration tests.
- Added `LiteBus.Outbox.Dispatch.InProcess` with `InProcessOutboxDispatcher` and `AddOutboxInProcessDispatcher()` for in-process outbox publication through `IEventPublisher`.
- Added `LiteBus.Outbox.Dispatch.Amqp` with `AmqpOutboxDispatcher`, `AmqpOutboxDispatcherOptions`, and `AddOutboxAmqpDispatcher()` for broker publication through `LiteBus.Transport.Amqp`. Registration aliases: `AddOutboxRabbitMqDispatcher`, `AddOutboxLavinMqDispatcher`.
- Added `tests/LiteBus.Outbox.Dispatch.Amqp.IntegrationTests` with end-to-end coverage against RabbitMQ and LavinMQ Testcontainers (in-memory store, processor, AMQP queue assertion).
- Added `docs/Outbox-Amqp-Dispatch.md` for AMQP outbox dispatch registration, routing, and wire format.
- Added `LiteBus.Inbox.Dispatch.InProcess` with `InProcessInboxDispatcher`, `AddInboxInProcessDispatcher()`, and single-dispatcher registration validation.
- Added `LiteBus.Inbox.Dispatch.Amqp` with `AmqpInboxDispatcher`, `AmqpInboxDispatcherOptions`, and `AddInboxAmqpDispatcher()` (plus `AddInboxRabbitMqDispatcher` / `AddInboxLavinMqDispatcher` aliases) for publishing leased inbox envelopes to AMQP.
- Added `tests/LiteBus.Inbox.Dispatch.Amqp.IntegrationTests` covering inbox processor dispatch to RabbitMQ and LavinMQ queues.
- Added `docs/Inbox-Amqp.md` documenting AMQP inbox dispatch registration, headers, and remote execution flow.
- Added `tests/LiteBus.Inbox.Dispatch.InProcess.UnitTests` covering in-process dispatch success, non-command contract failure, trace metadata propagation, and cancellation.
- Added `docs/Inbox.md` with core inbox module, writer, processor, hosting, and separate storage/dispatch registration.
- Added `LiteBus.Inbox.Storage.InMemory` with `InMemoryInboxStore`, `AddInMemoryInboxStorage()`, and `InMemoryInboxStoreOptions` for capacity limits and default lease duration.
- Added `LiteBus.Inbox.Storage.InMemory.UnitTests` with inbox store contract, concurrent lease, and idempotency coverage.
- Added `LiteBus.Outbox.Storage.EntityFrameworkCore` with `OutboxMessageEntity`, `IOutboxDbContext`, `EfCoreOutboxStore`, `EfCoreOutboxStoreOptions`, `OutboxEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration()`, and `AddEfCoreOutboxStorage()`.
- Added `LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests` and `LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests` against shared `OutboxStoreContractTests`.
- Added [Entity Framework Core outbox storage](docs/Outbox-EntityFrameworkCore-Storage.md) documentation.
- Added `LiteBus.Inbox.Ingress.Amqp` with `AmqpInboxConsumer`, `AmqpInboxIngressHandler`, `AddInboxAmqpIngress()`, and RabbitMQ/LavinMQ registration aliases.
- Added `IBackgroundService`, `IStartupTask`, `IModuleConfiguration.RegisterBackgroundService`, `IModuleConfiguration.RegisterStartupTask`, `LiteBus.Runtime.Extensions.Microsoft.Hosting`, and `LiteBus.Runtime.Extensions.Autofac.Hosting` so feature modules register host work separately from `DependencyDescriptor` registration.
- Added `tests/LiteBus.Inbox.Ingress.Amqp.IntegrationTests` covering publish → ingress → store → processor → command dispatch against RabbitMQ and LavinMQ Testcontainers.
- Added `docs/Inbox-Amqp-Ingress.md` for AMQP inbox ingress registration, wire format, and acknowledgement behavior.
- Added `LiteBus.Analyzers` with compile-time rules LB1001, LB1003, LB1004, LB1005, and LB1007 (duplicate command handlers, query impurity, inbox misuse, open generic handlers, missing contracts) and `docs/Analyzers.md`.
- Added `LiteBus.Inbox.Storage.EntityFrameworkCore` with `InboxMessageEntity`, `IInboxDbContext`, `EfCoreInboxStore`, `EfCoreInboxStoreOptions`, `GetModelBuilderConfiguration()`, and `AddEfCoreInboxStorage()`.
- Added `tests/LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests` and `tests/LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests` against shared `InboxStoreContractTests`.
- Added [Entity Framework Core inbox storage](docs/Inbox-EntityFrameworkCore-Storage.md) documentation.
- Added `LiteBus.Storage.EntityFrameworkCore` with shared EF relational leasing SQL, `EfCoreStorageProvider`, and provider-aware model column helpers used by inbox and outbox EF stores.
- Extended `EfCoreInboxStore` and `EfCoreOutboxStore` with multi-provider leasing for PostgreSQL, SQL Server, and MySQL (Pomelo) without adding provider packages to LiteBus shipping assemblies. Added optional `LeaseProvider` on EF store options and SQL Server integration contract tests.
- Expanded `ProcessorPassResult` with `SucceededCount`, `FailedCount`, `DeadLetteredCount`, and `ElapsedTime`; inbox and outbox processors batch terminal state updates and emit OpenTelemetry activities and metrics (`LiteBus.Inbox`, `LiteBus.Outbox`).
- Added batch `MarkCompleted` / `MarkFailed` (inbox) and `MarkPublished` / `MarkFailed` (outbox) store APIs, `RequeueDeadLetterAsync`, retention cleanup via `EnableCleanup()`, optional insert `NOTIFY` with `UseListenNotify` (schema version 4), and AMQP circuit breaker settings on `AmqpConnectionOptions`.
- Added v6 readiness integration and registration tests: PostgreSQL with AMQP ingress and dispatch, ingress failure acknowledgement paths, module registration guards (`DisableSchemaInitialization`, `DisableIngressConsumer`, duplicate dispatcher/ingress), EF Core processor end-to-end coverage, and `LiteBus.Composition.UnitTests` smoke test for `LiteBus.Samples.V6`.

### Fixed

- `InMemoryOutboxStore` no longer treats publishing envelopes with a null lease expiry as immediately leasable.
- Entity Framework Core inbox and outbox modules register one singleton store instance for writer, lease, and state roles so in-process lease locking works when all roles resolve from DI.
- `EfCoreInboxStore.AddAsync` returns the inserted entity on the happy path without a redundant reload.
- PostgreSQL advisory lock keys now use two independent stable hashes instead of overlapping bit slices from one hash.
- EF Core in-memory and SQLite inbox/outbox leasing filters pending rows in the database before `Take`, and SQLite uses the same in-process lease path as the EF in-memory provider.

### Breaking Changes

- Dropped .NET 8 and .NET 9 support. Shipping libraries and test projects target `net10.0` only.
- Replaced `IInboxStateStore` and `IOutboxStateStore` with terminal, retention, and diagnostics store interfaces (`IInboxTerminalStateStore`, `IInboxRetentionStore`, `IInboxDiagnosticsStore`, and outbox equivalents). Processors and cleanup services depend on the narrow interfaces; storage modules register all three against the same singleton store instance.
- Removed `IInbox.AddAsync` and `IOutbox.AddAsync`. Use `IInbox.AcceptAsync` and `IOutbox.EnqueueAsync` (or `ITransactionalOutbox.EnqueueAsync` for Entity Framework Core save-changes staging). Analyzer LB1004 and AMQP ingress now target `AcceptAsync` only.
- Removed `MessageRegistryAccessor`, `IMessageRegistry.Clear()`, and the process-wide singleton `MessageRegistry`. Custom modules must obtain `IMessageRegistry` from `configuration.GetContext<IMessageRegistry>()` after the messaging module runs (or `GetOrCreateContext` when defining a new messaging entry point). Use a new `MessageRegistry` per test or separate `AddLiteBus` hosts for isolation.
- `IMessageRegistry` now extends `IMessageWriter` and `IMessageReader`; `MessageMediator` and `IMessageResolveStrategy` take the read (and write for on-the-spot registration) surfaces instead of the combined registry.

### Changed

- Obsoleted top-level `AddPostgreSqlInboxStorage`, `AddEfCoreInboxStorage`, `AddInMemoryInboxStorage`, and flat inbox/outbox dispatch and ingress registration methods in favor of nested `AddInboxModule` / `AddOutboxModule` configuration.
- Restored the thin `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` and `LiteBus.Extensions.Microsoft.DependencyInjection` dependency graphs. The meta package references only the command, event, messaging, and query DI extension packages; register inbox, outbox, storage, and dispatch through the packages you install and `AddLiteBus(Action<IModuleRegistry>)`.
- Replaced cross-layer `LiteBusConfigurationException` / `LiteBusTimeoutException` usage in storage and transport with package-specific exception types.
- Moved `InboxPollingWorkSignal` to `LiteBus.Inbox.Abstractions` so inbox storage packages do not reference `LiteBus.Inbox`.
- Added `ITransactionalOutbox` and `TransactionalOutbox` (registered with `EnableSaveChangesInterceptor`) for contract-aware enqueue through the EF Core save-changes interceptor. `ITransactionalOutboxStore` is registered from EF Core storage; `EfCoreOutboxStore.UseExistingDbContext<TContext>` returns that interface.
- Added `IOutboxWorkSignal`, `OutboxPollingWorkSignal`, and optional `PostgreSqlOutboxWorkSignal` when PostgreSQL outbox storage sets `UseListenNotify`. `OutboxProcessorBackgroundService` waits through the work signal instead of raw `Task.Delay` polling.
- Added `EfCoreInboxStore.UseExistingDbContext<TContext>` and `PostgreSqlInboxStore.UseExistingConnection` for transactional inbox writes aligned with the outbox participation APIs.
- Added `MessageModuleBuilder.UseTimeProvider` so hosts can register a custom `TimeProvider`; when unset, `TimeProvider.System` is registered.
- `LiteBus.Runtime.Extensions.Autofac` registers `IServiceProvider` through `Autofac.Extensions.DependencyInjection.AutofacServiceProvider` instead of a private adapter.
- Added `IInboxTerminalStateStore.RequeueDeadLetterAsync(IReadOnlyList<string>)` and `IOutboxTerminalStateStore.RequeueDeadLetterAsync(IReadOnlyList<string>)` default overloads that parse GUID message ids for bulk replay.
- LiteBus module configuration now throws `LiteBusConfigurationException` when two modules register the same service type with different bindings instead of relying on container first-wins behavior.
- Added `OutboxEnvelope.IdempotencyKey`, `OutboxOptions.IdempotencyKey`, PostgreSQL schema version 3 upgrade, and store idempotency handling aligned with the inbox model.
- Moved `MessageContractAttribute` to `LiteBus.Messaging.Abstractions` with `RegisterFromAssembly` scanning and runtime mismatch validation when both attributes and explicit `Register` calls are used.
- Removed `IEventPublisher`; use `IEventMediator` for in-process event publication.
- `InboxEnvelope` and `OutboxEnvelope` expose `TraceContext` (JSON text) end-to-end through PostgreSQL and EF Core stores, lease SQL, in-process and AMQP dispatch, and AMQP ingress. Processors copy it into mediation settings via `MessageTraceContextKeys.TraceContext`.
- `EfCoreInboxStoreOptions` is a sealed record with init-only properties, matching `EfCoreOutboxStoreOptions`.
- PostgreSQL inbox and outbox store options default `EnsureSchemaCreationOnStartup` to `true` and `ValidateSchemaCreationOnStartup` to `false` so new hosts create schema automatically; opt in to validate-only startup for external migration workflows.

### Improved

- Parallel event broadcast handlers each run under an isolated `AmbientExecutionContext` scope so concurrent handlers no longer overwrite a shared `AsyncLocal` value.
- `MessageRegistry` duplicate message-type detection uses hash sets for O(1) lookups while preserving registration order for handlers and committed messages.
- Each `AddLiteBus` / module build receives its own `IMessageRegistry` through `IModuleConfiguration.GetOrCreateContext`, improving test isolation and multi-configuration hosts.
- PostgreSQL schema startup: validate-only path when `EnsureSchemaCreationOnStartup` is false and `ValidateSchemaCreationOnStartup` is true; index existence checks in `ValidateAsync` (optional via `ValidateIndexesOnStartup` on `PostgreSqlSchemaStoreOptions`); metadata repair in `EnsureAsync` when physical shape matches the current version but metadata is stale; `IStartupTask` and host startup gate so schema initializers finish before processor and ingress loops.
- `IModuleConfiguration.StartupTasks` and `IModuleConfiguration.BackgroundServices` preserve first-registration order while deduplicating types.
- Expanded v6 test coverage across PostgreSQL, AMQP ingress/dispatch, EF Core processor paths, module registration, and sample composition smoke verification.

### Docs

- Added `docs/Migration-Guide-v6.md` with package and API rename tables, registration before/after examples, and dispatcher-required guidance.
- Updated `docs/Architecture.md` with Storage, Dispatch, and Ingress axis diagrams.
- Updated `docs/Dependency-Graph.md` with the full v6 package map and dependency rules.
- Updated `docs/Roadmap.md` to mark v6 storage, dispatch, ingress, and analyzer packages as implemented.
- Updated `docs/Cookbook-and-Scenarios.md` with v6 recipes (PostgreSQL inbox + command dispatch, outbox + AMQP, EF outbox + events, InMemory testing, full ingress pipeline).
- Updated `docs/_Sidebar.md` with links to v6 migration, testing, storage, dispatch, ingress, and analyzer pages.
- Added `docs/Amqp-Transport.md` covering RabbitMQ and LavinMQ setup, connection strings, wire headers, and publish/consume usage.
- Documented in-memory outbox registration in `docs/Outbox.md` and testing guidance in `docs/Testing.md`.
- Added `samples/LiteBus.Samples.V6` demonstrating full v6 composition with InMemory storage and explicit dispatch registration.

### Changed

- Renamed inbox writer API surface: `IInbox.AcceptAsync` is the preferred acceptance method; `IInbox.AddAsync` is obsolete and forwards to `AcceptAsync`.
- Renamed outbox writer API surface: `IOutbox.EnqueueAsync` is the preferred enqueue method; `IOutbox.AddAsync` is obsolete and forwards to `EnqueueAsync`.
- Added `OutboxEnvelope.IdempotencyKey`, `OutboxOptions.IdempotencyKey`, PostgreSQL schema version 3 upgrade, and store idempotency handling aligned with the inbox model.
- Moved `MessageContractAttribute` to `LiteBus.Messaging.Abstractions` with `RegisterFromAssembly` scanning and runtime mismatch validation when both attributes and explicit `Register` calls are used.
- Removed `IEventPublisher`; use `IEventMediator` for in-process event publication.
- Renamed `IInboxStateStore.MarkCompletedAsync` parameter `commandId` to `messageId`. Updated inbox envelope, status, lease, failure, and dead-letter XML to message-neutral wording. Removed unused `LiteBus.Commands.Abstractions` reference from `LiteBus.Inbox.Abstractions`. Aligned `OutboxEnvelope` XML with message-neutral wording.
- Renamed inbox PostgreSQL and EF Core storage defaults to neutral table and column names: `public.litebus_inbox_messages` with primary key column `message_id` (replacing `litebus_inbox_commands` / `command_id`). PostgreSQL inbox schema version is 3; `EnsureAsync` and `GetUpgradeScript(2, 3)` apply `Sql/inbox/v3/rename_message_identity.sql` to rename legacy columns and the default table when present. Migration-owned deployments can run the same DDL manually. Added nullable `TraceContext` (`trace_context`) to inbox and outbox EF entities for schema version 2 parity with PostgreSQL stores.
- Replaced per-feature `*.Extensions.Microsoft.Hosting` packages with `IBackgroundService` types registered from core modules via `RegisterBackgroundService`. Inbox and outbox loops use `EnableInboxProcessor()` / `EnableOutboxProcessor()`. AMQP ingress registers `AmqpInboxConsumer` from `AddInboxAmqpIngress()`. PostgreSQL schema bootstrap uses `PostgreSqlInboxSchemaInitializer` / `PostgreSqlOutboxSchemaInitializer`. Removed processor health checks and separate `AddInboxAmqpIngressHosting` / `Add*ProcessorHosting` / `AddPostgreSql*SchemaHosting` extension methods. User-facing documentation uses background service naming; see `docs/Hosted-services.md`.
- Removed analyzer rules LB1002 (duplicate event handler routing) and LB1006 (handler priority conflict). Multiple event handlers for the same event and handlers sharing a priority value are intentional LiteBus behavior.
- Added analyzer rules LB1008 (missing command handler), LB1009 (missing query handler), and LB1010 (duplicate query handler).
- Renamed PostgreSQL storage packages to the v6 `Storage` layout: `LiteBus.PostgreSql` → `LiteBus.Storage.PostgreSql`, `LiteBus.Inbox.PostgreSql` → `LiteBus.Inbox.Storage.PostgreSql`, `LiteBus.Outbox.PostgreSql` → `LiteBus.Outbox.Storage.PostgreSql`, and matching Microsoft.Hosting extensions.
- Renamed inbox writer API: `ICommandScheduler.ScheduleAsync` → `IInbox.AddAsync`, `CommandScheduleOptions` → `InboxOptions`, `CommandReceipt<T>` → `InboxReceipt<T>`.
- Renamed outbox writer API: `IOutboxWriter` / `IIntegrationOutbox` → `IOutbox`; `OutboxOptions.MessageId` → `OutboxOptions.Id`.
- Renamed store roles and envelopes to neutral v6 names (`IInboxStore`, `InboxEnvelope`, `IOutboxStore`, `OutboxEnvelope`).
- `LiteBus.Outbox` is now transport-neutral orchestration only. The core module registers `IOutbox`, `IOutboxProcessor`, and `OutboxProcessorOptions`. Storage and dispatch are registered separately.
- `LiteBus.Inbox` is now transport-neutral orchestration only. Renamed `CommandScheduler` → `InboxWriter`, `CommandInboxProcessor` → `InboxProcessor`, `CommandInboxModule` → `InboxModule`, and matching builder/hosting extensions (`AddInboxModule`, `AddInboxProcessorHosting`, `AddLiteBusInboxProcessor`).
- The core inbox module registers `IInbox`, `IInboxProcessor`, and `InboxProcessorOptions` only. Storage and dispatch are registered separately.
- Inbox and outbox processor hosting validate that `IInboxDispatcher` and `IOutboxDispatcher` are registered before background loops start.
- `InboxProcessor` no longer records retry or dead-letter state when `MarkCompletedAsync` fails after a successful dispatch. The envelope keeps its active lease until completion succeeds or the lease expires.
- `DependencyDescriptor` equality now distinguishes instance and factory registrations with different targets instead of treating every singleton-instance registration for the same service type as a duplicate.
- DI adapters ignore duplicate `RegisterHostedService` calls for the same implementation type.
- Entity Framework Core inbox and outbox model configuration applies provider-specific JSON column types when `EfCoreStorageProvider` is passed to `GetModelBuilderConfiguration()`, marks message identifiers as application-assigned (`ValueGeneratedNever`), and reloads rows after insert so returned payloads match PostgreSQL normalization.
- Added `tests/LiteBus.Runtime.UnitTests` covering module ordering, dependency descriptors, DI adapters, and `AddLiteBus` integration.
- Renamed `LiteBus.Amqp` → `LiteBus.Transport.Amqp` (`LiteBus.{Area}.{Technology}` shared util layout; storage uses `LiteBus.Storage.PostgreSql`).
- Renamed `LiteBus.Inbox.Dispatch.Commands` → `LiteBus.Inbox.Dispatch.InProcess` (`InProcessInboxDispatcher`, `InProcessInboxDispatchModule`, `AddInboxInProcessDispatcher`).
- Renamed `LiteBus.Outbox.Dispatch.Events` → `LiteBus.Outbox.Dispatch.InProcess` (`InProcessOutboxDispatcher`, `InProcessOutboxDispatchModule`, `AddOutboxInProcessDispatcher`).
- Changed `InboxExecutionContextKeys.IsInboxExecution` from `__LiteBus.CommandInbox.IsInboxExecution` to `__LiteBus.Inbox.IsInboxExecution`.
- Removed unused `Microsoft.Extensions.Diagnostics.HealthChecks` central package versions from `src/Directory.Packages.props`.

- Removed `LiteBusEventOutboxDispatcher`, `IntegrationOutbox`, and `UseLiteBusEventDispatcher()` from `LiteBus.Outbox`.
- Removed the `LiteBus.Events.Abstractions` project reference from `LiteBus.Outbox`.
- Removed `CommandInboxDispatcher` from `LiteBus.Inbox`; in-process dispatch is provided by `LiteBus.Inbox.Dispatch.InProcess`.
- Removed the `LiteBus.Commands.Abstractions` project reference from `LiteBus.Inbox`.
- Removed `IIdempotentCommand`; supply `InboxOptions.IdempotencyKey` explicitly.
- Removed v5 `AddCommandInboxModule`, `ICommandScheduler`, `IIntegrationOutbox`, and other v5 registration aliases without obsolete shims.

## v5.0.0

### Changed

- `ICommandMediator.SendAsync` now always executes commands immediately in process.
- Durable command scheduling moved to `ICommandScheduler.ScheduleAsync`, which stores `ICommand` envelopes and returns `CommandReceipt<TCommand>`.
- Durable event publication now uses `IOutboxWriter.AddAsync` or `IIntegrationOutbox.AddAsync`, which store event envelopes and return `OutboxReceipt<TEvent>`.
- Durable inbox and outbox payloads now use stable message contracts with names and versions.
- Durable inbox stores now expose `ICommandInboxWriter`, `ICommandInboxLeaseStore`, and `ICommandInboxStateStore` instead of one broad store contract.
- Durable outbox stores now expose `IOutboxMessageWriter`, `IOutboxMessageLeaseStore`, and `IOutboxMessageStateStore` instead of one broad store contract.
- Stable outbox message ids now come from `OutboxOptions.MessageId`.
- Event handler predicates now apply to both `PublishAsync(IEvent, settings)` and `PublishAsync<TEvent>(TEvent, settings)`.
- Message descriptor resolution failures now throw `MessageDescriptorNotFoundException` with lookup details.
- Message registry namespace filtering now skips only `System` and `System.*` namespaces.
- Unsupported open generic handler shapes now throw `UnsupportedOpenGenericHandlerException`.
- Durable contract registration now supports closed generic message types and rejects open generic message types.
- Persisted contract registration and resolution now use `IMessageContractRegistry` only (`Register`, `GetContract`, `GetMessageType`).
- Closed generic messages with concrete handlers now resolve the registered handler type without closing it again.
- The repository now uses `LiteBus.slnx` instead of `LiteBus.sln`.
- CI workflows now restore, build, and test `LiteBus.slnx`, and report Docker availability before PostgreSQL Testcontainers tests.

### Added

- Added `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox`, and `LiteBus.Inbox.Storage.PostgreSql`.
- Added `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox`, and `LiteBus.Outbox.Storage.PostgreSql`.
- Added raw Npgsql inbox and outbox stores with leasing, retry visibility, dead-letter state, and Testcontainers coverage.
- Added canonical `.sql` schema files in `LiteBus.Storage.PostgreSql`, `LiteBus.Inbox.Storage.PostgreSql`, and `LiteBus.Outbox.Storage.PostgreSql` for copy-paste migration ownership.
- Added `IPostgreSqlSchemaLogger` to `LiteBus.Storage.PostgreSql` (Npgsql-only dependency) for optional schema operation logging.
- Added `PostgreSqlInboxSchema` / `PostgreSqlOutboxSchema` APIs: `GetCreateScript`, `GetUpgradeScript`, `EnsureAsync`, and `ValidateAsync`.
- Added `LiteBus.Inbox.Storage.PostgreSql.Extensions.Microsoft.Hosting` and `LiteBus.Outbox.Storage.PostgreSql.Extensions.Microsoft.Hosting` for opt-in schema bootstrap on generic host startup.
- Added `LiteBus.Inbox.Extensions.Microsoft.Hosting` and `LiteBus.Outbox.Extensions.Microsoft.Hosting` for optional generic-host processor loops and health checks.
- Added `LiteBus.Storage.PostgreSql.IntegrationTests` with Testcontainers coverage for inbox/outbox stores, schema bootstrap and upgrades, drift validation, module registration, and end-to-end processor flows.
- Added `AGENTS.md`, `src/.editorconfig`, and StyleCop documentation analyzers (`GenerateDocumentationFile`) for all `src/` projects.
- Added XML documentation on all library types, members, and private/internal fields under `src/`.

### Removed

- Removed the v4 attribute-based command inbox API and related command inbox abstractions.
- Removed `LiteBus.Commands.Extensions.Microsoft.Hosting` because it was tied to the old inbox host.
- Removed `LiteBus.Inbox.Extensions.Autofac` and `LiteBus.Outbox.Extensions.Autofac` because hosting registration lives in the Microsoft hosting extension packages (Autofac apps use the same hosting modules through the runtime adapter).
- Removed `IIdentifiedIntegrationEvent`; event identity now belongs to outbox envelope options.
- Removed inbox/outbox processor host interfaces and `UseProcessorHost`; hosting is configured through `AddInboxProcessorHosting` / `AddOutboxProcessorHosting` on the hosting extension packages.
- Removed `IMessageContractRegistrar`; contract registration is part of `IMessageContractRegistry`.

### Changed (hosting)

- Moved inbox and outbox processor hosting out of core modules into separate extension packages with self-contained `BackgroundService` loops.
- Core inbox/outbox modules now register processors only; they no longer reference Microsoft hosting or health-check packages.

### Docs

- Added v5 reliability roadmap, domain event and unit-of-work guidance, and architecture decision records.
- Updated command inbox docs for explicit scheduling semantics, storage metadata, retry, dead-letter, and idempotency guidance.
- Added durable outbox docs for writer, processor, dispatcher, PostgreSQL storage, and transaction boundaries.
- Added [PostgreSQL Schema Management](docs/PostgreSQL-Schema-Management.md) covering migration-owned DDL, explicit bootstrap, opt-in host bootstrap, multi-instance safety, and future upgrade paths.
- Added architecture, dependency graph, and v5 migration docs.
- Added a cookbook recipe for PostgreSQL inbox and outbox registration with processor hosting.
- Added `AGENTS.md` and Cursor rules for XML documentation standards on `src/**/*.cs`.

### Improved

- Expanded PostgreSQL integration tests and fixed cross-test isolation for parallel CI runs.

### Notes

- Inbox and outbox processors deliver **at-least-once** semantics. Handlers and dispatch targets must be idempotent, or you must enforce idempotency with application keys such as `CommandScheduleOptions.IdempotencyKey` and `OutboxOptions.MessageId`.
- v5 ships durable storage for **PostgreSQL only** (`LiteBus.Inbox.Storage.PostgreSql`, `LiteBus.Outbox.Storage.PostgreSql`). Entity Framework Core and SQL Server store packages remain on the roadmap; bring your own store by implementing the writer, lease, and state role interfaces until those packages ship.

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

### 🚀 Features

- **Dependency Injection Abstraction (`LiteBus.Runtime`):** The entire library has been refactored to be DI-agnostic,
  introducing a new runtime layer. This decouples the core logic from any specific DI container and allows for
  integrations via a lightweight adapter pattern.
- **Autofac Support:** Added first-class integration with Autofac via the new `LiteBus.Extensions.Autofac` package and
  its companions.
- **Durable Command Inbox:** Introduced the v4 command inbox feature for deferred command execution. This API was
  replaced in v5 by the explicit `ICommandScheduler` and inbox processor contracts.
- **Advanced Event Mediation:** Overhauled event mediation with powerful new controls:
- The new `[HandlerPriority]` attribute replaces `[HandlerOrder]` for defining execution priority.
- Added configurable concurrency for both priority groups (`PriorityGroupsConcurrencyMode`) and handlers within the same
  group (`HandlersWithinSamePriorityConcurrencyMode`).
- Enhanced `HandlerPredicate` that receives a full `IHandlerDescriptor` for advanced filtering logic based on handler
  type, priority, tags, and message type.

### ✨ Improvements

- **Simplified Module Registration:** The `AddCommandModule`, `AddEventModule`, and `AddQueryModule` extensions now
  automatically register the core `MessageModule`, reducing boilerplate configuration.
- **Robust Message Registry:** The internal `MessageRegistry` has been re-engineered for improved performance and
  correctness, ensuring handlers are correctly associated with messages regardless of registration order.
- **API Clarity:** Renamed several properties for better intent, such as `Order` to `Priority` on descriptors and
  `Handlers` to `MainHandlers` on `IMessageDependencies`.
- **Testability:** Added `IMessageRegistry.Clear()` to allow resetting the registry state, which is useful in test
  environments.

### 💥 Breaking Changes

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
  message contracts instead. See [Migration Guide](https://github.com/litenova/LiteBus/wiki/Migration-Guide) for
  details.

## v1.1.0

- Add `IQueryValidator`

## v1.0.0

- Added: Comprehensive wiki documentation
- Added: Source Link support for improved debugging
- Added: Automated release workflow with GitVersion integration
- Added: Handler tags for contextual scenario handling
- Changed: Updated repository structure for modern .NET practices
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
