# Changelog Archive

This archive holds the release notes for LiteBus v6.0.2 and earlier. Releases from v7.0.0 on are in [Changelog.md](Changelog.md).

## v6.0.2

This is a patch release for .NET 10. Public APIs, persistence schemas, and transport behavior are unchanged.

### Fixed

- Publishing an event through a base-typed or interface-typed reference now invokes the handlers registered for the concrete event type. Both `IEventMediator.PublishAsync(IEvent, ...)` and the `PublishAsync(@event, cancellationToken)` extension pass the event with the static type `IEvent`. Because handler contracts are contravariant, `AsyncBroadcastMediationStrategy<TMessage>` matched only the handlers registered for that static type and skipped the rest without raising an error. Handlers whose contract is not closed over the compile-time message type now go through the non-generic handler entry point, which routes to the closed contract the handler implements. This also restores in-process outbox dispatch, which publishes every `IEvent` through the non-generic overload.

## v6.0.1

This is a documentation and policy maintenance release for .NET 10. Public APIs, persistence schemas, and transport behavior are unchanged.

### Added

- `AI_POLICY.md` documents the project's history before public generative coding assistants and defines review, disclosure, provenance, and verification requirements for AI-assisted contributions.
- The repository now includes writing guidance for technical documentation, API descriptions, and capability comparisons.

### Changed

- The root README now focuses on command, query, and event mediation, durable messaging, a compact command example, and direct documentation links.
- Non-migration documentation describes the current LiteBus API and behavior, without narrating how past releases changed them. Historical API, package, and database transitions remain in the migration guides.
- The roadmap lists planned work without a version-specific roadmap page.
- Documentation edit links target the `main` branch.

### Fixed

- PostgreSQL schema drift diagnostics now report an incompatible current schema contract without assuming that an existing table belongs to a specific LiteBus release.

## v6.0.0

This is a major release for **.NET 10** (`net10.0` only). Applications upgrading from v5 must adopt nested module builders, `AcceptAsync` / `EnqueueAsync`, pipelined processors, and the new durable messaging contracts. PostgreSQL schemas begin at version 1 in this release and do not mutate LiteBus v5 tables automatically. Follow the [Migration Guide v6](https://litebus.io/docs/migration/v6) to upgrade the API, packages, hosting, and database.

### Added

- The repository enforces a role-based project and package dependency policy through `ArchitectureDependencyPolicyTests`.
- Root `Add*Transport` composition extensions cover AMQP, Kafka, AWS SQS, Azure Service Bus, and in-memory transports.
- Dispatch-scope lifecycle coverage includes the Autofac container.
- Axis-specific append results and outbox enqueue outcomes distinguish new rows from idempotent replays in receipts.
- Provider contract matrices cover file-backed SQLite and MySQL 8.4 for both Entity Framework Core durable stores.
- Third-party transport adapter authors can use the published `LiteBus.Transport.Testing` xUnit conformance tests.
- The repository has an evaluated package inventory, source-linked compiled snippets, test-symbol discovery, and semantic documentation gates.
- Shared durable-store contract cases cover empty batches, mixed terminal outcomes, complete filters, dead-letter replay, and strict idempotency conflicts.
- Broker readiness diagnostics cover Kafka, AWS SQS, and Azure Service Bus, including live emulator coverage for each configured target.
- This release adds the concern-specific `LiteBus.Testing.Mediation`, `LiteBus.Testing.Transport`, `LiteBus.Testing.DurableMessaging`, and `LiteBus.Testing.Hosting` packages.

- `IInboxEnvelopeFactory` / `IOutboxEnvelopeFactory` are shared by auto-commit writers, store-bound transactional writers, and EF interceptors.
- Non-generic `ITransactionalInbox` / `ITransactionalOutbox` are available with `StoreBoundTransactionalInbox` / `StoreBoundTransactionalOutbox`.
- PostgreSQL provides `CreateTransactionalStore`, `EnableAmbientTransactionProvider()`, and `IPostgreSqlTransactionProvider` for participation in ambient transactions.
- The writer item/metadata model adds `InboxAcceptItem`, `InboxAcceptMetadata`, `OutboxEnqueueItem`, `OutboxEnqueueMetadata`, and shared durable value objects in `LiteBus.Messaging.Abstractions.DurableMessaging`.
- The documentation includes the [Transactional messaging writes](https://litebus.io/docs/reliable-messaging/transactional-writes) scenario guide.
- The `LiteBus.Testing` package provides `Test*` mediators, inbox/outbox test doubles, and assertion helpers.
- `ICompositeModule` and nested `InboxModuleBuilder` / `OutboxModuleBuilder` support `UsePostgreSqlStorage`, `UseEntityFrameworkCoreStorage`, `UseInMemoryStorage`, `UseInProcessDispatch`, `UseAmqpDispatch`, and `UseAmqpIngress`.
- The contract registry is split into `IContractWriter` / `IContractReader` on `IMessageContractRegistry`. The durable runtime depends only on the read surface.
- The message registry is split into `IMessageWriter` / `IMessageReader` with O(1) `Find`. Each `IModuleConfiguration` gets its own registry instance, with no `Clear()` or `MessageRegistryAccessor`.
- Manifest hosting provides `IStartupTask`, `IBackgroundService`, and `IDiagnosticCheck` through `IModuleConfiguration`, with generic host bridges in `LiteBus.Runtime.Extensions.*.Hosting`.
- PostgreSQL storage ships v6 version 1 create scripts, indexes, an optional LISTEN/NOTIFY trigger, and `GetCreateScript`, `EnsureAsync`, and `ValidateAsync`. No automatic conversion exists from v5.
- EF Core and InMemory inbox/outbox storage are available, with `LiteBus.Storage.Testing` contract harnesses.
- The transport platform includes `LiteBus.Transport.Amqp`, Kafka, `LiteBus.Transport.AwsSqs`, Azure Service Bus, and InMemory, with inbox/outbox dispatch and AMQP ingress packages.
- `PipelinedInboxProcessor` / `PipelinedOutboxProcessor` provide batch terminal updates, OpenTelemetry meters, retention cleanup, and dead-letter replay APIs.
- The transactional outbox provides `LiteBusOutboxSaveChangesInterceptor`, `ITransactionalOutbox<TContext>`, and aligned participation APIs for PostgreSQL connections and EF `UseExistingDbContext`.
- `LiteBus.Analyzers` includes rules LB1001, LB1003, LB1004, LB1005, LB1007, LB1008, LB1009, LB1010, LB1011, LB1012, LB1013, LB1014 (processor without dispatcher), LB1015-LB1016 (transactional EF/interceptor and DbContext), and LB1017 (explicit contract registration for attributed types). See [Analyzers](https://litebus.io/docs/reference/analyzers).
- Saga inbox integration (`inbox.EnableSaga()`) is available, with payload encryption hooks, tenant lease filters, and management and health extensions.
- Failure-mode coverage includes real worker process termination, Generic Host drain during active dispatch, broker-backed shutdown persistence policy, and per-message scoped `DbContext` isolation.
- The repository owns its docs corpus under `site/content/docs/`, with [Documentation Index](https://litebus.io/docs), [Migration Guide v6](https://litebus.io/docs/migration/v6), [v6 feature index](https://litebus.io/docs/reference/feature-index-v6), and [Capability catalog](https://litebus.io/docs/reference/capability-catalog).

### Changed

- Error handlers now receive `MessageErrorContext<TMessage, TResult>` plus the caller's explicit cancellation token. The typed context shares handled outcome and fallback result state with the mediation pipeline.
- `ILiteBusBuilder` moved to `LiteBus.Runtime.Abstractions` and now exposes only `Modules`. Feature packages provide `AddMessaging`, `AddCommands`, `AddQueries`, `AddEvents`, `AddInbox`, and `AddOutbox`.
- `LiteBus.Orchestration.Abstractions` became `LiteBus.DurableMessaging.Abstractions`, which owns shared durable metadata, retry, lease, processor, and hook contracts.
- Inbox and outbox implementation services and module builders moved from abstractions into their core packages.
- Microsoft DI and Autofac adapters now own dispatch-scope creation. Composition without scope configuration fails, while root provider dispatch requires explicit `RootMessageDispatchScopeFactory` registration.
- EF Core inbox and outbox stores use `IDbContextFactory<TContext>` operation contexts owned by the adapter.
- Saga storage is selected exactly once inside `EnableSaga(...)`. In-memory storage is no longer an implicit fallback.
- Module dependency validation uses composite ownership and `IRequires<TModule>` without registration markers or dependency-registry scans during `Build()`.
- Outbox processor option precedence is independent of configuration call order.
- PostgreSQL inbox, outbox, and saga schemas start at version 1. Validation checks required column types as well as columns, indexes, and metadata.
- Inbox and outbox store append methods return ordered append results containing the source-of-truth envelope and insertion outcome.
- SQLite EF models store durable timestamps as UTC ticks, and MySQL leasing uses `READ COMMITTED` with a named chronological index.
- Test coverage uses one canonical collector configuration and an exact source-line union across every CI batch. Pull request and release jobs enforce 90 percent line coverage and treat Codecov upload failures as failures.
- Transport publishers resolve circuit breakers by destination. Ingress recovery no longer shares publisher failure state, and half-open recovery admits one probe after a monotonic break duration. Opaque operation permits prevent late completions from resetting a newer circuit generation.
- Transport consumers now separate provider-neutral `MaxInFlightMessages` from RabbitMQ and Azure prefetch, SQS `ReceiveBatchSize`, and Azure `MaxConcurrentCalls`. Every ingress adapter carries the same nested `Safety` record. In-memory destinations now apply configurable, lossless backpressure to queued and in-flight deliveries.
- ASP.NET Core health checks and the management health route expose shared per-probe timeout and parallelism limits. The `AddLiteBus()` health registration carries both `litebus` and `ready` tags.
- `LiteBus.Testing` is now a framework-neutral base package. Mediator, transport, durable, and host helpers no longer impose unrelated dependency graphs, Newtonsoft.Json, or an assertion library on consumers.

- The package layout uses `LiteBus.Storage.PostgreSql`, `LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*`, `LiteBus.Transport.Amqp`, and `*.Dispatch.InProcess`.
- Inbox/outbox naming is neutral, using `litebus_inbox_messages.message_id`, `InboxEnvelope`, `OutboxEnvelope`, and message-neutral processor and store XML.
- `IEventMediator.PublishAsync` is the sole in-process event API.
- `MessageContractAttribute` lives in `LiteBus.Messaging.Abstractions`. Explicit `Contracts.Register` is recommended for durable types.
- When two modules register the same service type with different bindings, module configuration throws.
- PostgreSQL store options now default to `EnsureSchemaCreationOnStartup = true`. Deployments where migrations own the DDL can use optional validate-only startup.
- Analyzer LB1004 targets `IInbox.AcceptAsync`, `AcceptBatchAsync`, `ITransactionalInbox`, and `InboxAcceptItem` rather than scheduler APIs.
- Saga now uses a per-dispatch `AsyncLocal` scope, `SagaDefinitionId` and tenant-scoped primary keys, a versioned `SagaCompleteItem`, dirty-conflict propagation and completion-only retry in `SagaProcessorHook`, and `ISagaStore.QueryAsync` / `PurgeAsync`. `ISaga<TState>` is removed.
- **v5 to v6 API migration:** see [Migration Guide v6](https://litebus.io/docs/migration/v6) for the legacy-to-v6 inventory.

### Removed

- Removed the duplicate `AddLiteBus(Action<IModuleRegistry>)` overloads from the Microsoft DI and Autofac adapters. Use the single `Action<ILiteBusBuilder>` callback and access `builder.Modules` for custom module registration.
- Removed the preview-only PostgreSQL v6 migration scripts and schema versions. The released inbox, outbox, and saga shapes each use one complete version 1 create script.
- Removed the redundant `PostgreSql*Schema.CreateIfNotExistsAsync` aliases. Use `EnsureAsync` for application-managed startup checks or `ValidateAsync` when deployment tooling owns DDL.
- Removed the AMQP-specific header alias, circuit-breaker wrapper, and exception. AMQP now uses the shared transport headers and destination-scoped circuit-breaker contracts directly.
- Removed the legacy AWS SQS correlation-header fallback. All transport adapters now read and write the canonical `TransportHeaders.CorrelationId` key.
- Removed the unused GitVersion configuration. CI and release workflows take their package version from the build or release tag.
- Removed the release-workflow call to a removed documentation script. Release validation now uses the repository's active documentation and site checks.

### Fixed

- Typed error handlers can suppress a recoverable exception and return a fallback result without reimplementing an untyped synchronous interface. The runtime no longer discards their explicit cancellation token.
- Event parallel fault-mode documentation now matches runtime behavior. Already-started sibling tasks settle before the mediator surfaces either one failure or an aggregate exception, and one sibling's failure never cancels the others.
- The shared Generic Host orchestrator now runs as a supervised `BackgroundService`. When a LiteBus background loop faults unexpectedly, the orchestrator requests application shutdown immediately instead of leaving the host alive without that workload.
- Closed generic handler registrations retain independent descriptors instead of colliding on one open generic definition.
- Inbox and outbox leases use a monotonic generation fence. Renewal and terminal persistence reject stale generations, including when the same configured owner reacquires an expired row.
- Direct PostgreSQL and relational EF Core leasing use the database clock for eligibility, expiry, and renewal so an application clock offset cannot claim future-visible work or extend a lease incorrectly.
- Inbox and outbox receipts now report replays detected by exact message ID or tenant-scoped idempotency as `AlreadyAccepted` or `AlreadyEnqueued` instead of inferring the outcome from envelope equality.
- MySQL EF leasing now binds nullable tenant filters with a typed provider parameter, reloads the actual identifier column, and claims disjoint ordered batches without range-lock starvation or update deadlocks.
- SQLite EF leasing and operator queries now translate timestamp comparisons and ordering instead of failing on `DateTimeOffset` expressions.
- Analyzer LB1004 now finds result-bearing commands in inbox batches expressed through local variables, arrays, target-typed lists, parenthesized or cast expressions, and collection spreads.
- Open transport circuits no longer extend their deadline when retry loops report another rejection. A failed destination cannot block healthy publisher destinations or ingress consumption.
- Azure Service Bus no longer treats prefetch as callback concurrency, SQS no longer silently clamps an overloaded prefetch field, and Kafka and in-memory ingress no longer advertise prefetch settings they ignore. Invalid safety, SQS receive, and Azure concurrency bounds now fail during module composition.
- In-memory transport publication no longer grows an unbounded channel. Publishers wait asynchronously at the configured per-destination capacity, cancellation removes waiting publishers, and requeue retains its reservation.
- Kafka readiness no longer runs a synchronous metadata call inside the diagnostic runner. Provider probes preserve caller cancellation, redact SDK error text, and isolate broker failures as unhealthy results.
- AMQP publishers now accept RabbitMQ's empty-name default exchange and scope its circuit by routing key. An already canceled publish stops before circuit lookup or broker access.
- ASP.NET management failures now return stable problem details with a request trace identifier. Exception messages remain in structured host logs and are not returned to management clients.

- InMemory outbox lease expiry now handles null lease timestamps.
- EF inbox/outbox modules now register one singleton store for writer, lease, and state roles.
- PostgreSQL advisory lock keys now use independent stable hashes.
- EF in-memory/SQLite leasing now filters pending rows before `Take`.
- Outbox dispatcher recording is now thread-safe, so background processor tests are deterministic.
- Saga dirty-state conflicts no longer reload and persist stale handler snapshots after a concurrent version advance.
- Transport CI now isolates results and detects skipped tests for current VSTest TRX output. Live Azure tests use a separate opt-in category.

### Breaking Changes

- `IMessageMediator.MediateAsync<TMessage, TResult>` was removed because task-returning strategies produced a nested `Task<Task>` API. Call `Mediate<TMessage, Task>` or `Mediate<TMessage, Task<TResult>>` and await its returned task.
- `IMessageErrorHandler.HandleError` and scalar `HandleErrorAsync` overloads were replaced by typed-context asynchronous methods. The obsolete `IMessageErrorHandler<TMessage, TResult>` marker and `LegacyErrorHandlerSupport` were removed.
- `IMessageTransport` was renamed to `ITransportPublisher`.
- `IRegistrableCommandConstruct`, `IRegistrableQueryConstruct`, and `IRegistrableEventConstruct` were removed.
- `OutboxEnvelope.AsPublished` now requires the publication timestamp.
- Broker dispatch and ingress adapters require one matching root transport module. Broker connection settings were removed from ingress options and dispatch overloads.
- `LeaseRenewalRequest` now carries `LeaseGeneration`, `LeaseDuration`, and `RequestedExpiresAt` so relational stores can calculate expiry from their database clock while in-memory stores retain deterministic clock control.
- PostgreSQL v6 starts each component at schema version 1 because no v6 schema was released before 6.0.0. v5 tables require replacement or an application-owned data migration.
- Transport modules register `ITransportCircuitBreakerRegistry` instead of one process-wide `ITransportCircuitBreaker`. Custom publisher constructors now receive the registry, and the broad `TransportPublishFailurePolicy` classification API was removed. Circuit adapters call `AcquirePermit()` and pass the returned `TransportCircuitBreakerPermit` to `RecordSuccess` or `RecordFailure`.
- `TransportConsumerOptions.MaxConcurrentMessages` was renamed to `MaxConcurrentCalls`. `ReceiveBatchSize` and `MaxInFlightMessages` were added. `AwsSqsInboxIngressOptions.PrefetchCount` became `ReceiveBatchSize`. Kafka and in-memory ingress removed `PrefetchCount`. Provider-neutral ingress properties are now grouped under each adapter's `Safety` record, including AMQP trust and batch settings.
- AWS SQS and Azure Service Bus root transport modules now register connectivity probes. Configure `ConnectivityCheckQueueUrl` or `ConnectivityCheckTarget`. Otherwise the registered probe reports degraded instead of claiming an unopened SDK client is healthy.
- `IInboxStore` and `IOutboxStore` append methods return `InboxAppendResult` and `OutboxAppendResult`. The redundant typed `IOutbox.EnqueueBatchAsync<TEvent>` overload is removed. Use the non-generic item batch overload.
- EF application migrations must add `IX_LiteBus_Inbox_CreatedAt` and `IX_LiteBus_Outbox_CreatedAt`. In existing SQLite tables, convert durable timestamp columns to UTC ticks stored as `INTEGER`.

- **Target framework:** the target is `net10.0` only (.NET 8 and 9 dropped).
- **Writer APIs:** `IInbox.AcceptAsync` and `IOutbox.EnqueueAsync` replace `AddAsync` / scheduler aliases. `InboxOptions`, `OutboxOptions`, `IInboxScheduler`, and `IOutboxScheduler` are removed. Deferred visibility uses `MessageVisibility` on `*Metadata`. There are no obsolete shims.
- **Writer construction:** `InboxAcceptItems` and `OutboxEnqueueItems` companion types are removed. Build items with static factories on `InboxAcceptItem` / `OutboxEnqueueItem` (`From`, `WithTopic`, `WithIdempotency`, and related helpers).
- **In-process dispatch:** `UseInProcessDispatch()` replaces flat `AddInboxInProcessDispatcher` / event publisher dispatch paths.
- **Processors:** only pipelined processors remain. Sequential legacy loops are removed.
- **PostgreSQL schema:** inbox, outbox, and saga use version **1** DDL. Drain and replace v5 tables or run a reviewed application-owned data migration. No `GetUpgradeScript` path exists from v5 shapes.
- **Store roles:** `IInboxTerminalStateStore`, retention, and diagnostics interfaces replace monolithic state stores.
- **Registry:** the process-wide `MessageRegistry` and `Clear()` are removed. There is one registry per module configuration.
- **Removed APIs:** `IEventPublisher`, `IIdempotentCommand`, v5 `ICommandScheduler` / `AddCommandInboxModule` aliases, and `ISagaHandler<TCommand,TState>` are removed (use `ISagaContext` in command handlers). See [Saga](https://litebus.io/docs/reliable-messaging/saga).
- **Registration:** flat storage/dispatch/ingress registrars are removed. Compose inside `AddInboxModule` / `AddOutboxModule` only.
- **Composition packages:** `LiteBus.Extensions.All` is removed. Use the per-module Microsoft DI packages or the `LiteBus.Extensions.Microsoft.DependencyInjection` aggregate package.

### Docs

- This release imports the documentation into the main repository and removes the GitHub wiki submodule.
- The [Documentation Index](https://litebus.io/docs) is the canonical manual entry point.
- A compile-checked application sample covers command, query, event, inbox, and outbox composition.
- Repository checks cover relative links, plain ASCII typography, trailing whitespace, and writing-rule phrases.
- Release checks cover benchmark discovery, package metadata, symbol packages, and changelog-derived release notes.

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
- When message descriptor resolution fails, it throws `MessageDescriptorNotFoundException` with lookup details.
- Message registry namespace filtering now skips only `System` and `System.*` namespaces.
- Unsupported open generic handler shapes now fail with `UnsupportedOpenGenericHandlerException`.
- Durable contract registration now supports closed generic message types and rejects open generic message types.
- Persisted contract registration and resolution now use only `IMessageContractRegistry` (`Register`, `GetContract`, `GetMessageType`).
- For closed generic messages with concrete handlers, resolution now returns the registered handler type without closing it again.
- The repository now uses `LiteBus.slnx` instead of `LiteBus.sln`.
- CI workflows now restore, build, and test `LiteBus.slnx`, and report Docker availability before PostgreSQL Testcontainers tests.

### Added

- Added `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox`, and `LiteBus.Inbox.Storage.PostgreSql`.
- Added `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox`, and `LiteBus.Outbox.Storage.PostgreSql`.
- Added raw Npgsql inbox and outbox stores with leasing, retry visibility, dead-letter state, and Testcontainers coverage.
- Added canonical `.sql` schema files in `LiteBus.Storage.PostgreSql`, `LiteBus.Inbox.Storage.PostgreSql`, and `LiteBus.Outbox.Storage.PostgreSql` that migration owners can copy into their own migrations.
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
- Removed `LiteBus.Inbox.Extensions.Autofac` and `LiteBus.Outbox.Extensions.Autofac` because the Microsoft hosting extension packages provide the hosting registration (Autofac apps use the same hosting modules through the runtime adapter).
- Removed `IIdentifiedIntegrationEvent`. Event identity is now part of outbox envelope options.
- Removed inbox/outbox processor host interfaces and `UseProcessorHost`. Hosting is configured through `AddInboxProcessorHosting` / `AddOutboxProcessorHosting` on the hosting extension packages.
- Removed `IMessageContractRegistrar`. Contract registration is part of `IMessageContractRegistry`.

### Changed (Hosting)

- Moved inbox and outbox processor hosting out of core modules into separate extension packages with self-contained `BackgroundService` loops.
- Core inbox/outbox modules now register only processors. They no longer reference Microsoft hosting or health-check packages.

### Docs

- Added v5 reliability roadmap, domain event and unit-of-work guidance, and architecture decision records.
- Updated the command inbox docs on explicit scheduling semantics, storage metadata, retry, dead-letter, and idempotency.
- Added durable outbox docs on the writer, processor, dispatcher, PostgreSQL storage, and transaction boundaries.
- Added [PostgreSQL Schema Management](https://litebus.io/docs/integrations/postgresql-schema-management) covering migration-owned DDL, explicit bootstrap, opt-in host bootstrap, multi-instance safety, and future upgrade paths.
- Added architecture, dependency graph, and v5 migration docs.
- Added a cookbook recipe for PostgreSQL inbox and outbox registration with processor hosting.
- Added `AGENTS.md` and Cursor rules for XML documentation standards on `src/**/*.cs`.

### Improved

- Expanded PostgreSQL integration tests and fixed cross-test isolation for parallel CI runs.

### Notes

- Inbox and outbox processors deliver **at-least-once** semantics. Handlers and dispatch targets must be idempotent, or you must enforce idempotency with application keys such as `CommandScheduleOptions.IdempotencyKey` and `OutboxOptions.MessageId`.
- v5 ships durable storage for **PostgreSQL only** (`LiteBus.Inbox.Storage.PostgreSql`, `LiteBus.Outbox.Storage.PostgreSql`). Entity Framework Core and SQL Server store packages shipped in **v6** (`LiteBus.Inbox.Storage.EntityFrameworkCore`, `LiteBus.Outbox.Storage.EntityFrameworkCore`). Dedicated SQL Server Npgsql-style packages remain on the [Roadmap](https://litebus.io/docs/roadmap).

## v4.4.0

### Added

- **Post-Handler Result Override:** Post-handlers can now override the result returned to the caller by writing a replacement value to `AmbientExecutionContext.Current.MessageResult`. The mediator reads this property after the post-handler chain completes. When it is non-null, the mediator returns it in place of the main handler result. Last write wins when multiple post-handlers write to this property. This applies to commands with results and queries. Void commands and events are unaffected.

### Improved

- **Testing Docs (`WebApplicationFactory` isolation):** Added a dedicated wiki section on the `MessageRegistryAccessor.Instance.Clear()` workaround, which integration tests using `WebApplicationFactory` must call. Without this call, the static `MessageRegistry` retains stale handler registrations across tests in the same process, causing intermittent `InvalidOperationException` failures on CI.

### Updated

- **Dependencies:** Updated `Microsoft.Extensions.*` packages to 10.0.8, `Microsoft.SourceLink.GitHub` to 10.0.300, `Microsoft.NET.Test.Sdk` to 18.5.1, and `coverlet.*` to 10.0.1.
- **CI:** Updated `softprops/action-gh-release` from v2 to v3 (Node 24 runtime).

## v4.3.0

### Added

- **Open Generic Handler Support:** LiteBus supports open generic pre-handlers, post-handlers, and error handlers (e.g., `MyPreHandler<T> : ICommandPreHandler<T> where T : ICommand`). At startup, LiteBus automatically closes each registered generic for every concrete message type that satisfies its constraints. You can implement a cross-cutting concern such as logging, validation, metrics, or authorization once and apply it to every message type, without modifying existing messages or handlers. Registration order does not matter. LiteBus respects all standard C# generic constraints (interface, class, struct, new()).

## v4.2.0

### Added

- **Event Contextual Data:** The `Items` property is available on `EventMediationSettings` again, so you can pass contextual data through the event mediation pipeline as with commands and queries.

### Improved

- **.NET 10 Support:** This release adds .NET 10 support.
- **Developer Experience:** The build now signs assemblies only when the `LiteBus.snk` file exists. Contributors who fork the repository no longer need to generate a strong name key to build the project locally.

## v4.1.0

### Added

- **Type-Safe Stream Query Post-Handler:** The new `IStreamQueryPostHandler<TQuery, TQueryResult>` interface defines a strongly-typed post-handler for stream queries. It gives access to the original query and the `IAsyncEnumerable<TQueryResult>` result stream. Its design matches regular command and query post-handlers.

### Fixed

- **Stream Query Context Preservation:** Fixed a bug in the stream query mediation strategy where the `AmbientExecutionContext` was lost during stream enumeration. Stream handlers running logic after yielding all results could not access the context (e.g., the `Items` collection), and neither could stream post-handlers. The context is now preserved throughout the streaming lifecycle.

## v4.0.0

This is a major release that decouples the library from specific Dependency Injection (DI) containers, introduces a durable Command Inbox, and adds explicit priority, concurrency, and filtering controls to event mediation.

### Features

- **Dependency Injection Abstraction (`LiteBus.Runtime`):** The library is DI-agnostic, with a new runtime layer. The core logic no longer depends on any specific DI container, and integrations connect through an adapter pattern.
- **Autofac Support:** Added integration with Autofac through the new `LiteBus.Extensions.Autofac` package and its companion packages.
- **Durable Command Inbox:** Introduced the v4 command inbox feature for deferred command execution. This API was replaced in v5 by the explicit `ICommandScheduler` and inbox processor contracts.
- **Advanced Event Mediation:** Event mediation has explicit priority, concurrency, and filtering controls:
- The new `[HandlerPriority]` attribute defines execution priority, replacing `[HandlerOrder]`.
- Added configurable concurrency for both priority groups (`PriorityGroupsConcurrencyMode`) and handlers within the same group (`HandlersWithinSamePriorityConcurrencyMode`).
- `HandlerPredicate` now receives a full `IHandlerDescriptor`, for filtering logic based on handler type, priority, tags, and message type.

### Improvements

- **Simplified Module Registration:** The `AddCommandModule`, `AddEventModule`, and `AddQueryModule` extensions now automatically register the core `MessageModule`, reducing boilerplate configuration.
- **Registration-Independent Message Registry:** The internal `MessageRegistry` was re-engineered for performance and correctness. Handlers are now associated with messages regardless of registration order.
- **API Clarity:** Several properties were renamed to express their intent, such as `Order` to `Priority` on descriptors and `Handlers` to `MainHandlers` on `IMessageDependencies`.
- **Testability:** `IMessageRegistry.Clear()` now resets the registry state for test environments.

### Breaking Changes

- **Project Structure & NuGet Packages:** The project structure and package names have been refactored. You must update your `.csproj` files to reference the new packages (e.g., `LiteBus.Extensions.Microsoft.DependencyInjection`, `LiteBus.Commands.Extensions.Microsoft.DependencyInjection`).
- **DI Registration API:** The `AddLiteBus` registration process is now part of the new DI-specific extension packages. Module registration extensions (`AddCommandModule`, etc.) have moved to their respective core namespaces (e.g., `LiteBus.Commands`).
- **Attribute Renaming:** `[HandlerOrder]` has been replaced by `[HandlerPriority]`. The `Order` property on `IHandlerDescriptor` is now `Priority`.
- **Mediation Settings `Items` Key:** The key type for the `Items` dictionary on `CommandMediationSettings`, `QueryMediationSettings`, and `ExecutionContext` has been changed from `object` to `string`.
- **`EventMediationSettings` Redesign:** The structure of `EventMediationSettings` has been changed to support the new priority and concurrency features. The `Filters` property is now `Routing`, and a new `Execution` property has been added.
- **`IMessageDependencies` Renaming:** The `Handlers` and `IndirectHandlers` properties have been renamed to `MainHandlers` and `IndirectMainHandlers`, respectively. This affects custom mediation strategies.

> **Note:** Because of the architectural changes, see the **v4 Migration Guide** in the release notes for detailed upgrade instructions.

## v3.1.0

- **Added**: You can pass contextual metadata through the mediation pipeline. The `CommandMediationSettings`, `QueryMediationSettings`, and `EventMediationSettings` include an `Items` dictionary (`IDictionary<object, object?>`) that passes data to all handlers (pre-handlers, main handlers, post-handlers, and error handlers) through `AmbientExecutionContext.Current.Items`.

## v3.0.0

- **Breaking Change**: All LiteBus assemblies are strong-named, so enterprise applications and projects that require signed dependencies can reference them.

## v2.2.3

- **Fixed**: This release removes an extra DI container registration.

## v2.2.2

- **Fixed**: DI container registration now filters out interfaces and abstract classes. Previously, `RegisterFromAssembly()` caused DI container errors by trying to register non-instantiable types. The LiteBus message registry still accepts all types to support polymorphic dispatch, but only concrete classes are registered with the DI container.

## v2.2.1

- **Fixed**: Record structs couldn't be registered because a type filtering condition only allowed class types. They now work as message types (commands, queries, events).
- **Improved**: Message registration now accepts all non-System types, so you can use any non-System type in message definitions.

## v2.2.0

- **Added**: Incremental registration lets you break down LiteBus configuration across different parts of the application.

## v2.1.0

- **Added**: This release adds .NET 9 support and stays backward compatible with .NET 8.
- **Updated**: All dependencies are now at their latest .NET 9 compatible versions.
- **Improved**: The multi-targeting build process now covers both .NET 8 and .NET 9.

## v2.0.0

- **Breaking Change**: Removed nullable annotations from mediator interfaces. Nullability should now be expressed in message contracts instead. See [Migration Guides](https://litebus.io/docs/migration) for details.

## v1.1.0

- Added `IQueryValidator`.

## v1.0.0

- Added comprehensive wiki documentation.
- Added Source Link support for improved debugging.
- Added an automated release workflow with GitVersion integration.
- Added handler tags for contextual scenario handling.
- Changed the repository structure to the supported .NET project layout.
- Improved code documentation and examples.
- Fixed minor issues from previous versions.

## v0.25.1

- Added `ICommandValidator`.

## v0.25.0

- Enabled `Nullable` for all projects.

## v0.24.4

- Improved XML comments in the codebase.

## v0.24.3

- If no handlers are found for plain event message types, no error is thrown by default.

## v0.24.2

- You can now abort handler execution by calling `Abort` on the execution context.

## v0.24.1

- Added `Tags` to `IExecutionContext`.

## v0.24.0

- Upgraded to .NET 8.

## v0.23.1

- Added `QueryMediatorExtensions` for backward compatibility.
- Added `CommandMediatorExtensions` for backward compatibility.
- Added `EventMediatorExtensions` for backward compatibility.

## v0.23.0

- Fixed the missing `Exception` parameter in the `IAsyncMessageErrorHandler[TMessage, TMessageResult]` and `IAsyncMessageErrorHandler[TMessage]` interfaces.

## v0.22.0

- Introduced tag-based handler filtering through `HandlerTag` and `HandlerTags` attributes.
- Added `CommandMediationSettings` to `ICommandMediator` for configuring command mediation.
- Added `QueryMediationSettings` to `IQueryMediator` for configuring query mediation.

## v0.21.0

- Query, Event, and Command error handlers returned `object` instead of `Task`. They now return `Task`.

## v0.20.2

- Refined Handle Descriptors.
- Removed all use of reflection in `MessageDependencies`.
- Removed redundant code from descriptors.

## v0.20.1

- Renamed the `AddMessaging` method to `AddMessageModule`.

## v0.20.0

- Reverted TargetFramework to NET 7.

## v0.19.1

- Added `ThrowOnNoHandlers` to `EventMediationSettings` to throw an exception when no handlers are found for an event.
- Pre and post handlers no longer run when no main handlers are found.

## v0.19.0

- Upgraded to .NET 8.

## v0.18.4

- Renamed `FilterHandler` to `HandlerFilter` on `EventMediationSettings`, as the shorter name states directly that it filters handlers.

## v0.18.3

- Added `EventMediationSettings` to IEventMediator for configuring event mediation.
- Added `FilterHandler` to `EventMediationSettings` for filtering event handlers.

## v0.18.2

- When an exception is rethrown because there are no error handlers, the stack trace is now preserved.

## v0.18.1

- Event handlers now run synchronously by default.

## v0.18.0

- All post handlers now take the message result as the second parameter.
- IEventPreHandler is now asynchronous.
- Added more unit tests.

## v0.17.1

- Added the `Items` property to `IExecutionContext` for passing data between handlers.

## v0.17.0

- Renamed the `AddCommands` method to `AddCommandModule`.
- Renamed the `AddEvents` method to `AddEventModule`.
- Renamed the `AddQueries` method to `AddQueryModule`.

## v0.16.0

- Introduced an execution context that uses AsyncLocal functionality and is accessible through AmbientExecutionContext.
- Renamed `RegisterFrom` to `RegisterFromAssembly` in module builders.
- Standardized the namespace for all files in the `LiteBus.Messaging.Abstractions` project to `LiteBus.Messaging.Abstractions`, irrespective of folder path.
- Removed the `HandleContext` parameter from pre and post handlers.

## v0.15.1

- Removed the `IEvent` constraint from event handlers, so objects can be passed as events without implementing the `IEvent` interface.

## v0.15.0

- Added an overload to the event publisher for passing an object as a message.
- Removed the `LiteBus` prefix from module constructor names.

## v0.14.1

- Upgraded dependency packages.

## v0.14.0

- Upgraded to .NET 7.

## v0.13.0

- Replaced `ICommandBase` with `ICommand`.
- Replaced `IQueryBase` with `IQuery`.
- Renamed `ILiteBusModule` to `IModule`.
- Removed the `RegisterPreHandler`, `RegisterHandler`, and `RegisterPostHandler` methods and replaced them with `Register`.
- Removed unnecessary base interfaces.

## v0.12.0

- The message registry now supports registering any class type as a message.

## v0.11.3

- Fixed a bug where pre handlers ran during the error phase instead of error handlers. Error handlers now run instead.

## v0.11.2

- Fixed a bug in rethrow decisions. The count of indirect error handlers is now considered when determining whether an exception should be rethrown.

## v0.11.1

- Disabled nullable reference types.
- Error handlers now cover errors in pre and post handlers.

## v0.11.0

- Introduced non-generic message registration overloads for events, queries, and messaging configuration.
- Removed the sample project.
- Added unit tests for events and queries.
