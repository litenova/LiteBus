# LiteBus - Complete AI Context & Reference Guide

> LiteBus is a .NET 10 library for in-process mediation (commands, queries, events) and durable messaging (inbox, outbox, saga), with pluggable relational or in-memory storage and broker transports. It serves CQRS-style application architecture where handler discovery, pipeline stages, message contracts, auditing, and at-least-once delivery are declared rather than inferred. The architecture is a module graph in which each feature is an `IModule` that writes container-neutral `DependencyDescriptor` entries into an `IDependencyRegistry`, so the same composition runs on Microsoft DI and Autofac.

The assembly version is `7.0.0` (`VersionPrefix` in `src/Directory.Build.props`). The target framework is `net10.0`. Nullable reference types are enabled. The license is MIT. All assemblies are strong-named with a shared public key.

---

## Table of contents

1. [Core Architecture & Mental Model](#1-core-architecture--mental-model)
2. [Global Setup & Dependency Injection](#2-global-setup--dependency-injection)
3. [Configuration & All Options (Exhaustive)](#3-configuration--all-options-exhaustive)
4. [Feature-by-Feature Deep Dive](#4-feature-by-feature-deep-dive)
5. [Enum & Constants Catalog](#5-enum--constants-catalog)
6. [Common Recipes & Code Snippets](#6-common-recipes--code-snippets)

---

## 1. Core Architecture & Mental Model

### 1.1 Design patterns in use

| Pattern | Where it appears |
| --- | --- |
| **Module / Composition Root** | `IModule`, `ICompositeModule`, `IModuleRegistry`, `IModuleConfiguration`. Each feature (`MessageModule`, `CommandModule`, `QueryModule`, `EventModule`, `InboxModule`, `OutboxModule`, `SagaModule`, transport modules, storage/dispatch/ingress sub-modules) is a module. The registry sorts modules topologically by `IRequires<TModule>` edges before calling `Build`. |
| **Builder** | `MessageModuleBuilder`, `CommandModuleBuilder`, `QueryModuleBuilder`, `EventModuleBuilder`, `InboxModuleBuilder`, `OutboxModuleBuilder`, `SagaModuleBuilder`, plus per-adapter builders (`PostgreSqlInboxModuleBuilder`, `EfCoreOutboxStorageModuleBuilder`, `AmqpInboxIngressModuleBuilder`, ...). Builders hold configuration only and never touch the container. |
| **Container abstraction / Adapter** | The abstraction is `IDependencyRegistry` with `DependencyDescriptor` and `InstanceLifetime`. `MicrosoftDependencyRegistryAdapter` and `AutofacDependencyRegistryAdapter` translate descriptors. `IMessageDispatchScopeFactory` and `IMessageDispatchScope` abstract per-message scopes. `IMessageDispatchScope : IDisposable, IAsyncDisposable` exposes `IServiceProvider ServiceProvider { get; }`, which is the scoped provider for the lifetime of the scope. |
| **Mediator** | `IMessageMediator` is the core mediator. `ICommandMediator`, `IQueryMediator`, and `IEventMediator` are semantic facades over it. |
| **Strategy** | `IMessageResolveStrategy` (descriptor lookup) and `IMessageMediationStrategy<TMessage, TMessageResult>` (pipeline shape). Implementations: `ActualTypeOrFirstAssignableTypeMessageResolveStrategy`, `SingleAsyncHandlerMediationStrategy<TMessage>`, `SingleAsyncHandlerMediationStrategy<TMessage, TMessageResult>`, `SingleStreamHandlerMediationStrategy<TMessage, TMessageResult>` (its constructor takes the enumeration `CancellationToken`, and it implements `IMessageMediationStrategy<TMessage, IAsyncEnumerable<TMessageResult>>`), `AsyncBroadcastMediationStrategy<TMessage>`, `DecisionEvaluationMediationStrategy<TMessage>` (`IMessageMediationStrategy<TMessage, Task<MediationDecision>>`, which backs `Evaluate` by running guards then validators only, so no shortcut claims a key, no pre-handler or main handler runs, the completion stage does not fire, and no audit record is produced). |
| **Pipeline / staged middleware** | The declaration order of the `PreStage` enum fixes the stage order: Guard, then Validator, then Shortcut, then PreHandler, then the main handler, then post-handlers, then error handlers on fault, then completion handlers. |
| **Registry** | `IMessageRegistry` (handler descriptors and message metadata), `IMessageContractRegistry` (durable contract name and version mapped to CLR type), `ISagaStateTypeRegistry`, `ITransportCircuitBreakerRegistry`. |
| **Factory** | `IInboxEnvelopeFactory`, `IOutboxEnvelopeFactory`, `InboxProcessorFactory`, `OutboxProcessorFactory`, `PipelineDispatch.For(Type)`. |
| **Lazy resolution** | `LazyHandler<THandler, TDescriptor>` is a struct with required init-only `Lazy<THandler> Handler` and `TDescriptor Descriptor`. `ILazyHandlerCollection<THandler, TDescriptor> : IReadOnlyCollection<LazyHandler<THandler, TDescriptor>> where TDescriptor : IHandlerDescriptor` adds no members and is built by `LazyHandlerCollection<THandler, TDescriptor>` (sealed, with a constructor from `IEnumerable<LazyHandler<THandler, TDescriptor>>`) or `EnumerableExtensions.ToLazyReadOnlyCollection`. The pipeline resolves handler instances from the container only when it reaches them. `LazyMessageDispatchScope` defers host scope creation until the first service resolution, so a stream that is never enumerated holds no container scope. |
| **Ambient context (AsyncLocal)** | `AmbientExecutionContext` with `AmbientExecutionContext.ExecutionContextScope`, and `AmbientAuditScope` which builds on `IExecutionContext.Items`. |
| **Envelope / Unit of work** | `InboxEnvelope`, `OutboxEnvelope` are immutable records with transition methods (`AsLeased`, `AsCompleted`/`AsPublished`, `AsFailed`, `AsDeadLettered`, `AsRequeued`, `AsReleased`). |
| **Lease / fencing token** | `LeaseOwner`, monotonic `LeaseGeneration`, and `LeaseExpiresAt` on envelopes. `ILeaseRenewable.RenewLeaseAsync` sends heartbeats, and `ReleaseLeaseAsync` returns a cancelled dispatch to the queue with its attempt refunded. |
| **Discriminated union via sealed record hierarchies** | `MessageIdentity`, `Idempotency`, `MessageVisibility`, `MessageTrace`, `TenantScope`, `PublicationTarget`, `AuditDeclaration`, `AuthorizationDeclaration`, `AzureServiceBusDiagnosticTarget`. |
| **Result objects instead of exceptions** | `Verdict`, `Validity`, `Shortcut`, `Shortcut<T>`, `Refusal`, `PipelineDecision`, `MediationOutcome`, `PersistResult`, `ProcessorPassResult`. |
| **Roslyn analyzers** | 22 diagnostic rules (`LB1001` to `LB1024`, with `LB1002` and `LB1006` reserved and never shipped) that enforce composition invariants at compile time. |

### 1.2 Key interfaces and their primary implementations

#### Mediation core (`LiteBus.Messaging.Abstractions` -> `LiteBus.Messaging`)

| Interface | Primary implementation | Notes |
| --- | --- | --- |
| `IMessageMediator` | `MessageMediator` (internal) | It creates an `ExecutionContext` and an ambient scope, resolves the descriptor, and builds `MessageDependencies` over a lazily created dispatch scope. It then delegates to the mediation strategy, restores the caller ambient context, and retains the scope through `MediationScopeRetention` until the returned `Task` or `IAsyncEnumerable` completes. Its signature `TMessageResult Mediate<TMessage, TMessageResult>(TMessage message, MessageMediationRequest<TMessage, TMessageResult> request, CancellationToken cancellationToken = default) where TMessage : notnull` is synchronous and returns `TMessageResult` directly rather than a `Task`. |
| `IMessageMediationStrategy<in TMessage, out TMessageResult>` | `SingleAsyncHandlerMediationStrategy<>`, `SingleStreamHandlerMediationStrategy<,>`, `AsyncBroadcastMediationStrategy<>`, `DecisionEvaluationMediationStrategy<>` | It requires `where TMessage : notnull` and declares `TMessageResult Mediate(TMessage message, IMessageDependencies messageDependencies, IExecutionContext executionContext)`. A result that is neither `Task` nor `IAsyncEnumerable<T>` counts as materialized, and its execution and dispatch scopes are disposed before `Mediate` returns, so never return `ValueTask<TResult>` or any other in-flight value. |
| `IMessageRegistry` (= `IMessageWriter` and `IMessageReader`) | `MessageRegistry` (internal) | It is thread-safe through a private lock. It discovers handler descriptors from CLR types, links handlers to messages, binds `IMessageDefinition` metadata, and closes open generic handlers. |
| `IMessageContractRegistry` (= `IContractWriter` and `IContractReader`) | `MessageContractRegistry` (internal) | It is a lock-protected bidirectional map between CLR types and (`Name`, `Version`). It also honours `[MessageContract]` on demand. |
| `IMessageSerializer` | `SystemTextJsonMessageSerializer` | It uses `JsonSerializerDefaults.Web` by default and wraps `JsonException` or `NotSupportedException` in `MessageSerializationException`. |
| `IExecutionContext` | `ExecutionContext` (internal) | It carries `CancellationToken`, `HandledAt`, `Items`, `Data`, `Tags`, `MessageResult`, and `PostHandlersSuppressed`. |
| `IProducesResult<out TMessageResult>` | marker on `ICommand<T>`, `IQuery<T>`, and `IStreamQuery<T>` | It states a message's result type so the registry can bind the second parameter of an arity-2 open generic handler without referencing the axis packages. |
| `IIdempotencyStore` | `PostgreSqlIdempotencyStore` (`LiteBus.Commands.Storage.PostgreSql`) and `MartenIdempotencyStore` (`LiteBus.Commands.Storage.Marten`), with `InMemoryIdempotencyStore` in `LiteBus.Testing.Mediation` for tests | It remembers applied idempotency keys with fingerprints, in-flight answers, and claim-timeout takeover. See 4.9. |
| `IdempotencyKeyResolver` | itself (singleton) | It resolves `"{Scope ?? FullName}:{key}"` from a message's `IdempotencyDeclaration` and prefixes an ambient tenant as a third segment. Each segment escapes `\` and `:`. A blank scope or blank key throws `IdempotencyConfigurationException`. |
| `IMessageDependencies` | `MessageDependencies` (internal) | It filters descriptors by handler predicate and tag intersection, orders them by `Priority` then `RegistrationSequence`, and wraps each in a `LazyHandler`. It precomputes a bitmask of occupied pre-stages. Its members are `MainHandlers` and `IndirectMainHandlers` (`ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor>`), `PreStageHandlers` and `IndirectPreStageHandlers` (`ILazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor>`), `PostHandlers` and `IndirectPostHandlers` (`IMessagePostHandler`, `IPostHandlerDescriptor`), `ErrorHandlers` and `IndirectErrorHandlers` (`IMessageErrorHandler`, `IErrorHandlerDescriptor`), `RefusalMappers` and `IndirectRefusalMappers` (`IMessageRefusalMapper`, `IRefusalMapperDescriptor`), merged priority-ordered `CompletionHandlers` (`IMessageCompletionHandler`, `ICompletionHandlerDescriptor`, with no indirect split), `FaultRefusalClassifiers` (`IReadOnlyList<IFaultRefusalClassifier>`, resolved lazily from the dispatch scope and empty by default), and `bool HasPreStageHandlers(PreStage stage)` with a default implementation. |
| `IAuditScope` | `AmbientAuditScope` (internal, singleton instance) | It stores `AuditScopeState` under execution-context item key `__LiteBus.Audit.Scope`. |
| `IAuditRecordWriter` | `AuditRecordWriter` (internal, scoped) | It builds an `AuditRecord` from the message's `AuditedDeclaration`, `IAuditScope`, `IAuditOutcomeMapper`, and `TimeProvider`. |
| `IAuditOutcomeMapper` | `DefaultAuditOutcomeMapper` | It maps `MediationOutcome` to `AuditOutcome` with no application knowledge. |
| `IAuditTrail` | none shipped (application supplied) | The application registers it through `MessageModuleBuilder.UseAuditTrail`. |
| `IMessageContractResolver` | `DeclaredTypeMessageContractResolver` (optional) | When it is unregistered, the default behaviour uses the runtime instance type. |
| `IHandleContextData` | `HandleContextData` | It is a type-keyed store reached through `IExecutionContext.Data`, with one instance per mediation guarded by a lock. |
| `IUnitOfWork` | application supplied. `MartenUnitOfWork` (`LiteBus.Commands.Storage.Marten`) commits the scoped Marten session | `CommitAsync` runs once per mediation that ended `Succeeded`, from a completion handler at `HandlerPriorities.UnitOfWork`. The command and query builders register it with `UseUnitOfWork<T>`. |
| `IDomainEventCollector` | `DomainEventCollector` (internal, in `LiteBus.Outbox`) | It is scoped per mediation. `Collect` records a domain event and `Drain` returns every collected event destructively. The staging completion handler registered with `OutboxModuleBuilder.StageCollectedEvents()` drains them into `ITransactionalOutbox`. |
| `IFaultRefusalClassifier` | application supplied. `PostgreSqlConstraintRefusalClassifier` (`LiteBus.Storage.PostgreSql`) maps unique (23505) and exclusion (23P01) violations on configured constraints to denials | Each entry is a singleton collection item registered with `MessageModuleBuilder.UseFaultRefusalClassifier`. The first match wins, cancellation is never classified, and nothing changes when none is registered. |
| `DeclarationGuard<TMessage, TValue>` and `AuthorizationGuard<TMessage>` | abstract base classes, subclassed by the application | They look the declaration up for the runtime message type through `IMessageMetadataAccessor`. A recorded exemption allows the message, and an undeclared message is denied with code `declaration.missing` unless `DecideUndeclaredAsync` is overridden. `AuthorizationGuard` seals the decision so an exempt message is allowed and an authorized one delegates to `AuthorizeAsync(policy, message, ct)`. |

#### Semantic facades

| Interface | Implementation | Strategy used |
| --- | --- | --- |
| `ICommandMediator` | `CommandMediator` | `SingleAsyncHandlerMediationStrategy<ICommand>` and `<ICommand<TResult>, TResult>` with `ActualTypeOrFirstAssignableTypeMessageResolveStrategy`. |
| `IQueryMediator` | `QueryMediator` | `SingleAsyncHandlerMediationStrategy<IQuery<TResult>, TResult>` for `QueryAsync`, and `SingleStreamHandlerMediationStrategy<IStreamQuery<TResult>, TResult>` for `StreamAsync`. |
| `IEventMediator` | `EventMediator` | `AsyncBroadcastMediationStrategy<IEvent>` and `<TEvent>`. |

#### Runtime / composition (`LiteBus.Runtime.Abstractions` -> `LiteBus.Runtime`)

| Interface | Implementation |
| --- | --- |
| `ILiteBusBuilder` | `LiteBusBuilder` |
| `IModuleRegistry` | `ModuleRegistry` (internal, with DFS topological sort, cycle detection, and composite child expansion) |
| `IModuleConfiguration` | `ModuleConfiguration` (internal) |
| `IDependencyRegistry` | `DependencyRegistry`, `MicrosoftDependencyRegistryAdapter`, `AutofacDependencyRegistryAdapter` (all share `DependencyRegistrationTracker`) |
| `IMessageDispatchScopeFactory` | `MicrosoftMessageDispatchScopeFactory`, `AutofacMessageDispatchScopeFactory`, `RootMessageDispatchScopeFactory` |
| `IStartupTask` | `PostgreSqlInboxSchemaInitializer`, `PostgreSqlOutboxSchemaInitializer`, `PostgreSqlSagaSchemaInitializer`, `InboxObservableMetricsInitializer`, `OutboxObservableMetricsInitializer`, `TransportObservableMetricsInitializer` |
| `IBackgroundService` | `InboxProcessorBackgroundService`, `OutboxProcessorBackgroundService`, `InboxCleanupBackgroundService`, `OutboxCleanupBackgroundService`, `TransportInboxIngressConsumer`, `RecurringCommandBackgroundService<TCommand>`. The processor services wrap the internal `ProcessorBackgroundService<TProcessor>` loop and are driven by `LiteBusHostOrchestrator` (a `BackgroundService`) |
| `IDiagnosticCheck` | `AuditTrailDiagnosticCheck`, `IdempotencyStoreDiagnosticCheck`, `PostgreSqlInboxSchemaDiagnosticCheck`, `PostgreSqlOutboxSchemaDiagnosticCheck`, `PostgreSqlIdempotencySchemaDiagnosticCheck` (`idempotency.postgresql.schema`), `PostgreSqlRecurringLeaseSchemaDiagnosticCheck` (`recurring.postgresql.schema`), `AmqpConnectivityDiagnosticCheck`, `KafkaConnectivityDiagnosticCheck`, `AwsSqsConnectivityDiagnosticCheck`, `AzureServiceBusConnectivityDiagnosticCheck` |

#### Durable messaging

| Interface | Implementation(s) |
| --- | --- |
| `IInbox` | `Inbox` |
| `ITransactionalInbox` | `StoreBoundTransactionalInbox` (PostgreSQL ambient transaction) |
| `ITransactionalInbox<TContext>` | `TransactionalInbox<TContext>` (EF Core) |
| `IInboxStore`, `IInboxLeaseStore`, `IInboxStateWriter`, `IInboxDeadLetterStore`, `IInboxRetentionStore`, `IInboxDiagnosticsStore`, `IInboxMessageQuery`, `IInboxPurgeStore`, `IInboxProcessingStore`, and `IInboxOperationsStore` | `InMemoryInboxStore`, `PostgreSqlInboxStore`, `EfCoreInboxStore` |
| `IInboxDispatcher` | `CommandInboxDispatcher` (in-process), `TransportInboxDispatcher` (broker) |
| `IInboxProcessor` | `PipelinedInboxProcessor` |
| `IInboxProcessorControl` | `InboxProcessorControl` |
| `IInboxWorkSignal` | `InboxPollingWorkSignal`, `PostgreSqlInboxWorkSignal` (LISTEN/NOTIFY) |
| `IInboxManager` | `InboxManager` (internal) |
| `IOutbox` | `Outbox` |
| `ITransactionalOutbox` and `ITransactionalOutbox<TContext>` | `StoreBoundTransactionalOutbox`, `TransactionalOutbox<TContext>`, `MartenTransactionalOutbox` (Marten `IDocumentSession`) |
| `IOutboxStore` and role interfaces | `InMemoryOutboxStore`, `PostgreSqlOutboxStore`, `EfCoreOutboxStore` |
| `IOutboxDispatcher` | `EventOutboxDispatcher` (in-process), `TransportOutboxDispatcher` (broker) |
| `IOutboxProcessor` | `PipelinedOutboxProcessor` |
| `IOutboxProcessorControl` | `OutboxProcessorControl` |
| `IOutboxWorkSignal` | `OutboxPollingWorkSignal`, `PostgreSqlOutboxWorkSignal` |
| `IOutboxManager` | `OutboxManager` (internal) |
| `IProcessorEnvelopeHook` | `SagaProcessorHook` |
| `ISagaStore` | `InMemorySagaStore`, `PostgreSqlSagaStore` |
| `ISagaContext` | `SagaExecutionContext` |
| `IRecurringCommandLease` | `PostgreSqlRecurringCommandLease` (`LiteBus.Commands.Recurring.Storage.PostgreSql`), which is a renewable lease row in `litebus_recurring_leases` | `TryAcquireAsync(leaseName, ct)` returns an `IRecurringCommandLeaseHandle` or null. Disposing the handle releases the lease, and its `LeaseLost` token fires when the lease is lost while held. The lease is acquired before each recurring pass when `RecurringCommandHostOptions.RequireLease` is set. |

#### Transport

| Interface | Implementation(s) |
| --- | --- |
| `ITransportPublisher` | `InMemoryPublisher`, `AmqpPublisher`, `KafkaPublisher`, `SqsPublisher`, `AzureServiceBusPublisher`, and the `TestMessageTransport` double |
| `IMessageConsumer` | `InMemoryConsumer`, `AmqpConsumer`, `KafkaConsumer`, `SqsConsumer`, `AzureServiceBusConsumer` |
| `ITenantRoutingStrategy` | none shipped (application supplied, optional) |
| `ITransportCircuitBreaker` and `ITransportCircuitBreakerRegistry` | `TransportCircuitBreaker` and `TransportCircuitBreakerRegistry` |
| `IPayloadEncryptor` and `IContextualPayloadEncryptor` | none shipped (application supplied). `IInboxPayloadProtector` and `IOutboxPayloadProtector` wrap it per axis |

### 1.3 The mediation pipeline in exact order

```text
IMessageMediator.Mediate
  |- new ExecutionContext(tags, items, cancellationToken)
  |- AmbientExecutionContext.CreateScope(executionContext)
  |- new LazyMessageDispatchScope(factory)               (host scope created on first service resolution)
  |- IMessageResolveStrategy.Find(runtimeType, reader)
  |     (if null and RegisterPlainMessagesOnSpot -> IMessageWriter.Register then retry)
  |     (if still null -> NoHandlerFoundException or MessageDescriptorNotFoundException)
  |- new MessageDependencies(...)  filter by predicate + tags, order by Priority then RegistrationSequence
  |- IMessageMediationStrategy.Mediate(message, dependencies, executionContext)
  |     |- RunAsyncPreStages
  |     |     1. PreStage.Guard      -> IMessageGuard<T>.DecideAsync      -> Verdict   (StopAtFirst)
  |     |     2. PreStage.Validator  -> IMessageValidator<T>.ValidateAsync -> Validity  (CollectFailures: all run)
  |     |     3. PreStage.Shortcut   -> IMessageShortcut<T>[,R].TryAnswerAsync -> Shortcut (StopAtFirst: first answer or denial)
  |     |     4. PreStage.PreHandler -> IMessagePreHandler<T>.PreHandleAsync  (cannot stop)
  |     |     Within each stage: indirect (base-type) handlers run BEFORE direct handlers.
  |     |- if decision.StopsPipeline
  |     |     - refusal (Denied/Invalid, from a guard, a validator, or a Shortcut.Deny): try IMessageRefusalMapper<T,R> -> value; else throw
  |     |       LiteBusMessageDeniedException / LiteBusMessageInvalidException
  |     |     - answered: PipelineDecision.ResolveResult<R>(messageType)
  |     |- main handler (exactly one for command/query; all for events grouped by priority; invoked through the closed contract on its descriptor with the mediation token)
  |     |- RunAsyncPostHandlers (direct then indirect; stops if IExecutionContext.SuppressPostHandlers was called)
  |     |- on catch: classify with IFaultRefusalClassifier entries first (a recognized fault ends as that refusal); else RunAsyncErrorHandlers (indirect then direct), rethrow unless MessageErrorOutcome.Handled
  |     |- finally: RunAsyncCompletionHandlers (one priority-ordered pass) with CancellationToken.None; a fault escaping completion is classified the same way
  |- executionScope.Dispose() before returning         (caller ambient context restored)
  |- MediationScopeRetention.RetainUntilPipelineCompletes(result, resourceScope)
```

The code enforces these rules:

* The declaration order of the `PreStage` enum fixes the stage order, and priorities cannot change it.
* `HandlerPriorityAttribute` orders handlers within one stage or role only, in ascending order. Ties break on `RegistrationSequence` (module registration order).
* Every role except completion runs its direct (message-type) handlers and its indirect (base-type) handlers as two separate passes. The completion stage merges both descriptor sets into one `IMessageDependencies.CompletionHandlers` collection sorted by `Priority` then `RegistrationSequence`, so priority is the only ordering rule there. `IMessageDependencies` has no `IndirectCompletionHandlers`. `IMessageDescriptor` still separates them at the registry level.
* When no handler occupies a pre-stage, the pipeline skips that stage (`IMessageDependencies.HasPreStageHandlers` bitmask).
* `MediationExceptionFilters.IsRecoverableMediationException` excludes `NoHandlerFoundException`, `MultipleHandlerFoundException`, `OperationCanceledException`, `IdempotencyConfigurationException`, and refusals (`LiteBusMessageDeniedException`, `LiteBusMessageInvalidException`) from error handlers.
* `MediationExceptionFilters.IsRefusal(Exception)` returns true for `LiteBusMessageDeniedException` or `LiteBusMessageInvalidException`.
* `MediationExceptionFilters.IsCallerCancellation(Exception, CancellationToken)` returns true when the token is cancelled and the exception or one of its inner exceptions is an `OperationCanceledException`. Cancellation from any other token returns false.
* `MessageContextExtensions.RunAsyncDecisionStages(messageDependencies, message, cancellationToken)` runs guards then validators in fixed order and stops there. It skips shortcut, which claims keys, and pre-handler, which does work. It is a prefix of the fixed order rather than an arbitrary subset, so a shortcut never answers ahead of a guard.
* `MessageContextExtensions.ResolveRefusalResult<TMessageResult>(messageDependencies, message, decision)` (carries `[RequiresUnreferencedCode]`) throws `ArgumentException` when `!decision.IsRefusal`. It selects the direct then indirect mapper whose `MessageResultType` is assignable to `TMessageResult`, throws `PipelineContractException` when two mappers apply at the same level, and throws `decision.CreateRefusalException` when no mapper applies. A second overload takes a `Refusal` and the causing exception for a classified fault.
* `MediationExceptionFilters.TryClassifyRefusal(classifiers, exception, out refusal)` consults the registered `IFaultRefusalClassifier` entries in order, and the first match wins. It never offers a cancellation or an exception that already carries a refusal. A fault from any pre-completion stage that a classifier recognizes ends the mediation as that refusal. Error handlers do not run, completion handlers observe the refusal outcome, `TrySendAsync` returns it as a value, and refusal mappers apply. A fault escaping the completion stage that a classifier recognizes only changes what the caller receives and the `Try` capture. Handlers that already ran keep the outcome they observed, the rest never run, and no refusal mapper is consulted.
* Completion handlers run on every path and are never cancelled. A completion handler that throws while the mediation had already failed has its exception collected into `exception.Data[MediationExceptionData.SuppressedCompletionFaults]` (`"LiteBus.SuppressedCompletionFaults"`) instead of replacing the original fault.

### 1.4 Lifecycle and thread safety

| Component | Lifetime | Thread safety |
| --- | --- | --- |
| `IMessageRegistry`, `IMessageReader`, `IMessageWriter` | **Singleton instance** created at composition time | Yes. All mutations and reads take a private lock. |
| `IMessageContractRegistry`, `IContractReader`, `IContractWriter` | **Singleton instance** | Yes. A private lock guards both lookup dictionaries. |
| `TimeProvider` | **Singleton instance** (`TimeProvider.System` unless overridden) | Yes. |
| `IAuditScope` (`AmbientAuditScope`) | **Singleton instance**. State is per-mediation through `AsyncLocal` items | Yes for the accessor. State is logically per-flow. |
| `IAuditOutcomeMapper` | **Singleton instance** | Thread safety is the application's responsibility. |
| `IAuditTrail` | **Scoped** when registered by type, **Singleton** when registered as an instance | Thread safety is the application's responsibility. |
| `IAuditRecordWriter` | **Scoped** (factory) | - |
| `IMessageMediator`, `ICommandMediator`, `IQueryMediator`, `IEventMediator` | **Transient** | Stateless and safe to resolve anywhere. |
| Every discovered handler type | **Scoped** (registered as itself) | One instance per dispatch scope. |
| `IMessageDispatchScopeFactory` | **Singleton** | Yes. |
| `IExecutionContext` | **Scoped** (factory over the ambient context) | It resolves the context of the mediation in flight. `AmbientExecutionContext` remains for code outside DI. `HandledAt` is read once from the messaging `TimeProvider` when the mediation starts. |
| `IUnitOfWork` | **Scoped** (self and first-wins interface mapping) | It is registered only when `UseUnitOfWork<T>` was called. Each axis commits its own implementation. |
| `IDomainEventCollector` | **Scoped** | It is registered only when `OutboxModuleBuilder.StageCollectedEvents()` was called. There is one collector per mediation. |
| `IFaultRefusalClassifier` entries | **Singleton** collection items | Entries must be stateless and thread-safe. They are consulted in registration order on fault paths only. |
| In-memory and PostgreSQL stores | **Singleton instance** | Yes. In-memory stores use locks and concurrent collections, while PostgreSQL stores are stateless over `NpgsqlDataSource` and container-owned through `PostgreSqlDataSourceOwner<TModule>`, so a `UseConnectionString` data source is disposed with the provider. The PostgreSQL idempotency store and the Marten idempotency store are Singletons, and the recurring PostgreSQL lease is a Singleton. |
| EF Core stores | **Singleton** over `IDbContextFactory<TContext>` | Each operation creates its own `DbContext`. |
| `ITransactionalInbox` and `ITransactionalOutbox` (plus generic variants) | **Scoped** | They are bound to the caller's unit of work. |
| Processor options records (`InboxProcessorOptions`, `OutboxProcessorOptions`, host and cleanup options) | **Singleton instances** captured from the builder | Treat as immutable after composition. |
| `InboxProcessorControl` and `OutboxProcessorControl` | **Singleton instance** (also as `IInboxProcessorControl` and `IOutboxProcessorControl`) | Yes, through a lock and `TaskCompletionSource`. |
| `IInboxProcessor` and `IOutboxProcessor` | **Transient** (factory) | Each resolution creates a new processor, and each processor generates its own lease-owner suffix. |
| Background services and startup tasks | **Singleton** | Each is registered once and deduplicated by type. |
| Transport clients (`IAmqpConnectionManager`, `KafkaProducerOwner`, `IConsumer<string, byte[]>`, `IAmazonSQS`, `ServiceBusClient`, `InMemoryTransportBroker`) | **Singleton** | They are owned by the transport module. The Kafka producer is flushed once when the container is disposed. |
| `ITransportPublisher` and `IMessageConsumer` | **Singleton** publishers and **Transient** consumers in all five adapters | Each consumer instance holds one subscription. `IAmqpPublisher` is a separate Transient `AmqpPublisher` registration that resolves to a different instance than the Singleton `ITransportPublisher`. |
| `ISagaStateTypeRegistry`, `SagaExecutionContext`, `ISagaContext`, `IProcessorEnvelopeHook` | **Singleton** | `SagaExecutionContext` keeps per-dispatch state in async-local storage. |

The async execution model runs `Task`-based code with `ConfigureAwait(false)` on every performance-critical path. Streaming queries return `IAsyncEnumerable<T>`, and the dispatch scope stays alive through `MediationScopeRetention.ScopeRetainedAsyncEnumerable<T>`, which **allows only one enumeration** (a second `GetAsyncEnumerator` throws `InvalidOperationException`). The mediator returns the raw strategy result wrapped so that scope disposal happens after the task completes or the stream is fully enumerated or disposed.

### 1.5 Package map

| Package | Contains |
| --- | --- |
| `LiteBus` | Meta-package holding Messaging, Commands, Queries, Events, Inbox, Outbox, their abstractions, and `LiteBus.DurableMessaging.Abstractions`. |
| `LiteBus.Messaging.Abstractions` | Handler contracts, pipeline value types, contracts, metadata, audit contracts, exceptions. |
| `LiteBus.Messaging` | Registry, mediator, mediation strategies, pipeline runner, audit writer, serializer, and shared durable processor support code. |
| `LiteBus.Commands[.Abstractions]`, `LiteBus.Queries[.Abstractions]`, `LiteBus.Events[.Abstractions]` | Semantic axes. |
| `LiteBus.Commands.Recurring` | It runs recurring commands through a hosted loop that sends one command on a fixed interval (`AddRecurring`, `RecurringCommandBackgroundService<TCommand>`), with opt-in single-flight through `IRecurringCommandLease` (`RequireLease` and `LeaseName`). |
| `LiteBus.Commands.Storage.PostgreSql` | It provides a PostgreSQL `IIdempotencyStore` for the command axis (`commands.UsePostgreSqlIdempotencyStore(...)`), with a versioned schema and the `idempotency.postgresql.schema` probe. |
| `LiteBus.Commands.Recurring.Storage.PostgreSql` | It provides a PostgreSQL `IRecurringCommandLease` backed by a renewable lease row in `litebus_recurring_leases` (`commands.UsePostgreSqlRecurringLease(...)`), with the `recurring.postgresql.schema` probe. |
| `LiteBus.Commands.Storage.Marten` | Marten command-axis storage with `MartenUnitOfWork` (scoped session commit), `MartenIdempotencyStore` (fingerprint claims as Marten documents), and `MartenAuditTrail` (success records staged in the handler session, other outcomes out of band). |
| `LiteBus.{Commands,Queries,Events,Messaging}.Extensions.{Microsoft.DependencyInjection,Autofac}` | Empty reference-only install shells, with one NuGet ID per semantic module. |
| `LiteBus.Runtime.Abstractions`, `LiteBus.Runtime` | Modules, dependency descriptors, diagnostics, hosting manifest. |
| `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | `AddLiteBus(this IServiceCollection, ...)` |
| `LiteBus.Runtime.Extensions.Autofac` | `AddLiteBus(this ContainerBuilder, ...)` |
| `LiteBus.Extensions.Microsoft.DependencyInjection` | Code-less convenience meta-package whose only content is references to `LiteBus.Commands.Extensions.Microsoft.DependencyInjection`, `LiteBus.Events.Extensions.Microsoft.DependencyInjection`, `LiteBus.Messaging.Extensions.Microsoft.DependencyInjection`, and `LiteBus.Queries.Extensions.Microsoft.DependencyInjection`. |
| `LiteBus.Runtime.Extensions.Hosting`, `...Microsoft.Hosting`, `...Autofac.Hosting` | It provides `IHostedService` orchestration for manifest startup tasks and background services. |
| `LiteBus.DurableMessaging.Abstractions` | Retry, idempotency, visibility, trace, tenancy, processor options and results, payload encryption, and processor hooks. |
| `LiteBus.Inbox[.Abstractions]`, `LiteBus.Outbox[.Abstractions]` | Durable axes. |
| `LiteBus.Inbox.Storage.{InMemory,PostgreSql,EntityFrameworkCore}` and outbox equivalents, plus `LiteBus.Outbox.Storage.Marten` | Storage adapters. The Marten package stages outbox rows on the current Marten `IDocumentSession` (`MartenTransactionalOutbox`). |
| `LiteBus.Inbox.Dispatch[.{InProcess,InMemory,Amqp,Kafka,AwsSqs,AzureServiceBus}]` and outbox equivalents | Dispatch adapters. |
| `LiteBus.Inbox.Ingress[.{InMemory,Amqp,Kafka,AwsSqs,AzureServiceBus}]` | Ingress from a broker into the inbox. |
| `LiteBus.Transport.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.{InMemory,Amqp,Kafka,AwsSqs,AzureServiceBus}` | Broker-neutral transport and its adapters. |
| `LiteBus.Storage.PostgreSql`, `LiteBus.Storage.EntityFrameworkCore` | Shared relational helpers (schema bootstrap, lease SQL, provider detection). |
| `LiteBus.Saga[.Abstractions]`, `LiteBus.Saga.InboxIntegration`, `LiteBus.Saga.Storage.PostgreSql` | Saga orchestration. |
| `LiteBus.Extensions.AspNetCore` | Operator management HTTP endpoints and the idempotency-key endpoint filter (`WithIdempotencyKey`, `IdempotencyKeyGuard`, `MediationResult.ToHttpResult`). |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | `IHealthChecksBuilder.AddLiteBus(...)` |
| `LiteBus.Messaging.Extensions.OpenTelemetry`, `LiteBus.Inbox.Extensions.OpenTelemetry`, `LiteBus.Outbox.Extensions.OpenTelemetry`, `LiteBus.Transport.Extensions.OpenTelemetry`, `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` | OpenTelemetry registration helpers. |
| `LiteBus.Testing`, `LiteBus.Testing.Mediation`, `LiteBus.Testing.Hosting`, `LiteBus.Testing.Transport`, `LiteBus.Testing.DurableMessaging` | Test doubles and fixtures. |
| `LiteBus.Analyzers` | Roslyn analyzers `LB1001` to `LB1024` (`LB1002` and `LB1006` reserved, never shipped). |

---
## 2. Global Setup & Dependency Injection

### 2.1 Microsoft.Extensions.DependencyInjection

`LiteBus.Extensions.Microsoft.DependencyInjection.ServiceCollectionExtensions`:

```csharp
public static IServiceCollection AddLiteBus(
    this IServiceCollection services,
    Action<ILiteBusBuilder> configure);
```

What it does, in order:

1. Throws `ModuleCompositionException` when `AddLiteBus` was already called on this collection, detected by an existing `LiteBusHostManifest` descriptor. Each service collection hosts a single LiteBus composition.
2. Wraps `services` in a `MicrosoftDependencyRegistryAdapter`.
3. Registers `IMessageDispatchScopeFactory` with implementation `MicrosoftMessageDispatchScopeFactory` as **Singleton**. This registration is why `MessageModule.Build` can assert its presence.
4. Creates a `ModuleRegistry`, wraps it in `LiteBusBuilder`, and invokes `configure`.
5. Calls `moduleRegistry.BuildOrder()`, which freezes registration and topologically sorts the modules, then calls `Build(moduleConfiguration)` on each module in order.
6. Invokes each `moduleConfiguration.CompositionValidations` delegate. Rules that span several modules run only after every module has registered what it owns.
7. Registers `LiteBusHostManifest` (Singleton) built from the module configuration.
8. Calls `services.RegisterDiagnosticChecks(moduleConfiguration.DiagnosticChecks)`, which registers each probe type AsSelf Singleton and skips exact duplicates. It throws `ModuleCompositionException` when one implementation type is listed under two probe names or when two implementation types share one probe name.
9. Calls `services.RegisterBackgroundServices(moduleConfiguration.StartupTasks, moduleConfiguration.BackgroundServices)`. When both lists are empty after order-preserving deduplication, nothing is registered. Otherwise it registers a Singleton `BackgroundServiceHostedServiceIndex`, each task and service type AsSelf Singleton, and one Singleton `IHostedService` (`LiteBusHostOrchestrator`) whose factory resolves the tasks and services and records each background service in the index.

`InstanceLifetime` maps to `ServiceLifetime` one to one (`Transient` to `Transient`, `Singleton` to `Singleton`, `Scoped` to `Scoped`). Any other value throws `ArgumentOutOfRangeException`. Both host-registration methods are public on `IServiceCollection` (`LiteBus.Runtime.Extensions.Microsoft.Hosting`) and directly callable: `RegisterBackgroundServices(this IServiceCollection services, IReadOnlyList<Type> startupTasks, IReadOnlyList<Type> backgroundServices)` and `RegisterDiagnosticChecks(this IServiceCollection services, IReadOnlyList<DiagnosticCheckDescriptor> diagnosticChecks)`.

```csharp
using LiteBus.Commands;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Queries;

builder.Services.AddLiteBus(liteBus =>
{
    var assembly = typeof(PlaceOrderCommand).Assembly;

    liteBus.AddMessaging(messaging => messaging.UseTimeProvider(TimeProvider.System));
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(assembly));
    liteBus.AddQueries(queries => queries.RegisterFromAssembly(assembly));
    liteBus.AddEvents(events => events.RegisterFromAssembly(assembly));
});
```

### 2.2 Autofac

`LiteBus.Extensions.Autofac.ContainerBuilderExtensions`:

```csharp
public static ContainerBuilder AddLiteBus(this ContainerBuilder builder, Action<ILiteBusBuilder> configure);
```

Compared with Microsoft DI, it first registers `IServiceProvider` with implementation `AutofacServiceProviderAdapter` as `InstancePerLifetimeScope`, so factory descriptors can resolve services. It then registers `IMessageDispatchScopeFactory` with implementation `AutofacMessageDispatchScopeFactory` (Singleton). Lifetime translation maps `Transient` to `InstancePerDependency`, `Singleton` to `SingleInstance`, and `Scoped` to `InstancePerLifetimeScope`. Open generic implementation types are registered with `RegisterGeneric`.

A second `AddLiteBus` call on the same `ContainerBuilder` throws `ModuleCompositionException`, detected by a composition marker in `builder.Properties`. Each container builder hosts a single LiteBus composition. After building every module in `BuildOrder()`, `AddLiteBus` invokes each `moduleConfiguration.CompositionValidations` delegate before registering the manifest, diagnostic checks, and background services.

Host registration delegates to the public, directly callable `ContainerBuilder` methods `RegisterBackgroundServices(this ContainerBuilder builder, IReadOnlyList<Type> startupTasks, IReadOnlyList<Type> backgroundServices)` and `RegisterDiagnosticChecks(this ContainerBuilder builder, IReadOnlyList<DiagnosticCheckDescriptor> diagnosticChecks)` (`LiteBus.Runtime.Extensions.Autofac.Hosting`). The diagnostic method registers probe types AsSelf `SingleInstance` and skips exact duplicates. It throws `ModuleCompositionException` when one implementation type is listed under two probe names or when two implementation types share one probe name. When both the startup-task and background-service lists are empty after order-preserving deduplication, nothing is registered. Otherwise each type is registered AsSelf `SingleInstance` plus one `SingleInstance` `IHostedService` (`LiteBusHostOrchestrator`). The Autofac path registers no `BackgroundServiceHostedServiceIndex`.

```csharp
var containerBuilder = new ContainerBuilder();
containerBuilder.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly));
});
var container = containerBuilder.Build();
var commandMediator = container.Resolve<ICommandMediator>();
```

### 2.3 Composition entry points

`ILiteBusBuilder` extension methods for the messaging axes and transports. The axis methods also have an `IModuleRegistry` form, listed below. The transport methods do not, and recurring commands compose through `CommandModuleBuilder`. `ILiteBusBuilder` exposes `IModuleRegistry Modules { get; }`, the registry that collects modules before graph validation and build, which feature-specific composition extensions use:

| Method | Package | Registers |
| --- | --- | --- |
| `AddMessaging(Action<MessageModuleBuilder>)` | `LiteBus.Messaging` | `MessageModule` |
| `AddCommands(Action<CommandModuleBuilder>)` | `LiteBus.Commands` | `CommandModule` (composite, requires `MessageModule`) |
| `AddQueries(Action<QueryModuleBuilder>)` | `LiteBus.Queries` | `QueryModule` (requires `MessageModule`) |
| `AddEvents(Action<EventModuleBuilder>)` | `LiteBus.Events` | `EventModule` (requires `MessageModule`) |
| `AddInbox(Action<InboxModuleBuilder>)` | `LiteBus.Inbox` | `InboxModule` (composite, requires `MessageModule`) |
| `AddOutbox(Action<OutboxModuleBuilder>)` | `LiteBus.Outbox` | `OutboxModule` (composite, requires `MessageModule`) |
| `commands.AddRecurring<TCommand>(...)` | `LiteBus.Commands.Recurring` | `RecurringCommandModule<TCommand>` as a child of `CommandModule`, and `RecurringCommandBackgroundService<TCommand>` on the host manifest |
| `AddInMemoryTransport()` and `AddInMemoryTransport(InMemoryTransportOptions)` | `LiteBus.Transport.InMemory` | `InMemoryTransportModule`. The parameterless form delegates with `new InMemoryTransportOptions()` |
| `AddAmqpTransport(AmqpConnectionOptions)` | `LiteBus.Transport.Amqp` | `AmqpTransportModule` |
| `AddKafkaTransport(KafkaTransportOptions)` | `LiteBus.Transport.Kafka` | `KafkaTransportModule` |
| `AddAwsSqsTransport(AwsSqsTransportOptions)` | `LiteBus.Transport.AwsSqs` | `AwsSqsTransportModule` |
| `AddAzureServiceBusTransport(AzureServiceBusTransportOptions)` | `LiteBus.Transport.AzureServiceBus` | `AzureServiceBusTransportModule` |

`IModuleRegistry` extensions: `AddMessageModule`, `AddCommandModule` and `AddQueryModule` (with a builder action only, and a null action throws `ArgumentNullException`), `AddEventModule`, `AddInboxModule` and `AddOutboxModule` (with and without a builder action, where the parameterless `AddEventModule()` delegates to the with-action overload with an empty action). `IModuleRegistry.Register(IModule module)` accepts any module directly and returns the registry for chaining. `IsModuleRegistered<T>()` reports whether a module type is present, checking exact type matches only, without considering inheritance relationships.

### 2.4 Exactly what each module registers

**`MessageModule`** takes `public MessageModule(Action<MessageModuleBuilder> builder)`, throws `ArgumentNullException` when the action is null, and `Build` throws `ModuleCompositionException` when no `IMessageDispatchScopeFactory` descriptor exists yet:

| Service | Implementation | Lifetime |
| --- | --- | --- |
| `IMessageRegistry`, `IMessageReader`, `IMessageWriter` | the same `MessageRegistry` instance | Singleton (instance) |
| `IMessageCatalog` | Singleton factory (`_ => new MessageCatalog(messageRegistry)`). The snapshot is taken on first resolve, after every module has registered its messages | Singleton (factory) |
| `IMessageMetadataAccessor` | `new MessageMetadataAccessor(messageRegistry)` | Singleton (instance) |
| `IMessageContractRegistry`, `IContractReader`, `IContractWriter` | the same `MessageContractRegistry` instance | Singleton (instance) |
| `IMessageSerializer` | `SystemTextJsonMessageSerializer` | Transient |
| `TimeProvider` | builder value or `TimeProvider.System` | Singleton (instance) |
| `IExecutionContext` | Scoped factory (`static _ => AmbientExecutionContext.Current`) | Scoped (factory) |
| `IAuditScope` | `AmbientAuditScope` | Singleton (instance) |
| `IAuditOutcomeMapper` | builder value or `DefaultAuditOutcomeMapper` | Singleton (instance) |
| `IAuditTrail` | only if `UseAuditTrail` or `UseAuditTrailInstance` was called | The lifetime passed to `UseAuditTrail<T>` (Scoped by default), or Singleton for an instance |
| `IAuditActorResolver` | only when a `UseAuditActorResolver*` builder call was made | The lifetime passed to `UseAuditActorResolver` (Scoped by default), or Singleton for an instance. A missing resolver is not a composition failure |
| `IAuditRecordWriter` | `AuditRecordWriter` factory | Scoped |
| `IdempotencyKeyResolver` | Singleton instance over the registry metadata. The `IIdempotencyStore` itself is registered by the command storage packages or the application, not here | Singleton (instance) |
| `IFaultRefusalClassifier` collection items | one `RegisterCollection` or `ForCollection` entry per `UseFaultRefusalClassifier` call | Singleton each. Nothing is registered when the list is empty |
| `IMessageMediator` | `MessageMediator` | Transient |
| `LiteBusCompositionSummary` | Singleton instance shared through the module context and filled in by a composition validation after every module has built | Singleton (instance) |
| every newly discovered handler type | the handler type itself | Scoped |

**`CommandModule`** registers `ICommandMediator` with implementation `CommandMediator` (Transient) and every handler type discovered in this module as Scoped. It registers `AuditTrailDiagnosticCheck` (`litebus.audit.trail`) when `EnableAuditing()` was called. When `UseUnitOfWork<T>` was called, it registers the `TUnitOfWork` type as itself plus a first-wins `IUnitOfWork` mapping, both Scoped. Child modules registered through `RegisterChildModule` (the PostgreSQL idempotency store, the Marten idempotency store, the PostgreSQL recurring lease) build after the parent.

**`QueryModule`** has the same shape with `IQueryMediator` implemented by `QueryMediator`, including the `UseUnitOfWork<T>` registration. A different unit-of-work type per axis is allowed.

**`EventModule`** registers `IEventMediator` with implementation `EventMediator` (Transient) and its handlers. It registers `AuditTrailDiagnosticCheck` (`litebus.audit.trail`) when `EnableAuditing()` was called, including when `AddAuditing` selects the events axis (`AuditingComposition.Events == true`, which calls `EnableAuditing()` for the caller).

**`RecurringCommandModule<TCommand>`** builds as follows. `Build` validates the host options, throwing `ArgumentOutOfRangeException` on bad timing and `ArgumentException` on a blank `LeaseName`. It registers the `RecurringCommandDeclaration<TCommand>` instance (Singleton) and `RecurringCommandBackgroundService<TCommand>` as its own Transient type, and calls `configuration.RegisterBackgroundService(typeof(RecurringCommandBackgroundService<TCommand>))` so the loop runs on the host. When `RequireLease` is set, it registers a composition validation that throws `LiteBusConfigurationException` when no `IRecurringCommandLease` is registered.

**`InboxModule`** is composite with `ParentFirst` ordering, and its children are storage, then dispatcher, then ingress, then feature bridges such as saga:

| Service | Notes |
| --- | --- |
| `IMessageContractRegistry`, `IContractReader` | the shared contract registry (idempotent re-registration) |
| `InboxProcessorOptions` | instance from the builder |
| `IInboxPayloadProtector` | only when `UsePayloadEncryption` was called |
| `IInboxEnvelopeFactory` implemented by `InboxEnvelopeFactory` | Transient. Takes an optional `InboxEnvelopeOptions` carrying the ambient-trace opt-in |
| `InboxEnvelopeOptions` | Singleton instance, only when `UseAmbientTrace()` was called |
| `IInbox` implemented by `Inbox` | Transient |
| `InboxCleanupHostOptions`, `InboxRetentionCoordinator` | instances |
| `IInboxManager` implemented by `InboxManager` | Transient |
| `IInboxProcessor` | factory (`InboxProcessorFactory.Create`), Transient |
| `InboxProcessorControl`, `IInboxProcessorControl`, `InboxProcessorHostOptions`, `InboxProcessorBackgroundService` | only when `EnableInboxProcessor()` was called. The background service is added to the host manifest |
| `InboxCleanupBackgroundService` | only when `EnableCleanup()` was called |
| `InboxObservableMetrics` | Singleton factory and `InboxObservableMetricsInitializer` startup task |
| consumer diagnostic probes | one manifest entry per `AddDiagnosticCheck<TCheck>(name)` call. `AddLiteBus` registers each probe type as Singleton once |

When storage is missing, or when the processor is enabled without a dispatcher, `Build` throws `DurableStorageConfigurationException`.

**`OutboxModule`** mirrors the inbox with `OutboxProcessorOptions`, `IOutboxPayloadProtector`, `IOutboxEnvelopeFactory`, `IOutbox`, `OutboxCleanupHostOptions`, `OutboxRetentionCoordinator`, `IOutboxManager`, `IOutboxProcessor`, `OutboxProcessorControl`, `OutboxProcessorHostOptions`, `OutboxProcessorBackgroundService`, `OutboxCleanupBackgroundService`, `OutboxObservableMetrics` and its initializer. It also registers the supplied `IMessageContractResolver` as a Singleton instance when `OutboxModuleBuilder.UseContractResolver()` was called, and one `OutboxEnqueueOptions` Singleton built from the `UseAmbientTrace` and `EnablePerHandlerDelivery` flags. `StageCollectedEvents()` registers the scoped `IDomainEventCollector` and an open-generic completion handler at `HandlerPriorities.Persistence` that drains the collector into `ITransactionalOutbox` when the mediation succeeded.

### 2.5 Direct instantiation without a DI container

`MessageModule` requires an `IMessageDispatchScopeFactory`. For a container-less host, use `RootMessageDispatchScopeFactory`, which exposes a supplied root provider and never disposes it.

`MessageRegistry`, `MessageContractRegistry`, `MessageMediator`, `ModuleRegistry` and `ModuleConfiguration` are all `internal`, so there is no path that wires every piece by hand. `LiteBusBuilder` itself is public: `new LiteBusBuilder(IModuleRegistry modules)` throws `ArgumentNullException` when `modules` is null and exposes it as `IModuleRegistry Modules { get; }`. The container-less pattern is to run the same `AddLiteBus` composition over a plain `ServiceCollection` (no host, no ASP.NET Core):

```csharp
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(commands => commands.Register<PlaceOrderCommandHandler>());
});

await using var provider = services.BuildServiceProvider();

var mediator = provider.GetRequiredService<ICommandMediator>();
await mediator.SendAsync(new PlaceOrderCommand(cartId));
```

In a custom host that owns its own provider and wants the root provider to serve every dispatch (no per-message scope), register `RootMessageDispatchScopeFactory` before the modules build:

```csharp
IDependencyRegistry registry = /* your adapter */ null!;
registry.Register(new DependencyDescriptor(
    typeof(IMessageDispatchScopeFactory),
    serviceProvider => new RootMessageDispatchScopeFactory(serviceProvider),
    InstanceLifetime.Singleton));
```

Public types that *can* be constructed directly (useful in tests and custom hosts):

```csharp
// Mediators over any IMessageMediator
var commandMediator = new CommandMediator(messageMediator);
var queryMediator   = new QueryMediator(messageMediator);
var eventMediator   = new EventMediator(messageMediator);

// Serializer with custom JSON options
var serializer = new SystemTextJsonMessageSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true
});

// Durable writers over a store
var envelopeFactory = new InboxEnvelopeFactory(contractReader, serializer, TimeProvider.System);
var inbox = new Inbox(inboxStore, envelopeFactory);

var outboxFactory = new OutboxEnvelopeFactory(contractReader, serializer, TimeProvider.System);
var outbox = new Outbox(outboxStore, outboxFactory);

// Stores
var inMemoryInbox  = new InMemoryInboxStore(new InMemoryInboxStoreOptions(), TimeProvider.System);
var inMemoryOutbox = new InMemoryOutboxStore(new InMemoryOutboxStoreOptions(), TimeProvider.System);

// Contract registry population without modules
IMessageContractRegistry contracts = /* singleton from AddLiteBus */ null!;
contracts.Register<PlaceOrderCommand>("orders.place", 1);
contracts.RegisterFromAssembly(typeof(PlaceOrderCommand).Assembly);

// Ambient execution context for code that runs outside a mediation
using (AmbientExecutionContext.CreateScope(myExecutionContext))
{
    // AmbientExecutionContext.Current is available here
}
AmbientExecutionContext.ResetForTesting();   // test isolation helper
```

### 2.6 Hosting, health checks and management endpoints

```csharp
// Generic host: AddLiteBus registers LiteBusHostOrchestrator as IHostedService, but only when the manifest holds at least one startup task or background service; with both lists empty no IHostedService is registered at all.
// Health checks:
builder.Services.AddHealthChecks()
    .AddLiteBus(options =>
    {
        options.FailHealthWhenNoProbes = true;
        options.DiagnosticChecks = new DiagnosticCheckRunOptions
        {
            MaxParallelism = 4,
            Timeout = TimeSpan.FromSeconds(5)
        };
    },
    name: "litebus");

// Operator HTTP endpoints:
builder.Services.AddLiteBusManagement(options =>
{
    options.RoutePrefix = "litebus";
    options.AuthorizationPolicy = "ops";
});
var app = builder.Build();
app.AddLiteBusManagementEndpoints();
```

On the Microsoft DI path, `BackgroundServiceHostedServiceExtensions.GetHostedServiceForBackgroundService<TBackgroundService>(this IServiceProvider serviceProvider)` (where `TBackgroundService : class, IBackgroundService`) resolves the `IHostedService` adapter that executes `TBackgroundService`. It first resolves all `IHostedService` entries so the `BackgroundServiceHostedServiceIndex` fills in, then returns the adapter for `TBackgroundService`. It throws `ArgumentNullException` for a null provider and `InvalidOperationException` when no adapter was registered. It only works on the Microsoft DI path, which is the only one that registers the index.

Manual host control for tests (`LiteBus.Testing.Hosting`):

```csharp
await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, ct);
var inboxLoop = LiteBusHostedServiceExtensions.GetInboxProcessorHostedService(provider);
LiteBusHostedServiceExtensions.AssertBackgroundServices(provider, typeof(InboxProcessorBackgroundService));
await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, ct);
```

---

## 3. Configuration & All Options (Exhaustive)

Conventions used below: "Required?" means the compiler or a runtime guard forces a value. `required` marks C# `required` members. Properties declared `{ get; init; }` with no initializer default to `null` (reference or nullable) or the type default (`0`, `false`, `TimeSpan.Zero`, `default(T)`).

### 3.1 Mediation settings

#### `CommandMediationSettings` (`LiteBus.Commands.Abstractions`, sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Routing` | `CommandRoutingSettings` | `new CommandRoutingSettings()` | No | Tag and predicate filters applied to every handler role for this send. |
| `Items` | `IDictionary<string, object>` | `new Dictionary<string, object>()` | No | A get-only collection copied into `IExecutionContext.Items`. It carries out-of-band data to handlers, and inbox dispatch uses it to inject trace metadata. |

#### `CommandRoutingSettings` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Tags` | `IEnumerable<string>` | `[]` (empty) | No | A handler participates when it carries no tags, or when its tags intersect this set. An empty set therefore selects untagged handlers only. |
| `HandlerPredicate` | `Func<IHandlerDescriptor, bool>` | `_ => true` | No | A second filter applied after tag filtering and evaluated per descriptor before the handler instance is resolved. |

#### `QueryMediationSettings` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Routing` | `QueryRoutingSettings` | `new QueryRoutingSettings()` | No | Tag and predicate filter for query handlers. |
| `Items` | `IDictionary<string, object>` | `new Dictionary<string, object>()` | No | Copied into `IExecutionContext.Items`. Unlike the command variant, this property has an `init` accessor. |

#### `QueryRoutingSettings` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Tags` | `IEnumerable<string>` | `[]` | No | Same semantics as `CommandRoutingSettings.Tags`. |
| `HandlerPredicate` | `Func<IHandlerDescriptor, bool>` | `_ => true` | No | Same semantics as `CommandRoutingSettings.HandlerPredicate`. |

#### `EventMediationSettings` (`LiteBus.Events.Abstractions`, sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `ThrowIfNoHandlerFound` | `bool` | `false` | No | When `true`, publishing an event with zero applicable main handlers throws `NoHandlerFoundException`. When `false`, the publish does nothing after the pre-stages. Outbox in-process dispatch sets it to `true` by default (`InProcessOutboxDispatchOptions.RequireHandler`), because a durable dispatch that reached nothing is a failure rather than a success. |
| `AutoRegisterUnregisteredMessageTypes` | `bool` | `false` | No | Sets `MessageMediationRequest.RegisterPlainMessagesOnSpot`. When `true`, an unknown event type is registered in the `IMessageRegistry` at publish time instead of throwing `NoHandlerFoundException`. |
| `Routing` | `EventRoutingSettings` | `new EventRoutingSettings()` | No | Tag and predicate filters. |
| `Execution` | `EventExecutionSettings` | `new EventExecutionSettings()` | No | Concurrency and fault aggregation for the broadcast. |
| `Items` | `IDictionary<string, object>` | `new Dictionary<string, object>()` | No | Copied into `IExecutionContext.Items`. |

#### `EventRoutingSettings` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Tags` | `IEnumerable<string>` | `new List<string>()` | No | Tag filter for event handlers. |
| `HandlerPredicate` | `EventHandlerFilter` (delegate `bool (IHandlerDescriptor)`) | `_ => true` | No | Descriptor-level predicate. |

#### `EventExecutionSettings` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `PriorityGroupsConcurrencyMode` | `ConcurrencyMode` | `Sequential` | No | `Sequential` runs each priority group to completion before the next. `Parallel` starts every priority group at once, discarding priority as an ordering guarantee. |
| `HandlersWithinSamePriorityConcurrencyMode` | `ConcurrencyMode` | `Sequential` | No | Controls whether handlers sharing one priority run one at a time (ordered by `RegistrationSequence`) or concurrently. |
| `FaultMode` | `HandlerFaultMode` | `PropagateFirst` | No | Applies to sequential and parallel execution alike. `PropagateFirst` stops the group at the first fault. `ContinueAndAggregate` runs every handler in the group and throws the single exception, or an `AggregateException` when more than one failed. The mode never carries across priority groups. |

#### `MessageMediationRequest<TMessage, TMessageResult>` (`LiteBus.Messaging.Abstractions`, sealed record)

The low-level request object accepted by `IMessageMediator.Mediate`. Build one directly only when writing a custom axis.

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MessageResolveStrategy` | `IMessageResolveStrategy` | none | **Yes** (`required`) | Chooses the `IMessageDescriptor` for the runtime message type. |
| `MessageMediationStrategy` | `IMessageMediationStrategy<TMessage, TMessageResult>` | none | **Yes** (`required`) | Defines the pipeline shape (single handler, stream, broadcast). |
| `Tags` | `IEnumerable<string>` | none | **Yes** (`required`) | Tag filter. |
| `RegisterPlainMessagesOnSpot` | `bool` | `false` | No | Allows registering an unknown message type during mediation. |
| `Items` | `IDictionary<string, object>` | `new Dictionary<string, object>()` | No | Seed values for `IExecutionContext.Items`. |
| `HandlerPredicate` | `Func<IHandlerDescriptor, bool>` | `_ => true` | No | Descriptor filter. |

### 3.2 Durability and processor settings

#### `RetryOptions` (`LiteBus.Messaging.Abstractions`, sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MaxAttempts` | `int` | `5` | No | When `envelope.AttemptCount >= MaxAttempts` after a dispatch failure, the envelope is dead-lettered instead of retried. Validated `> 0`. |
| `InitialDelay` | `TimeSpan` | `5 seconds` | No | Base delay for attempt 1. Validated `>= TimeSpan.Zero`. |
| `MaxDelay` | `TimeSpan` | `5 minutes` | No | Hard cap applied before and after jitter. Validated `>= TimeSpan.Zero`. |
| `Backoff` | `RetryBackoff` | `Exponential` | No | `Fixed` always uses `InitialDelay`. `Exponential` uses `InitialDelay * 2^(attemptCount-1)`. `Schedule` reads `Delays`. Validated with `Enum.IsDefined`. |
| `Delays` | `IReadOnlyList<TimeSpan>` | `[]` | Only for `Schedule` | The explicit ladder, one delay per retry in order. An attempt past the end repeats the last entry. `MaxDelay` does not clamp these entries, because the ladder is the policy. The list must be non-empty and free of negative entries when `Backoff` is `Schedule`, and it is ignored otherwise. |
| `UseJitter` | `bool` | `true` | No | Multiplies the computed delay by a random factor in `[0.8, 1.2)`, then re-caps at `MaxDelay`. Under `Schedule` the jitter is bounded by the ladder entry rather than by `MaxDelay`. Disable it for deterministic tests. |
| `IsPermanentFailure` | `Func<Exception, bool>?` | `null` | No | Application classifier that dead-letters a message on the first failure it recognizes, regardless of `MaxAttempts`. Runs in addition to the built-in non-retryable set (missing handler, multiple handlers, guard denial, validation failure). It is not called for a cancellation the processor treats as shutdown. A predicate that throws is treated as `false`, so a faulty classifier keeps the retry schedule instead of losing the message. |

Methods: `TimeSpan CalculateDelay(int attemptCount)` and `void Validate()`. Both inbox and outbox modules call `Validate()` when the processor is enabled, so a misconfigured ladder fails at startup rather than on the first failure. `MessageProcessorDiagnostics.ValidateRetryOptions(RetryOptions, string)` runs the shared subset (attempt count range, delay ranges, defined backoff) from the internal processor-options validator when the processor factories build, and the validator then runs the full `RetryOptions.Validate()` so an invalid schedule ladder also fails at startup.

#### `ProcessorOptions` (`LiteBus.Messaging.Abstractions.Processing`, public record; base of `InboxProcessorOptions` and `OutboxProcessorOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `BatchSize` | `int` | `20` | No | Maximum envelopes leased per pass. Validated `> 0`. Also drives adaptive polling: a full batch skips the poll delay. |
| `LeaseDuration` | `TimeSpan` | `1 minute` | No | Lease expiry written when the processor claims a message. Validated `> TimeSpan.Zero`. |
| `Retry` | `RetryOptions` | `new RetryOptions()` | No | Retry and dead-letter policy for dispatch failures. |
| `LeaseOwner` | `string?` | `null` | No | Base lease-owner name. When it is null or whitespace, the processor uses `MachineName:ProcessId`. The pipelined processor always appends a `Guid` suffix so each processor instance is uniquely fenced. |
| `DispatcherConcurrency` | `int` | `1` | No | Number of parallel dispatch workers within one pass (a `SemaphoreSlim` gate). Validated `> 0`. |
| `LeaseHeartbeatInterval` | `TimeSpan` | `15 seconds` | No | Lease renewal cadence while dispatch runs. `TimeSpan.Zero` disables heartbeating. Validated `>= 0` and, when positive, `<= LeaseDuration / 2`. |
| `TenantId` | `string?` | `null` | No | Restricts leasing to one tenant partition (passed into the lease request, where `null` leases across all tenants and a blank value leases only unscoped rows). |
| `HonorShutdownTokenOnPersist` | `bool` | `false` | No | When `false`, the terminal persist uses the heartbeat token. When `true`, it uses the original shutdown token, so persistence can still be cancelled by host shutdown. |
| `HookFailurePolicy` | `ProcessorHookFailurePolicy?` | `null` | No | Applied when an `IProcessorEnvelopeHook.AfterDispatchAsync` throws after a successful dispatch. `null` keeps the dispatcher recommendation: `OutboxModuleBuilder.ProcessorOptions` fills `dispatcherModule.DefaultHookFailurePolicy` when a dispatcher is registered (transport outbox modules report `CompleteDespiteHookFailure`, while the in-process module inherits the `DeadLetter` default), and `InboxModuleBuilder.ProcessorOptions` fills `DeadLetter`. An explicit value always wins over the dispatcher recommendation. |

`InboxProcessorOptions` and `OutboxProcessorOptions` are `sealed record X : ProcessorOptions` with no added members.

#### `ProcessorHostOptions` (public class; base of `InboxProcessorHostOptions` and `OutboxProcessorHostOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | No | When `false`, the background loop returns immediately at startup. |
| `PollInterval` | `TimeSpan` | `1 second` | No | Delay after an empty or partial pass. `TimeSpan.Zero` means no delay (hot loop). Validated `>= TimeSpan.Zero`. |
| `StartupDelay` | `TimeSpan` | `TimeSpan.Zero` | No | One-off delay before the first pass, interruptible by a drain request. Validated `>= TimeSpan.Zero`. |
| `UseAdaptivePolling` | `bool` | `true` | No | When `true` and the last pass leased a full `BatchSize`, the poll delay is skipped so a backlog drains at full speed. |

Method: `void Validate()`. `InboxProcessorHostOptions` and `OutboxProcessorHostOptions` are empty sealed subclasses.

#### `InboxCleanupHostOptions` / `OutboxCleanupHostOptions` (sealed classes)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | No | When `false`, the retention loop exits at startup. |
| `Interval` | `TimeSpan` | `1 hour` | No | Delay between cleanup passes. Validated `> TimeSpan.Zero`. On failure, the loop sleeps the current backoff (initial 5 seconds), then doubles it, capped at `Interval`. |
| `Retention` | `TimeSpan?` | `null` | No | How long terminal rows (Completed or Published) are kept. **When `null`, the cleanup loop returns immediately and `RunRetentionPurgeAsync` returns 0.** Validated `> TimeSpan.Zero` when set. |

Method: `void Validate()`.

#### `LeaseRenewalRequest` (sealed record, positional)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MessageId` | `Guid` | none | **Yes** | Envelope being renewed. |
| `LeaseOwner` | `string` | none | **Yes** | Must match the stored owner or renewal fails. |
| `LeaseGeneration` | `long` | none | **Yes** | Fencing token. A mismatch means another worker re-leased the row. |
| `LeaseDuration` | `TimeSpan` | none | **Yes** | Requested extension length. |
| `RequestedExpiresAt` | `DateTimeOffset` | none | **Yes** | Absolute expiry computed from the caller's clock. |

#### `LeaseReleaseRequest` (sealed record, positional)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MessageId` | `Guid` | none | **Yes** | Envelope being released. |
| `LeaseOwner` | `string` | none | **Yes** | Must match the stored owner or release fails. |
| `LeaseGeneration` | `long` | none | **Yes** | Fencing token. A mismatch means another worker re-leased the row. |

Consumed by `ILeaseRenewable.ReleaseLeaseAsync`, which returns a leased message to the queue without counting the attempt that the lease consumed. The row returns to `Pending`, or to `Failed` when it already carries an error, with its lease cleared, its attempt count decremented, and its visibility unchanged. The interface default releases nothing and returns `false`, so a store written before this member existed keeps reclaiming the row with the attempt counted.

#### `InboxLeaseRequest` / `OutboxLeaseRequest` (sealed records)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `BatchSize` | `int` | none | **Yes** (`required`) | Maximum rows to claim. |
| `LeaseOwner` | `string` | none | **Yes** (`required`) | Owner written on the claimed rows. |
| `Now` | `DateTimeOffset` | none | **Yes** (`required`) | Timestamp used for `VisibleAfter` and lease-expiry comparisons. |
| `LeaseDuration` | `TimeSpan` | none | **Yes** (`required`) | Lease length. The in-memory outbox store substitutes `DefaultLeaseDuration` when this is zero and throws `ArgumentOutOfRangeException` for a negative value. Every inbox store (in-memory, PostgreSQL, EF Core) throws `ArgumentOutOfRangeException` for a zero or negative value and never applies `DefaultLeaseDuration`. |
| `TenantId` | `string?` | `null` | No | Restricts the claim to one tenant partition. `null` leases across all tenants. A blank value (empty or whitespace) leases only unscoped rows. Stored and requested tenants compare through `DurableTenantId` normalization. |

#### `PersistResult` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `AppliedCount` | `int` | constructor | **Yes** | Envelopes whose terminal state was written. Validated non-negative. |
| `SkippedCount` | `int` | constructor | **Yes** | Envelopes skipped because the lease was lost or no longer matched. Validated non-negative. |

Statics: `PersistResult.Empty`, `AllApplied(int)`, `FromOutcome(int, int)`, `FromMessageIds(IReadOnlyList<Guid>, ISet<Guid>)`.

#### `ProcessorPassResult` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `LeasedCount` | `int` | none | **Yes** (`required`) | Envelopes claimed in the pass, compared against `BatchSize` for adaptive polling. |
| `SucceededCount` | `int` | `0` | No | Completed or published envelopes. |
| `FailedCount` | `int` | `0` | No | Envelopes marked failed for retry. |
| `DeadLetteredCount` | `int` | `0` | No | Envelopes moved to dead letter. |
| `ElapsedTime` | `TimeSpan` | `TimeSpan.Zero` | No | Wall-clock duration of the pass including leasing and persistence. |

### 3.3 Durable acceptance and enqueue metadata

#### `InboxAcceptMetadata` (sealed record)

All five members are `required`. `InboxAcceptMetadata.Immediate` is the canonical starting point. Refine it with `with`.

| Property Name | Data Type | Default (`Immediate`) | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Identity` | `MessageIdentity` | `MessageIdentity.Generated.Instance` | **Yes** | `Generated` lets the factory create a `Guid`. `Supplied(Guid)` stores the caller's id, which stores treat as a duplicate key. |
| `Idempotency` | `Idempotency` | `Idempotency.None.Instance` | **Yes** | `Keyed(key, conflictMode)` writes `IdempotencyKey`. Duplicates either return the existing row or throw. |
| `Visibility` | `MessageVisibility` | `MessageVisibility.Immediate.Instance` | **Yes** | `At(DateTimeOffset)` or `After(TimeSpan)` set `VisibleAfter`, deferring leasing. |
| `Trace` | `MessageTrace` | `MessageTrace.None.Instance` | **Yes** | `Correlated`, `Workflow` and `Distributed` map to the `CorrelationId`, `CausationId` and `TraceContext` columns. |
| `Tenant` | `TenantScope` | `TenantScope.Unscoped.Instance` | **Yes** | `Isolated(tenantId)` writes `TenantId`, which participates in the idempotency uniqueness scope. |

#### `OutboxEnqueueMetadata` (sealed record)

It has the same five members plus one more. `OutboxEnqueueMetadata.Immediate` is the base value.

| Property Name | Data Type | Default (`Immediate`) | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Identity` | `MessageIdentity` | `Generated.Instance` | **Yes** | As above. |
| `Idempotency` | `Idempotency` | `None.Instance` | **Yes** | As above. |
| `Visibility` | `MessageVisibility` | `Immediate.Instance` | **Yes** | As above. |
| `Trace` | `MessageTrace` | `None.Instance` | **Yes** | As above. |
| `Tenant` | `TenantScope` | `Unscoped.Instance` | **Yes** | As above. |
| `Target` | `PublicationTarget` | `PublicationTarget.ContractDefault.Instance` | **Yes** | `Topic(name)`, `Exchange(name)` and `Queue(name)` write the `Topic` column, which `TransportOutboxDispatcher` uses when no `ITenantRoutingStrategy` and no `ResolveRoute` delegate is configured. `ResolveRoute` wins over `Topic`, which wins over the contract name. |

#### `InboxAcceptItem` (sealed record, untyped)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Message` | `object` | none | **Yes** (`required`) | The instance serialized into the payload. |
| `MessageType` | `Type?` | `null` | No | Overrides contract lookup for heterogeneous batches and falls back to `Message.GetType()`. The factory validates assignability and throws `ArgumentException` otherwise. |
| `Metadata` | `InboxAcceptMetadata` | `InboxAcceptMetadata.Immediate` | No | Durable annotations. |

Statics: `From(object, InboxAcceptMetadata?)`, `From(object, Type, InboxAcceptMetadata?)`, `From<TMessage>(InboxAcceptItem<TMessage>)`.

#### `InboxAcceptItem<TMessage>` (sealed record, `TMessage : notnull`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Message` | `TMessage` | none | **Yes** (`required`) | Typed message instance. |
| `Metadata` | `InboxAcceptMetadata` | `InboxAcceptMetadata.Immediate` | No | Durable annotations. |

Statics: `From(TMessage, InboxAcceptMetadata?)`, `FromAmbient(TMessage)`, `ScheduledAt(TMessage, DateTimeOffset)`, `ScheduledAfter(TMessage, TimeSpan)`, `WithIdempotency(TMessage, string)`, `WithIdentity(TMessage, Guid)`, `WithCorrelation(TMessage, string)`, `WithTrace(TMessage, MessageTrace)`, `WithTenant(TMessage, string)`. `FromAmbient` stamps `Trace` from `MessageTrace.FromAmbient()` over `Immediate` metadata. Outside a mediation, the item equals `From(message)`.

#### `OutboxEnqueueItem` (sealed record, untyped)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Message` | `object` | none | **Yes** (`required`) | Event instance. |
| `MessageType` | `Type` | none | **Yes** (`required`) | Runtime type used for contract lookup. Unlike the inbox item, this is not optional. |
| `Metadata` | `OutboxEnqueueMetadata` | `OutboxEnqueueMetadata.Immediate` | No | Durable annotations. |

Statics: `From(object)`, `From(object, Type)`, `From(object, Type, OutboxEnqueueMetadata)`.

#### `OutboxEnqueueItem<TEvent>` (sealed record, `TEvent : notnull`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Message` | `TEvent` | none | **Yes** (`required`) | Typed event. |
| `Metadata` | `OutboxEnqueueMetadata` | `OutboxEnqueueMetadata.Immediate` | No | Durable annotations. |

Statics: `From(TEvent)`, `From(TEvent, OutboxEnqueueMetadata)`, `FromAmbient(TEvent)`, `ScheduledAt`, `ScheduledAfter`, `WithIdempotency`, `WithIdentity`, `WithTopic(TEvent, string)`. `FromAmbient` stamps `Trace` from `MessageTrace.FromAmbient()` over `Immediate` metadata. Outside a mediation, the item equals `From(message)`.

#### `InboxReceipt` / `InboxReceipt<TMessage>` (sealed records)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Id` | `Guid` | none | **Yes** (`required`) | Durable message identifier for tracking endpoints. |
| `MessageType` | `Type` | none | **Yes** (`required`) | The accepted CLR type (closed generic when applicable). |
| `Contract` | `MessageContractReference` | none | **Yes** (`required`) | Stable `Name` and `Version` written with the payload. |
| `AcceptedAt` | `DateTimeOffset` | none | **Yes** (`required`) | Store-assigned acceptance timestamp. |
| `Trace` | `MessageTrace` | none | **Yes** (`required`) | Reconstructed from the stored row, so a duplicate returns the original trace. |
| `Tenant` | `TenantScope` | none | **Yes** (`required`) | Reconstructed from the stored row. |
| `Outcome` | `InboxAcceptOutcome` | `Accepted` | No | `AlreadyAccepted` means the store returned an existing row for the supplied idempotency key or identifier. |

#### `OutboxReceipt` / `OutboxReceipt<TEvent>` (sealed records)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Id` | `Guid` | none | **Yes** (`required`) | Durable message identifier. |
| `MessageType` | `Type` | none | **Yes** (`required`) | Stored CLR type. |
| `Contract` | `MessageContractReference` | none | **Yes** (`required`) | Stable contract reference. |
| `StoredAt` | `DateTimeOffset` | none | **Yes** (`required`) | Store-assigned timestamp. |
| `Trace` | `MessageTrace` | `MessageTrace.None.Instance` | No | Trace metadata. Optional here, unlike the inbox receipt. |
| `Tenant` | `TenantScope` | `TenantScope.Unscoped.Instance` | No | Tenant metadata. |
| `Outcome` | `OutboxEnqueueOutcome` | `Enqueued` | No | `AlreadyEnqueued` for a collapsed duplicate. |

#### `MessageContractReference` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Name` | `string` | none | **Yes** (`required`) | Stable contract name. |
| `Version` | `int` | none | **Yes** (`required`) | Positive contract version. |

### 3.4 Envelope records (persisted shape)

#### `InboxEnvelope` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Id` | `Guid` | none | **Yes** (`required`) | Primary key (`message_id`). |
| `ContractName` | `string` | none | **Yes** (`required`) | `contract_name`, which resolves the CLR type at dispatch. |
| `ContractVersion` | `int` | none | **Yes** (`required`) | `contract_version`. |
| `Payload` | `string` | none | **Yes** (`required`) | Serialized, optionally encrypted message text in the `payload` column. |
| `CreatedAt` | `DateTimeOffset` | none | **Yes** (`required`) | `created_at`, the keyset pagination anchor. |
| `VisibleAfter` | `DateTimeOffset?` | `null` | No | `visible_after`, where leasing skips rows whose value is in the future. |
| `AttemptCount` | `int` | none | **Yes** (`required`) | `attempt_count`, incremented by `AsLeased`. |
| `Status` | `InboxStatus` | none | **Yes** (`required`) | `status`, stored as `int`. |
| `IdempotencyKey` | `string?` | `null` | No | `idempotency_key`, unique together with `tenant_id` where not null. |
| `IdempotencyConflictMode` | `IdempotencyConflictMode` | `ReturnExisting` | No | **Not persisted**. Carries the accept-time policy to the store. |
| `LeaseOwner` | `string?` | `null` | No | `lease_owner`. |
| `LeaseGeneration` | `long` | `0` | No | `lease_generation`, a monotonic fencing token incremented with `checked` in `AsLeased`. |
| `LeaseExpiresAt` | `DateTimeOffset?` | `null` | No | `lease_expires_at`, where an expired lease makes the row claimable again. |
| `LastError` | `string?` | `null` | No | `last_error`, truncated to 1024 characters by `MessageProcessorDiagnostics.FormatError`. |
| `CorrelationId` | `string?` | `null` | No | `correlation_id`. |
| `CausationId` | `string?` | `null` | No | `causation_id`. |
| `TenantId` | `string?` | `null` | No | `tenant_id`. |
| `TraceContext` | `string?` | `null` | No | `trace_context`, a W3C trace parent string or a JSON object with `traceparent` and optional `tracestate`. |
| `CompletedAt` | `DateTimeOffset?` | `null` | No | `completed_at`, the retention cutoff column. |
| `LastAttemptedAt` | `DateTimeOffset?` | `null` | No | `last_attempted_at`, stamped on each lease. |
| `FirstFailedAt` | `DateTimeOffset?` | `null` | No | `first_failed_at`, set on the first failure only, so it keeps naming the first one. |
| `DeadLetteredAt` | `DateTimeOffset?` | `null` | No | `dead_lettered_at`, the column a dead-letter listing sorts by. |
| `LastLeaseOwner` | `string?` | `null` | No | `last_lease_owner`, which survives the lease being released, so it still names the instance that was handling a failed message. |
| `ErrorType` | `string?` | `null` | No | `error_type`, the CLR type behind `LastError`, stored apart from it so failures can be grouped without parsing text. |

Transitions: `AsLeased(owner, expiresAt)` runs from any status and records `LastLeaseOwner`. `AsCompleted(DateTimeOffset? completedAt = null)` clears `LastError`. A null timestamp leaves `CompletedAt` unchanged for the store to stamp, and processors pass the processor clock. `AsFailed(MessageFailure, visibleAfter?)` and `AsDeadLettered(MessageFailure)` join `AsCompleted` in requiring `Processing`. `AsReleased()` requires `Processing`, and it clears the lease, decrements `AttemptCount` floored at zero without spending the attempt, returns to `Pending` or to `Failed` when the envelope carries an error, and leaves `VisibleAfter` unchanged. `AsRequeued()` requires `DeadLettered`, and it resets `VisibleAfter`, `AttemptCount`, `LeaseOwner` and `LeaseExpiresAt` while `LeaseGeneration` is preserved, `LastError`, `ErrorType`, `FirstFailedAt` and `DeadLetteredAt`, and keeps `LastAttemptedAt` and `LastLeaseOwner` as the record of what happened before. A wrong source status throws `InvalidOperationException`. `AsFailed` and `AsDeadLettered` throw `ArgumentNullException` on a null failure.

#### `OutboxEnvelope` (sealed record)

Identical to `InboxEnvelope` except for these members:

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Topic` | `string?` | `null` | No | `topic`, the explicit publication destination, used when no `ITenantRoutingStrategy` and no `ResolveRoute` delegate is configured. Route precedence is tenant strategy, then `ResolveRoute`, then `Topic`, then the contract name. Indexed where not null. |
| `Handler` | `string?` | `null` | No | `handler`, the full name of the single event handler this envelope is addressed to when per-handler delivery wrote it, or null for a broadcast envelope published to every subscribed handler. Participates in strict idempotency comparison on all three stores. |
| `Status` | `OutboxStatus` | none | **Yes** (`required`) | `Pending` / `Publishing` / `Published` / `Failed` / `DeadLettered`. |
| `PublishedAt` | `DateTimeOffset?` | `null` | No | `published_at`, the retention cutoff column. |

Transitions: `AsLeased`, `AsPublished(DateTimeOffset publishedAt)` (requires `Publishing`, and sets `PublishedAt` while clearing `LastError`), `AsFailed(MessageFailure, visibleAfter?)`, `AsDeadLettered(MessageFailure)`, `AsReleased()` (requires `Publishing`, and clears the lease, decrements `AttemptCount` floored at zero without spending the attempt, returns to `Pending` or to `Failed` when the envelope carries an error, and leaves `VisibleAfter` unchanged), and `AsRequeued` (requires `DeadLettered`, with the same reset shape as the inbox variant and `LeaseGeneration` preserved).

The operational columns `last_attempted_at`, `first_failed_at`, `dead_lettered_at`, `last_lease_owner` and `error_type` are on both envelope records and are written and read by every store adapter, so `IInboxManager.QueryAsync` and `IOutboxManager.QueryAsync` return them.

#### `MessageFailure` (`LiteBus.Messaging.Abstractions`, sealed record)

| Property Name | Data Type | Required? | Description & Impact |
| --- | --- | --- | --- |
| `Error` | `string` | **Yes** (`required`) | The formatted, length-capped text persisted to `last_error`. |
| `ErrorType` | `string?` | No | The full CLR type name of the exception, or null when the failure carried none. |
| `OccurredAt` | `DateTimeOffset` | **Yes** (`required`) | The UTC timestamp the attempt failed, used for `first_failed_at` and `dead_lettered_at`. |

Statics: `MessageFailure.From(Exception, DateTimeOffset)` formats the error and captures the type. `MessageFailure.FromError(string, DateTimeOffset)` covers a reason with no exception, such as a lost lease, and records no `ErrorType`.

### 3.5 Query / purge filters and paging

#### `InboxMessageFilter` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MessageId` | `Guid?` | `null` | No | Exact id match. |
| `MessageIds` | `IReadOnlyList<Guid>?` | `null` | No | Id set match. |
| `Statuses` | `IReadOnlyList<InboxStatus>?` | `null` | No | Status set. Null or empty means no status filter. |
| `ContractName` | `string?` | `null` | No | Exact contract name match. |
| `CorrelationId` | `string?` | `null` | No | Exact match. |
| `CausationId` | `string?` | `null` | No | Exact match. |
| `TenantId` | `string?` | `null` | No | Exact match. `null` leaves the query unrestricted across tenants. A blank value (empty or whitespace) matches only unscoped rows. Stored and requested tenants compare through `DurableTenantId` normalization. |
| `CreatedAfter` | `DateTimeOffset?` | `null` | No | Inclusive lower bound on `CreatedAt`. |
| `CreatedBefore` | `DateTimeOffset?` | `null` | No | Inclusive upper bound on `CreatedAt`. |

Static: `InboxMessageFilter.DeadLettered` (only `Statuses = [DeadLettered]`). Extensions: `HasMinimumCriteria()` and `IsUnrestricted()`. A tenant predicate counts as narrowing whenever `TenantId` is not `null`, so a blank tenant (unscoped-only) also satisfies purge safety. `IInboxManager.PurgeAsync` refuses an unrestricted filter unless `confirm: true`, throwing `InboxManagementException`.

#### `OutboxMessageFilter` (sealed record)

Same members plus `Topic` (`string?`, default `null`, exact match). `TenantId` follows the same store semantics (`null` unrestricted, blank unscoped-only), but `HasMinimumCriteria()` treats a blank tenant identifier as unset for purge safety, so a blank tenant alone does not authorize a purge. Static `OutboxMessageFilter.DeadLettered`. Extensions `HasMinimumCriteria()` and `IsUnrestricted()`. `IOutboxManager.PurgeAsync` throws `OutboxManagementException` for an unconfirmed unrestricted purge.

#### `OutboxFailureSummary` (`LiteBus.Outbox.Abstractions`, sealed record)

One row per contract name and version that has at least one `Failed` or `DeadLettered` message, returned by `IOutboxManager.GetFailureSummaryAsync`. A health probe uses it to tell messages that fail on every attempt apart from a backlog that is merely slow, without paging through individual rows.

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `ContractName` | `string` | none | **Yes** (`required`) | Stable contract name shared by the failing messages. |
| `ContractVersion` | `int` | none | **Yes** (`required`) | Contract version shared by the failing messages. |
| `FailedCount` | `int` | none | **Yes** (`required`) | Messages in `Failed` awaiting retry. |
| `DeadLetteredCount` | `int` | none | **Yes** (`required`) | Messages in `DeadLettered` needing an operator replay. |
| `LastError` | `string?` | `null` | No | Error text of the most recently attempted failing message of this contract, or `null` when none carries error text. |
| `LastErrorType` | `string?` | `null` | No | CLR type name of the exception behind `LastError`, or `null` when the most recent failure carried none. |
| `LastFailedAt` | `DateTimeOffset?` | `null` | No | Latest `OutboxEnvelope.LastAttemptedAt` among the failing messages of this contract, or `null` when none has been attempted. |

#### `InboxMessagePageRequest` / `OutboxMessagePageRequest` (sealed records)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `PageSize` | `int` | `50` | No | Rows per page. `InboxMessagePageRequest.MaxPageSize` (`1000`) caps the inbox request: `IInboxManager.QueryAsync` and the in-memory and PostgreSQL inbox stores throw `ArgumentOutOfRangeException` below 1 or above it. Outbox stores reject sizes below 1 with `ArgumentOutOfRangeException` and apply no upper cap. The ASP.NET Core management endpoints reject out-of-range values instead of clamping: `ToPageRequest(maxPageSize)` throws `ArgumentOutOfRangeException` when `PageSize <= 0` or `PageSize > maxPageSize` (`LiteBusManagementOptions.MaxPageSize`). |
| `Cursor` | `string?` | `null` | No | Opaque Base64 keyset cursor holding `CreatedAt` and `MessageId`, produced by `InboxMessagePageCursor.Encode` and `OutboxMessagePageCursor.Encode`. `null` requests the first page. |

`InboxMessagePage` and `OutboxMessagePage` are positional records: `(IReadOnlyList<Envelope> Items, bool HasMore, string? NextCursor)`. `RequeueResult` (both axes) is `(int Requested, int Requeued)`. Both `InboxMessagePageCursor` and `OutboxMessagePageCursor` expose `TryDecode(string?, out DateTimeOffset createdAt, out Guid messageId)`, which returns `false` with default outs for a null, blank, malformed Base64, or non-JSON cursor instead of throwing. Stores throw `ArgumentException` ("The cursor is invalid.") when the cursor fails `TryDecode`.

### 3.6 Storage adapter options

#### `InMemoryInboxStoreOptions` / `InMemoryOutboxStoreOptions` (sealed records, implement `IMessageStoreRetentionOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Capacity` | `int` | `0` | No | `0` means unbounded. A positive value makes the store throw `InboxStorageException` or `OutboxStorageException` once that many rows exist, unless the submission resolves to an existing row through idempotent insert. A negative value throws `ArgumentOutOfRangeException` from the store constructor. |
| `DefaultLeaseDuration` | `TimeSpan` | `1 minute` | No | Validated `> TimeSpan.Zero` by both store constructors. The outbox store substitutes it when a lease request supplies `TimeSpan.Zero` and throws `ArgumentOutOfRangeException` for a negative request duration. The inbox store never applies it: `LeasePendingAsync` throws `ArgumentOutOfRangeException` for a zero or negative request duration. |
| `TerminalRetention` | `TimeSpan?` | `null` | No | Advisory retention window exposed through `IMessageStoreRetentionOptions`. |

#### `PostgreSqlSchemaStoreOptions` (public record; base of the PostgreSQL store options)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MetadataSchemaName` | `string` | `"public"` | No | Schema holding the LiteBus schema-version metadata table. |
| `MetadataTableName` | `string` | `"litebus_schema_versions"` | No | Table holding one row per component with its recorded schema version. |
| `Logger` | `IPostgreSqlSchemaLogger?` | `null` | No | Receives schema create, upgrade, or validate entries at `PostgreSqlSchemaLogLevel`. |
| `ValidateIndexesOnStartup` | `bool` | `true` | No | When `true`, validation also asserts that the required indexes exist on the store table. |

#### `PostgreSqlInboxStoreOptions` (sealed record : `PostgreSqlSchemaStoreOptions`, `IPostgreSqlStoreTableOptions`, `IMessageStoreRetentionOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `SchemaName` | `string` | `"public"` | No | Schema containing the inbox table. |
| `TableName` | `string` | `"litebus_inbox_messages"` | No | Inbox table name. |
| `EnsureSchemaCreationOnStartup` | `bool` | `true` | No | Startup task creates or upgrades the schema. |
| `ValidateSchemaCreationOnStartup` | `bool` | `true` | No | Startup fails when the table does not match the expected schema version. |
| `UseListenNotify` | `bool` | `false` | No | When `true`, the module registers `PostgreSqlInboxWorkSignal` (LISTEN/NOTIFY) instead of `InboxPollingWorkSignal`, so an insert wakes the processor without waiting for the poll interval. Polling remains the fallback. |
| `TerminalRetention` | `TimeSpan?` | `null` | No | Advisory retention window. |
| `MetadataSchemaName`, `MetadataTableName`, `Logger`, `ValidateIndexesOnStartup` | inherited | see base | No | Schema metadata settings. |

#### `PostgreSqlOutboxStoreOptions`

Identical members, except `TableName` defaults to `"litebus_outbox_messages"`. When `UseListenNotify` is `true`, the module registers `IOutboxWorkSignal` as `PostgreSqlOutboxWorkSignal` (LISTEN/NOTIFY on `litebus_outbox_work`). Otherwise it registers `OutboxPollingWorkSignal`.

#### Marten transactional outbox (no options record)

`LiteBus.Outbox.Storage.Marten` ships no options record of its own. `MartenTransactionalOutbox` (sealed class, implements `ITransactionalOutbox`) stages each envelope on the current Marten `IDocumentSession` with `QueueSqlCommand` using the schema-owned insert, so documents and outbox rows commit together on `SaveChangesAsync`. Disposing the session without saving stores nothing. Schema and table come from the existing `PostgreSqlOutboxStoreOptions` via `PostgreSqlOutboxStoreRegistration`, and committed-row idempotency lookups run on a separate connection through `PostgreSqlOutboxStore`. Registration is `PostgreSqlOutboxModuleBuilder.UseMartenTransactionalOutbox()`. The application registers Marten itself with `AddMarten` against the same database. It cannot be combined with `EnableAmbientTransactionProvider()`: both register `ITransactionalOutbox`, so building the module throws when both are configured.

#### `EntityFrameworkCoreInboxStoreOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `SchemaName` | `string` | `"public"` | No | Schema used by `ToTable`, ignored for MySQL and SQLite, which have no schemas. |
| `TableName` | `string` | `"litebus_inbox_messages"` | No | Table name. |
| `LeaseProvider` | `EfCoreStorageProvider?` | `null` | No | Forces the raw-SQL lease dialect. When `null`, the provider is detected from `DbContext.Database.ProviderName`. |

#### `EntityFrameworkCoreOutboxStoreOptions`

Identical members, except `TableName` defaults to `"litebus_outbox_messages"`.

#### `PostgreSqlSagaStoreOptions` (sealed record : `PostgreSqlSchemaStoreOptions`, `IPostgreSqlStoreTableOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `SchemaName` | `string` | `"public"` | No | Schema containing saga tables. |
| `TableName` | `string` | `"litebus_saga_instances"` | No | Saga instance table. |
| `EnsureSchemaCreationOnStartup` | `bool` | `true` | No | Startup creates the schema. |
| `ValidateSchemaCreationOnStartup` | `bool` | `false` | No | **Off by default**, unlike the inbox and outbox options. |
| `MetadataSchemaName`, `MetadataTableName`, `Logger`, `ValidateIndexesOnStartup` | inherited | see base | No | Schema metadata settings. |

#### `PostgreSqlIdempotencyStoreOptions` (sealed record : `PostgreSqlSchemaStoreOptions`, `IPostgreSqlStoreTableOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `SchemaName` | `string` | `"public"` | No | Schema containing the idempotency keys table. |
| `TableName` | `string` | `"litebus_idempotency_keys"` | No | Idempotency keys table. |
| `ClaimTimeout` | `TimeSpan` | `5 minutes` | No | How long an unsettled claim blocks its key before a new caller takes it over, measured on the database clock. It must be positive, and `Validate()` rejects zero or negative. Keep it longer than the slowest mediation claiming through the store. |
| `EnsureSchemaCreationOnStartup` | `bool` | `true` | No | Startup task creates the schema. |
| `ValidateSchemaCreationOnStartup` | `bool` | `false` | No | **Off by default**, like the saga options. |
| `MetadataSchemaName`, `MetadataTableName`, `Logger`, `ValidateIndexesOnStartup` | inherited | see base | No | Schema metadata settings. |

#### `PostgreSqlRecurringLeaseOptions` (sealed record : `PostgreSqlSchemaStoreOptions`, `IPostgreSqlStoreTableOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `SchemaName` | `string` | `"public"` | No | Schema containing the recurring leases table. |
| `TableName` | `string` | `"litebus_recurring_leases"` | No | Recurring leases table. |
| `LeaseDuration` | `TimeSpan` | `30 seconds` | No | How long one acquisition holds the lease before it must be renewed, measured on the database clock. It must be positive. It bounds how long a crashed holder blocks the loop. |
| `RenewInterval` | `TimeSpan?` | `null` (meaning one third of `LeaseDuration`) | No | How often a held lease is renewed while its handle is alive. `EffectiveRenewInterval` resolves the value the loop uses. `Validate()` rejects a zero or negative duration or effective interval, and an explicit interval at or above `LeaseDuration`. |
| `EnsureSchemaCreationOnStartup` | `bool` | `true` | No | Startup task creates the schema. |
| `ValidateSchemaCreationOnStartup` | `bool` | `false` | No | **Off by default**, like the saga options. |
| `MetadataSchemaName`, `MetadataTableName`, `Logger`, `ValidateIndexesOnStartup` | inherited | see base | No | Schema metadata settings. |

#### `MartenIdempotencyStoreOptions` (sealed class, from `LiteBus.Commands.Storage.Marten`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `ClaimTimeout` | `TimeSpan` | `5 minutes` | No | How long an unsettled claim blocks its key before another claimant takes it over. It must be positive, and `Validate()` rejects zero or negative. Settable so the `UseMartenIdempotencyStore` configure action can assign it. |

#### `IPostgreSqlStoreTableOptions` (interface implemented by the five PostgreSQL option records)

| Member | Data Type | Description & Impact |
| --- | --- | --- |
| `SchemaName` | `string` | Store table schema. |
| `TableName` | `string` | Store table name. |
| `MetadataSchemaName` | `string` | Metadata table schema. |
| `MetadataTableName` | `string` | Metadata table name. |
| `Logger` | `IPostgreSqlSchemaLogger?` | Optional schema logger. |

#### `IMessageStoreRetentionOptions`

| Member | Data Type | Description & Impact |
| --- | --- | --- |
| `TerminalRetention` | `TimeSpan?` | How long completed or published rows may remain before cleanup deletes them. |

### 3.7 Dispatch adapter options

#### `TransportInboxDispatcherOptions` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `DefaultDestination` | `string` | `string.Empty` | No, but effectively required per broker | `TransportPublishRequest.Destination`: AMQP exchange, Kafka topic, SQS queue URL, or Service Bus entity. |
| `ContentType` | `string` | `"application/json"` | No | Written to transport message properties. |
| `Persistent` | `bool` | `true` | No | Requests broker-side persistence where supported. |
| `Mandatory` | `bool` | `true` | No | Requires the broker to route to at least one consumer. An unroutable publish fails. Passed through to `TransportPublishRequest.Mandatory`, which also defaults to `true`. |
| `ResolveRoute` | `Func<InboxEnvelope, string>?` | `null` | No | Per-envelope route. Ignored when an `ITenantRoutingStrategy` is registered. When both are absent, the route is `envelope.ContractName`. A null, empty or whitespace result fails the dispatch with `InboxDispatchException`. |
| `ValidatePayloadBeforeDispatch` | `bool` | `false` | No | When `true`, the payload is deserialized before publishing, turning a contract-wiring mistake into a dispatch failure instead of a poison message downstream. |

#### `TransportOutboxDispatcherOptions` (`LiteBus.Outbox.Dispatch`, sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `DefaultDestination` | `string` | `string.Empty` | No, but effectively required per broker | `TransportPublishRequest.Destination`: AMQP exchange, Kafka topic, SQS queue URL, or Service Bus entity. |
| `ContentType` | `string` | `"application/json"` | No | Written to transport message properties. |
| `Persistent` | `bool` | `true` | No | Requests broker-side persistence where supported. |
| `Mandatory` | `bool` | `true` | No | Requires the broker to route to at least one consumer. An unroutable publish fails. Passed through to `TransportPublishRequest.Mandatory`, which also defaults to `true`. |
| `TenantRoutingStrategy` | `ITenantRoutingStrategy?` | `null` | No | When set, the dispatch module registers it as the `ITenantRoutingStrategy` the dispatcher resolves, so a multi-tenant host routes each tenant partition to its own destination. The strategy result must be non-empty. A null or whitespace result fails the dispatch with `LiteBusDispatchException` naming the envelope. |
| `ResolveRoute` | `Func<OutboxEnvelope, string>?` | `null` | No | Per-envelope route. Wins over `envelope.Topic`. A null, empty or whitespace result fails the dispatch with `LiteBusDispatchException`. |
| `ValidatePayloadBeforeDispatch` | `bool` | `false` | No | When `true`, the payload is deserialized before publishing, turning a contract-wiring mistake into a dispatch failure instead of a poison message downstream. |

Route precedence runs from `ITenantRoutingStrategy`, then `ResolveRoute`, then `envelope.Topic`, then `envelope.ContractName`. The dispatcher also fills `TransportPublishRequest.MessageGroupId` (tenant id, or the contract name when unscoped) and `DeduplicationKey` (idempotency key, or the message id when absent) on every publish.

#### `InProcessOutboxDispatchOptions` (`LiteBus.Outbox.Dispatch.InProcess`, sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `RequireHandler` | `bool` | `true` | No | Sets `EventMediationSettings.ThrowIfNoHandlerFound` on the in-process publish. When no handler is registered, `true` fails the dispatch attempt (retry, then dead-letter). `false` completes the dispatch and marks the message published without any handler having run. Configured through `OutboxModuleBuilder.UseInProcessDispatch(dispatch => ...)` on `InProcessOutboxDispatchOptionsBuilder` (`RequireHandler` settable, `Build()` produces the record). |

### 3.8 Inbox ingress options

#### `TransportInboxIngressSafetyOptions` (sealed record) - shared by every broker ingress

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MaxMessageBytes` | `int` | `4194304` (4 MiB, `DefaultMaxMessageBytes`) | No | Deliveries larger than this are rejected before deserialization. `0` disables the limit. A negative value throws `DurableStorageConfigurationException` from `Validate()`. |
| `RequireStableIdentity` | `bool` | `true` | No | Requires a stable broker delivery identity so ingress can derive a deterministic message id. Without it, redelivery would create duplicate inbox rows. |
| `TrustApplicationHeaders` | `bool` | `false` | No | When `true`, LiteBus application headers may override broker-derived identity and tenant metadata. When `false` (the default), identity, idempotency, tenant, and visibility derive from the broker delivery id and accepted messages are immediately visible. Correlation headers are always honored. Leave `false` for untrusted publishers. |
| `AuthorizeDeliveryAsync` | `Func<TransportMessage, CancellationToken, Task>?` | `null` | No | Invoked before deserialization. Throw to reject a delivery. |
| `MaxInFlightMessages` | `int` | `32` (`TransportConsumerOptions.DefaultMaxInFlightMessages`) | No | LiteBus-side concurrency gate for ingress handlers. Validated `>= 1`. The enforced bound is one for the AMQP, Kafka, AWS SQS, and in-memory adapters, which run one handler at a time. On Azure Service Bus the bound is the lower of this value and `MaxConcurrentCalls`, which defaults to the SDK value of one. |
| `EnableBatchAccept` | `bool` | `false` | No | Buffers deliveries and accepts them through `IInbox.AcceptBatchAsync`. |
| `BatchSize` | `int` | `10` (`DefaultBatchSize`) | No | Deliveries per batch accept. Validated `>= 1`. |
| `BatchMaxWait` | `TimeSpan` | `200 ms` | No | Maximum wait before flushing a partial batch. Validated `>= TimeSpan.Zero`, and `Validate()` throws `DurableStorageConfigurationException` for a zero wait when `EnableBatchAccept` is set, since a zero wait never flushes partial batches. |

Method: `void Validate()`.

#### `TransportInboxIngressOptions` (sealed record) - the transport-neutral consumer loop options

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Destination` | `string` | `string.Empty` | No | Subscription source. |
| `SubscriptionName` | `string?` | `null` | No | Named subscription for topic destinations. |
| `PrefetchCount` | `int` | `0` | No | Native broker prefetch. |
| `ReceiveBatchSize` | `int` | `1` | No | Messages requested per receive call for batch-receiving transports (SQS). |
| `MaxConcurrentCalls` | `int?` | `null` | No | Native broker callback concurrency (Azure Service Bus). |
| `DeclareDestination` | `bool` | `false` | No | Whether the consumer declares the destination. This default is `false` while `TransportConsumerOptions.DeclareDestination` defaults to `true`. |
| `DurableDestination` | `bool` | `false` | No | Whether a declared destination survives broker restarts, with the same default difference as above. |
| `RequeueOnFailure` | `bool` | `true` | No | Whether a failed store write returns the delivery to the broker. `IngressAckPolicy` still refuses to requeue non-transient failures. |
| `Safety` | `TransportInboxIngressSafetyOptions` | `new()` | No | Safety limits above. |

Constant: `TransportInboxIngressOptions.DefaultMaxMessageBytes` (same value as the safety constant).

#### `TransportInboxIngressHostOptions` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | No | When `false`, the ingress background loop returns immediately. |
| `RetryPollInterval` | `TimeSpan` | `5 seconds` | No | Delay between consumer restart attempts after a startup or connection failure. Validated `>= TimeSpan.Zero`. A zero interval still waits `MinimumRestartDelay` (static, 1 second) between restarts so a persistent failure cannot turn into a hot restart loop. |

#### `AmqpInboxIngressOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Safety` | `TransportInboxIngressSafetyOptions` | `new()` | No | Shared safety limits. |
| `QueueName` | `string` | `string.Empty` | No | Queue consumed. |
| `PrefetchCount` | `ushort` | `10` | No | AMQP basic.qos prefetch. |
| `DeclareQueue` | `bool` | `true` | No | Declare the queue before subscribing. |
| `DurableQueue` | `bool` | `true` | No | Declared queue survives restarts. |
| `RequeueOnFailure` | `bool` | `true` | No | Nack with requeue on a transient failure. |

#### `KafkaInboxIngressOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Safety` | `TransportInboxIngressSafetyOptions` | `new()` | No | Shared safety limits. |
| `Destination` | `string` | `string.Empty` | No | Topic consumed. |
| `RequeueOnFailure` | `bool` | `true` | No | Leaves offsets uncommitted so the record is redelivered. |

#### `AwsSqsInboxIngressOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Safety` | `TransportInboxIngressSafetyOptions` | `new()` | No | Shared safety limits. |
| `Destination` | `string` | `string.Empty` | No | Queue URL polled. |
| `ReceiveBatchSize` | `int` | `1` | No | Messages per `ReceiveMessage` call. Composition-validated to 1..10. |
| `RequeueOnFailure` | `bool` | `true` | No | Returns messages for retry by resetting visibility. |

#### `AzureServiceBusInboxIngressOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Safety` | `TransportInboxIngressSafetyOptions` | `new()` | No | Shared safety limits. |
| `Destination` | `string` | `string.Empty` | No | Queue or topic name. |
| `SubscriptionName` | `string?` | `null` | No | Required when `Destination` names a topic. |
| `PrefetchCount` | `int` | `0` | No | Processor prefetch. Composition-validated `>= 0`. |
| `MaxConcurrentCalls` | `int?` | `null` | No | Service Bus processor callback concurrency. Composition-validated null-or-`>= 1`. |
| `RequeueOnFailure` | `bool` | `true` | No | Abandons the message for retry. |

#### `InMemoryInboxIngressOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Safety` | `TransportInboxIngressSafetyOptions` | `new()` | No | Shared safety limits. |
| `Destination` | `string` | `string.Empty` | No | Logical channel name. |
| `RequeueOnFailure` | `bool` | `true` | No | Re-enqueues the delivery. |

### 3.9 Transport options

#### `TransportConsumerOptions` (sealed record, `LiteBus.Transport.Abstractions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Destination` | `string` | none | **Yes** (`required`) | Address consumed (queue name, topic, queue URL). |
| `SubscriptionName` | `string?` | `null` | No | Named subscription for topic destinations. |
| `PrefetchCount` | `int` | `0` | No | Native prefetch for brokers that support it (RabbitMQ, Azure Service Bus). On AMQP, the default of zero maps to the broker-native unlimited prefetch window. |
| `ReceiveBatchSize` | `int` | `1` | No | Messages requested per broker batch receive. |
| `MaxConcurrentCalls` | `int?` | `null` | No | Native callback concurrency limit. |
| `MaxInFlightMessages` | `int` | `32` (`DefaultMaxInFlightMessages`) | No | Limits how many message handlers LiteBus runs concurrently. |
| `DeclareDestination` | `bool` | `true` | No | Declare the destination before subscribing. |
| `DurableDestination` | `bool` | `true` | No | Declared destination survives broker restarts. |
| `Exclusive` | `bool` | `false` | No | Exclusive subscription on this connection. |
| `ConsumerTag` | `string?` | `null` | No | Client-assigned consumer tag. |
| `DestinationArguments` | `IReadOnlyDictionary<string, object?>?` | `null` | No | Extra declaration arguments the client passes to the broker. |

The shared constant is `TransportConsumerOptions.DefaultMaxInFlightMessages = 32`.

#### `TransportPublishRequest` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Destination` | `string` | none | **Yes** (`required`) | Primary address (AMQP exchange, Service Bus entity, Kafka topic, SQS queue URL). |
| `Route` | `string?` | `null` | No | Route within the destination (AMQP routing key, Service Bus subject, Kafka key). |
| `Body` | `ReadOnlyMemory<byte>` | none | **Yes** (`required`) | Message body. LiteBus dispatchers pass UTF-8 payload text. |
| `Headers` | `IReadOnlyDictionary<string, object?>?` | `null` | No | Application headers. LiteBus dispatchers fill these with the `TransportHeaders` names. |
| `ContentType` | `string` | `"application/json"` | No | Content type property. |
| `ContentEncoding` | `string?` | `null` | No | Content encoding property. SQS base64 bodies also use the `litebus-content-encoding` header. |
| `Persistent` | `bool` | `true` | No | Broker persistence flag. |
| `Mandatory` | `bool` | `true` | No | The broker must route the message to at least one consumer. |
| `MessageId` | `string?` | `null` | No | Broker message id property. |
| `CorrelationId` | `string?` | `null` | No | Broker correlation id property. |
| `MessageGroupId` | `string?` | `null` | No | Identifier of the FIFO message group. LiteBus maps it to SQS `MessageGroupId` for `.fifo` destinations only. Standard queues and the other transports ignore it. |
| `DeduplicationKey` | `string?` | `null` | No | Deduplication key for brokers with content-based deduplication. LiteBus maps it to SQS `MessageDeduplicationId` for `.fifo` destinations only. Standard queues and the other transports ignore it. |

#### `TransportMessage` (sealed class, inbound delivery)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MessagingSystem` | `string?` | `null` | No | The adapter supplies the OTel `messaging.system` value (see `TransportMessagingSystems`). |
| `Body` | `ReadOnlyMemory<byte>` | none | **Yes** (`required`) | Delivery body. Copy or deserialize it before the handler returns. |
| `Headers` | `IReadOnlyDictionary<string, object?>` | none | **Yes** (`required`) | Application headers. |
| `Destination` | `string?` | `null` | No | Destination the publisher sent the message to. |
| `Route` | `string?` | `null` | No | Route within the destination. |
| `MessageId` | `string?` | `null` | No | Broker message id. |
| `CorrelationId` | `string?` | `null` | No | Broker correlation id. |
| `Redelivered` | `bool` | `false` | No | Whether the broker previously attempted delivery. |
| `AckAsync` | `Func<CancellationToken, Task>` | none | **Yes** (`required`) | Positive settlement delegate. |
| `NackAsync` | `Func<bool, CancellationToken, Task>` | none | **Yes** (`required`) | Negative settlement delegate. The `bool` requests requeue. |

Three helpers wrap settlement: `AcceptAsync()`, `DiscardAsync()` (nack without requeue), and `ReturnToQueueAsync()` (nack with requeue).

#### `TransportConsumerAckHandlers` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `AckAsync` | `Func<CancellationToken, Task>` | none | **Yes** (`required`) | Bundled positive settlement delegate. |
| `NackAsync` | `Func<bool, CancellationToken, Task>` | none | **Yes** (`required`) | Bundled negative settlement delegate. |

#### `TransportCircuitBreakerOptions` (sealed record, `LiteBus.Transport`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `FailureThreshold` | `int` | `5` | No | Number of consecutive failures that opens the circuit. `0` disables breaking. |
| `BreakDuration` | `TimeSpan` | `30 seconds` | No | Time an open circuit rejects broker operations with `TransportCircuitBreakerOpenException`. |

#### `TransportReconnectOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `AutomaticRecoveryEnabled` | `bool` | `true` | No | Whether the client auto-recovers dropped connections. |
| `RecoveryInterval` | `TimeSpan` | `5 seconds` | No | Interval between recovery attempts. |

#### `AmqpConnectionOptions` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Uri` | `Uri?` | `null` | No | When set, it replaces the discrete fields. It must be absolute, use `amqp` or `amqps`, and carry a host. Otherwise `AmqpTransportModule` throws `ArgumentException`. |
| `HostName` | `string` | `"localhost"` | Validated when `Uri` is null | Broker host, which must be non-blank. |
| `Port` | `int` | `5672` | Validated when `Uri` is null | Broker port, validated against `1..65535`. |
| `VirtualHost` | `string` | `"/"` | Validated when `Uri` is null | AMQP virtual host, which must be non-blank. |
| `UserName` | `string` | `"guest"` | Validated when `Uri` is null | Authentication user, which must be non-null. |
| `Password` | `string` | `"guest"` | Validated when `Uri` is null | Authentication password, which must be non-null. |
| `ClientProvidedName` | `string?` | `null` | No | Connection name that broker management UIs show. |
| `AutomaticRecoveryEnabled` | `bool` | `true` | No | Whether the RabbitMQ client auto-recovers dropped connections. |
| `NetworkRecoveryInterval` | `TimeSpan` | `5 seconds` | No | Interval between recovery attempts. When recovery is enabled, it must be `> TimeSpan.Zero`. |
| `CircuitBreaker` | `AmqpCircuitBreakerOptions` | `new()` | No | Connection and per-exchange breaker settings, which must be non-null. |

#### `AmqpCircuitBreakerOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `FailureThreshold` | `int` | `5` | No | It must be `>= 0`. |
| `BreakDuration` | `TimeSpan` | `30 seconds` | No | When the threshold is positive, it must be `> TimeSpan.Zero`. |

The internal helper `ToTransportOptions()` maps these settings onto `TransportCircuitBreakerOptions`.

#### `AmqpConsumerOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `QueueName` | `string` | none | **Yes** (`required`) | Queue the consumer reads. |
| `PrefetchCount` | `ushort` | `1` | No | basic.qos prefetch. `0` selects RabbitMQ's unlimited native prefetch window. |
| `MaxInFlightMessages` | `int` | `32` (`TransportConsumerOptions.DefaultMaxInFlightMessages`) | No | Maximum number of delivery handlers LiteBus may execute concurrently for this subscription. |
| `Exclusive` | `bool` | `false` | No | Exclusive consumer. |
| `ConsumerTag` | `string?` | `null` | No | Client-supplied consumer tag. |
| `DeclareQueue` | `bool` | `true` | No | Declares the queue before subscribing. |
| `DurableQueue` | `bool` | `true` | No | Declares a durable queue. |
| `QueueArguments` | `IReadOnlyDictionary<string, object?>?` | `null` | No | Arguments the client passes to `queue.declare`. |

#### `AmqpPublishRequest` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Exchange` | `string` | none | **Yes** (`required`) | Target exchange name. An empty name selects the AMQP default exchange. |
| `RoutingKey` | `string` | none | **Yes** (`required`) | Routing key the exchange uses to route the message. |
| `Body` | `ReadOnlyMemory<byte>` | none | **Yes** (`required`) | Message body. |
| `Headers` | `IReadOnlyDictionary<string, object?>?` | `null` | No | Application headers that LiteBus copies onto the AMQP message. |
| `ContentType` | `string` | `"application/json"` | No | MIME content type that LiteBus writes to AMQP message properties. |
| `ContentEncoding` | `string?` | `null` | No | Content encoding that LiteBus writes to AMQP message properties. |
| `Persistent` | `bool` | `true` | No | Requests broker-side persistence. |
| `Mandatory` | `bool` | `true` | No | When `true`, a publication the broker returns as unroutable fails with `AmqpMessageReturnedException`. When `false`, the broker drops unroutable messages without an error and publication succeeds. |
| `MessageId` | `string?` | `null` | No | AMQP message identifier that LiteBus writes to message properties. |
| `CorrelationId` | `string?` | `null` | No | AMQP correlation identifier that LiteBus writes to message properties. |

#### `KafkaTransportOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `BootstrapServers` | `string` | none | **Yes** (`required`) | Kafka bootstrap list. It must be non-blank. The module constructor throws `ArgumentException` otherwise. |
| `ClientId` | `string?` | `null` | No | Client id for producer, consumer, and admin clients. |
| `ConsumerGroupId` | `string` | `"litebus-transport"` | No | Consumer group used by `KafkaConsumer`. It must be non-blank. The module constructor throws `ArgumentException` otherwise. |
| `MessageTimeoutMs` | `int?` | `null` | No | Local message timeout passed to the Kafka producer. When `null`, the Confluent producer default applies. When set, it must be `> 0`. |
| `MessageSendMaxRetries` | `int?` | `null` | No | Maximum send retries passed to the Kafka producer. It is independent of `MessageTimeoutMs`. When `null`, the Confluent producer default applies. When set, it must be `> 0`. |
| `SocketTimeoutMs` | `int?` | `null` | No | Socket timeout passed to the Kafka producer. It is independent of `MessageTimeoutMs`. When `null`, the Confluent producer default applies. When set, it must be `> 0`. |
| `ConnectivityCheckTimeout` | `TimeSpan` | `5 seconds` | No | Cluster describe timeout for the connectivity probe. It must be `> TimeSpan.Zero`. |
| `SeekFailureBackoffInitial` | `TimeSpan` | `250 ms` | No | First delay before re-consuming an offset that failed ingress. It must be `> TimeSpan.Zero`. |
| `SeekFailureBackoffMax` | `TimeSpan` | `30 seconds` | No | Maximum seek backoff. It must be `>= SeekFailureBackoffInitial`. |
| `SeekFailureBackoffMultiplier` | `double` | `2.0` | No | Growth factor per repeated failure at the same offset. It must be finite and `>= 1`. |

LiteBus fixes the producer configuration to `Acks.All`. It fixes the consumer configuration to `EnableAutoCommit = false` and `AutoOffsetReset.Earliest`.

#### `AwsSqsTransportOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Region` | `string?` | `null` | No | AWS region system name. LiteBus uses it only when `ServiceUrl` is not set. When both are unset, the AWS SDK resolves the region from environment variables, the shared configuration file, or instance metadata. When the region cannot be resolved, LiteBus throws `AwsSqsTransportConfigurationException` without recording a circuit-breaker failure. |
| `ServiceUrl` | `string?` | `null` | No | Custom endpoint such as LocalStack. It takes precedence over `Region` and skips region resolution. |
| `AccessKey` | `string?` | `null` | No | Explicit credentials. Supply it together with `SecretKey`; otherwise the module throws `ArgumentException`. |
| `SecretKey` | `string?` | `null` | No | See `AccessKey`. |
| `ConnectivityCheckQueueUrl` | `string?` | `null` | No | Queue whose attributes the connectivity probe reads. |
| `LongPollWaitTimeSeconds` | `int` | `20` | No | SQS long-poll wait. It must be 0..20. |
| `VisibilityTimeoutSeconds` | `int` | `30` | No | Default visibility timeout applied on receive. It must be 0..43200. While a handler runs, LiteBus extends visibility once per half of this timeout, so a long-running handler keeps its receipt handle alive. |
| `RequeueVisibilityTimeoutSeconds` | `int` | `30` | No | Base visibility timeout for a handler-requested requeue. It must be 1..43200. |
| `MaxRequeueVisibilityTimeoutSeconds` | `int` | `900` | No | Maximum requeue backoff. It must be `>= RequeueVisibilityTimeoutSeconds` and `<= 43200`. |
| `RequeueBackoffMultiplier` | `double` | `2.0` | No | Factor by which the delay grows per prior receive attempt. It must be finite and `>= 1`. |
| `PollBackoffInitial` | `TimeSpan` | `500 ms` | No | Delay before polling again after a fully failed batch. It must be `> TimeSpan.Zero`. |
| `PollBackoffMax` | `TimeSpan` | `30 seconds` | No | Maximum poll backoff. It must be `>= PollBackoffInitial`. |
| `PollBackoffMultiplier` | `double` | `2.0` | No | Factor by which the delay grows per consecutive full-batch failure. It must be finite and `>= 1`. |

#### `AzureServiceBusTransportOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `ConnectionString` | `string` | none | **Yes** (`required`) | Namespace connection string. It must be non-blank. |
| `ClientId` | `string?` | `null` | No | Sets `ServiceBusClientOptions.Identifier`. |
| `ConnectivityCheckTarget` | `AzureServiceBusDiagnosticTarget?` | `null` | No | Target the connectivity probe peeks: `AzureServiceBusQueueDiagnosticTarget(QueueName)` or `AzureServiceBusSubscriptionDiagnosticTarget(TopicName, SubscriptionName)`. Names must be non-blank. |
| `ConsumerErrorRetryInterval` | `TimeSpan` | `5 seconds` | No | Delay before restarting the processor after a recoverable error. It must be `> TimeSpan.Zero`. |
| `ConsumerErrorRetryMaxInterval` | `TimeSpan` | `1 minute` | No | Maximum restart delay. It must be `>= ConsumerErrorRetryInterval`. |

#### `InMemoryTransportOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `DestinationCapacity` | `int` | `1024` (`DefaultDestinationCapacity`) | No | Bounded channel capacity per destination. `InMemoryTransportModule` requires a value `>= 1`. |

### 3.10 Diagnostics, health and management options

#### `DiagnosticCheckRunOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MaxParallelism` | `int` | `4` | No | Limit on concurrent probes, enforced with a `SemaphoreSlim`. It must be `> 0`. |
| `Timeout` | `TimeSpan` | `5 seconds` | No | Per-probe timeout. A timed-out probe reports `Unhealthy` with description "The diagnostic check timed out." It must be `> 0` ticks. |
| `RunTimeout` | `TimeSpan?` | `null` | No | Overall run budget. It must be non-negative. When `null`, LiteBus sets the budget to the worst-case packing of the probes into `MaxParallelism` lanes at `Timeout` per probe. LiteBus reports probes still running when the budget expires as `Unhealthy` with description "The diagnostic run exceeded its budget." |

#### `LiteBusHealthCheckOptions` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `FailHealthWhenNoProbes` | `bool` | `true` | No | When `true` and the manifest has zero probes, the run reports `Degraded` with a synthetic probe named `litebus.probes`. |
| `DiagnosticChecks` | `DiagnosticCheckRunOptions` | `new()` | No | Timeout and parallelism for the probe run. |

`AddLiteBus(this IHealthChecksBuilder, Action<LiteBusHealthCheckOptions>? configure = null, string name = "litebus")` registers the check with failure status `Unhealthy` and tags `litebus` and `ready`. It throws `ArgumentException` for a blank `name`, `ArgumentNullException` for a null `options.DiagnosticChecks`, and `ArgumentOutOfRangeException` for non-positive `MaxParallelism` or `Timeout` ticks or a negative `RunTimeout`. It runs these checks before registering the options singleton and calling `AddCheck<LiteBusHealthCheck>`.

#### `LiteBusManagementOptions` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `RoutePrefix` | `string` | `"litebus"` | No | Route prefix. LiteBus trims slashes from it. It must be non-blank. |
| `AllowAnonymousManagement` | `bool` | `false` | No | When `false`, every management route calls `RequireAuthorization`. |
| `AuthorizationPolicy` | `string?` | `null` | No | Named policy that replaces the default authorization requirement. |
| `FailHealthWhenNoProbes` | `bool` | `true` | No | Applies to `GET {prefix}/health`. |
| `DiagnosticChecks` | `DiagnosticCheckRunOptions` | `new()` | No | Timeout and parallelism for `GET {prefix}/health`. It must be non-null with positive values. |
| `DefaultDrainTimeout` | `TimeSpan` | `30 seconds` | No | Drain timeout when the caller supplies none. It must be positive and `<= MaxDrainTimeout`. |
| `MaxPageSize` | `int` | `100` | No | Upper bound that LiteBus applies to `PageSize` on query endpoints. It must be positive. |
| `MaxBulkMessageIds` | `int` | `1000` | No | Maximum message identifiers one bulk request or message query filter accepts. It must be positive. |
| `MaxDrainTimeout` | `TimeSpan` | `5 minutes` | No | Maximum drain timeout the endpoint accepts. It must be positive. |

#### Diagnostic result shapes

| Type | Shape |
| --- | --- |
| `DiagnosticCheckDescriptor` | `(Type ImplementationType, string Name)` |
| `DiagnosticResult` | `(DiagnosticStatus Status, string Description, IReadOnlyDictionary<string, object>? Data = null)` |
| `DiagnosticProbeOutcome` | `(string Name, DiagnosticStatus Status, string Description, IReadOnlyDictionary<string, object>? Data)` |
| `DiagnosticCheckRunResult` | `(DiagnosticAggregateStatus Status, IReadOnlyList<DiagnosticProbeOutcome> Probes)` |

#### `StoreSchemaInfo` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Component` | `string` | positional | **Yes** | Logical store name such as inbox or outbox. |
| `ExpectedVersion` | `int` | positional | **Yes** | Schema version the code expects. |
| `RecordedVersion` | `int` | positional | **Yes** | Version the metadata table records. |
| `SchemaName` | `string?` | `null` | No | Database schema, when applicable. |
| `TableName` | `string?` | `null` | No | Table name, when applicable. |

The static `StoreSchemaInfo.ForLogicalStore(component, version)` covers stores with no persisted metadata.

#### `RetentionRunStatus` (sealed record, positional)

| Property Name | Data Type | Description & Impact |
| --- | --- | --- |
| `Enabled` | `bool` | Whether the cleanup loop is configured to run. |
| `Retention` | `TimeSpan?` | Configured retention window. `null` disables cleanup. |
| `Interval` | `TimeSpan` | Configured cleanup interval. |
| `LastRunAt` | `DateTimeOffset?` | Timestamp of the last pass. |
| `LastDeletedCount` | `int` | Rows the last pass deleted. |
| `LastError` | `string?` | Error message from the last failed pass. |

### 3.11 Auditing configuration

#### `AuditedAttribute` (sealed, `AttributeTargets.Class | AttributeTargets.Struct`, `Inherited = false`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Action` | `string` | constructor argument | **Yes** | Use-case identity that LiteBus writes to the record, for example `orders.place-order`. |
| `Category` | `string?` | `null` | No | Grouping for review and retention. |
| `TargetKind` | `string?` | `null` | No | Kind of resource the action affects. |
| `ReasonRequired` | `bool` | `false` | No | When `true` and the outcome is `Succeeded` with no reason recorded, `AuditRecordWriter` throws `AuditReasonMissingException` at `HandlerPriorities.Observability`, so LiteBus rolls the work back instead of recording it without its justification. |

#### `AuditExemptAttribute` (sealed, `AttributeTargets.Class | AttributeTargets.Struct`, `Inherited = false`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Rationale` | `string` | constructor argument | **Yes** | Recorded reason for leaving the message unaudited. It must be non-blank, or the constructor throws `ArgumentException`. Analyzer `LB1018` reports messages that declare neither position. |

Both attributes implement `IMessageDeclarationSource` with `DeclarationType => typeof(AuditDeclaration)`. `AuditExemptAttribute` also implements `IMessageDeclarationExemptionSource`, so it records the same `DeclarationExemption` that `[DeclarationExempt(typeof(AuditDeclaration), rationale)]` records. One mechanism therefore offers two spellings, and every exemption on a message stays readable from one place.

#### `AuditedDeclaration` (sealed record : `AuditDeclaration`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Action` | `string` | constructor | **Yes** | It must be non-blank, or the constructor throws `ArgumentException`. |
| `Category` | `string?` | `null` | No | Grouping. |
| `TargetKind` | `string?` | `null` | No | Resource kind. |
| `ReasonRequired` | `bool` | `false` | No | LiteBus enforces it at write time against the final resolved reason. |
| `TargetSelector` | `Func<object, string?>?` | `null` | No | Projection from the message to the target id. Callers set it through `WithTargetFrom<TMessage>`. LiteBus uses it only when `IAuditScope` did not supply the target. A null or blank result means no target. |
| `ReasonSelector` | `Func<object, string?>?` | `null` | No | Projection from the message to the reason. Callers set it through `WithReasonFrom<TMessage>`. LiteBus uses it only when `IAuditScope` and the pipeline decision reason are both silent. A null or blank result means no reason. |
| `IsAudited` | `bool` (override) | `true` | - | Discriminates from `AuditExemptDeclaration`. |

#### `AuditExemptDeclaration` (sealed record : `AuditDeclaration`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Rationale` | `string` | constructor | **Yes** | It must be non-blank. |
| `IsAudited` | `bool` (override) | `false` | - | - |

The factories are `AuditDeclaration.Audited(string action)` and `AuditDeclaration.Exempt(string rationale)`. The base constructor is `internal`, so only these two positions extend the hierarchy.

#### `AuditRecord` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Action` | `string` | none | **Yes** (`required`) | From the declaration. |
| `Actor` | `AuditActor?` | `null` | No | From `IAuditActorResolver.Resolve`, overridden by `IAuditScope.WithActor`. A null value means nothing recorded who acted, which is a distinct answer from a named process. |
| `Outcome` | `AuditOutcome` | none | **Yes** (`required`) | From `IAuditOutcomeMapper.Map`. |
| `OccurredAt` | `DateTimeOffset` | none | **Yes** (`required`) | `TimeProvider.GetUtcNow()`. |
| `Duration` | `TimeSpan` | `TimeSpan.Zero` | No | Mediation duration from the completion context. |
| `Category` | `string?` | `null` | No | From the declaration. |
| `TargetKind` | `string?` | `null` | No | From the declaration. |
| `TargetId` | `string?` | `null` | No | From `IAuditScope.WithTarget`. Otherwise it comes from the declaration `TargetSelector`, when the declaration sets one and it yields a value. |
| `Reason` | `string?` | `null` | No | From `IAuditScope.Reason` when set. Otherwise it comes from the pipeline decision reason, or from the declaration `ReasonSelector` when the declaration sets one and it yields a value. |
| `FailureCode` | `string?` | `null` | No | From `IAuditOutcomeMapper.MapFailureCode`. It holds the refusal's own `Code` for a `Denied` or `Invalid` outcome, otherwise the exception type name, and `null` for an uncoded refusal. LiteBus excludes a shortcut's code, because an answered mediation is a success. |
| `MessageType` | `string?` | `null` | No | `messageType.FullName`. |
| `CorrelationId` | `string?` | `null` | No | LiteBus reads it from execution-context item `__LiteBus.Trace.CorrelationId`. |
| `TenantId` | `string?` | `null` | No | LiteBus reads it from execution-context item `__LiteBus.Trace.TenantId`. |
| `Properties` | `IReadOnlyDictionary<string, string>` | empty, ordinal comparer | No | LiteBus copies them from `IAuditScope.WithProperty` calls. |

The message itself is deliberately absent. The pipeline hands it to `IAuditActorResolver` instead, so a payload cannot reach audit storage by default. A record that holds a payload turns an audit table into an erasure liability, and what changed is already stored in the domain event stream under its own retention rule.

#### `AuditActor` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Id` | `string` | none | **Yes** (`required`) | Stable identifier that stays resolvable for as long as the trail is retained. Prefer a surrogate key over an address. |
| `Kind` | `string` | none | **Yes** (`required`) | It names what sort of thing acted. `AuditActor.UserKind` (`user`), `AuditActor.SystemKind` (`system`), or an application-defined code. It separates the actions people took from the actions a process took. It is required because an actor of no kind is a state no audit query can use, and no factory produces one. |
| `DisplayName` | `string?` | `null` | No | The name as it stood when the action happened. It makes the entry readable after the account is deleted, and it makes the trail hold personal data. LiteBus does not populate it, and no factory takes it. |
| `OnBehalfOf` | `string?` | `null` | No | The delegating actor. It separates support staff acting as a customer from the customer acting, and a device acting on a key from the person who authorized it. |

The factories are `User(id)`, `System(processName)`, and `For(kind, id)`. All reject a blank `kind` or `id` with `ArgumentException`.

Callers set a display name with `with { DisplayName = name }` rather than passing it to a factory. `DisplayName` is the field that makes the trail hold personal data, so an optional factory argument would make the easiest spelling at a call site the one the documentation warns about. Setting it separately stays one line, and a reviewer can see that line:

```csharp
AuditActor.User(account.Id.ToString()) with { DisplayName = account.Email };
```

`Kind` stays a string rather than an enumeration. The set of things that can act belongs to the application, and an application with a closed set keeps that set closed at the point it constructs the actor. A switch over its own union returns an `AuditActor`, which keeps compile-time checking at the construction site and leaves the record shape open. That open shape is what a SIEM schema expects.

#### `AuthorizedAttribute` (sealed, `AttributeTargets.Class | AttributeTargets.Struct`, `Inherited = false`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Policy` | `string` | constructor argument | **Yes** | The application policy under which the message needs authorization, for example `vaults.open`. Creating the declaration with a blank value fails. |

#### `AuthorizationExemptAttribute` (sealed, `AttributeTargets.Class | AttributeTargets.Struct`, `Inherited = false`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Rationale` | `string` | constructor argument | **Yes** | Recorded reason the message carries no authorization requirement. It must be non-blank, or the constructor throws `ArgumentException`. |

Both attributes implement `IMessageDeclarationSource` with `DeclarationType => typeof(AuthorizationDeclaration)`. `AuthorizationExemptAttribute` also implements `IMessageDeclarationExemptionSource`, so it records the same `DeclarationExemption` that `[DeclarationExempt(typeof(AuthorizationDeclaration), rationale)]` records.

#### `AuthorizedDeclaration` (sealed record : `AuthorizationDeclaration`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Policy` | `string` | constructor | **Yes** | It must be non-blank, or the constructor throws `ArgumentException`. |
| `RequiresAuthorization` | `bool` (override) | `true` | - | Discriminates from `AuthorizationExemptDeclaration`. |

#### `AuthorizationExemptDeclaration` (sealed record : `AuthorizationDeclaration`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Rationale` | `string` | constructor | **Yes** | It must be non-blank. |
| `RequiresAuthorization` | `bool` (override) | `false` | - | - |

The factories are `AuthorizationDeclaration.Authorized(string policy)` and `AuthorizationDeclaration.Exempt(string rationale)`. The base constructor is `internal`, so only these two positions extend the hierarchy. `IAuthorizationDefinition<TMessage>` is the keyed definition shape (`AuthorizationDeclaration Authorization { get; }`), and `IMessageDeclarations.Authorized(policy)` / `AuthorizationExempt(rationale)` are the `Describe` helpers. `AuthorizationExempt` records both the declaration and the exemption, exactly as `NotAudited` does.

LiteBus carries the position and never evaluates the policy. An application guard reads it through `IMessageMetadataAccessor`, and `RequireDeclaration<AuthorizationDeclaration>()` plus `LB1020` enforce that every message states one.

### 3.12 Contracts, metadata and payload protection

#### `MessageContractAttribute` (sealed, `Class | Struct`, `Inherited = false`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Name` | `string` | constructor | **Yes** | Stable contract name that LiteBus persists with the payload. |
| `Version` | `int` | `1` | No | Positive contract version. |

#### `MessageContract` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Name` | `string` | none | **Yes** (`required`) | Persisted name. |
| `Version` | `int` | none | **Yes** (`required`) | Persisted version. |
| `MessageType` | `Type` | none | **Yes** (`required`) | Concrete CLR type. |

#### `PayloadProtectionContext` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MessageId` | `Guid` | none | **Yes** (`required`) | LiteBus binds it as authenticated metadata. |
| `ContractName` | `string` | none | **Yes** (`required`) | LiteBus binds it as authenticated metadata. |
| `ContractVersion` | `int` | none | **Yes** (`required`) | LiteBus binds it as authenticated metadata. |
| `TenantId` | `string?` | `null` | No | LiteBus binds it when present. Empty and whitespace values normalize to `null`. |
| `Axis` | `string` | none | **Yes** (`required`) | Literal `inbox` or `outbox`, set by the LiteBus writers and dispatchers. |

#### Handler attributes

| Attribute | Target | Members | Notes |
| --- | --- | --- | --- |
| `HandlerPriorityAttribute` | `Class` | `int Priority` (constructor) | Lower values run first. When the attribute is absent, the priority is `HandlerPriorities.Default` (`0`). |
| `HandlerTagAttribute` | `Class`, `AllowMultiple = true` | `string Tag` | It may repeat on one handler. |
| `HandlerTagsAttribute` | `Class` | `string[] Tags` (`params`) | Bulk form. LiteBus combines it with any `HandlerTagAttribute` values. |

### 3.13 Composition builders (the configuration surface)

#### `MessageModuleBuilder`

| Member | Signature | Description & Impact |
| --- | --- | --- |
| `Contracts` | `IContractWriter { get; }` | Live contract registry writer. |
| `MessageModuleBuilder(...)` | `(IMessageRegistry messageRegistry, IContractWriter contracts)` | LiteBus constructs it in `MessageModule.Build`. Application code receives the builder through the `AddMessageModule` configuration action. |
| `UseTimeProvider` | `(TimeProvider) -> MessageModuleBuilder` | Registers the `TimeProvider` that auditing, envelope factories, processors, and stores use. |
| `UseSerializer<TMessageSerializer>` | `(InstanceLifetime lifetime = Singleton) -> MessageModuleBuilder`, `TMessageSerializer : class, IMessageSerializer` | Registers a container-constructed serializer. It decides the wire format of every persisted message. |
| `UseSerializerInstance` | `(IMessageSerializer) -> MessageModuleBuilder` | Registers a pre-created serializer, necessarily a **Singleton**. Use this overload when the serializer is configured rather than injected: `UseSerializerInstance(new SystemTextJsonMessageSerializer(options))`. |
| `UseTelemetry` | `(MediationTelemetryOptions) -> MessageModuleBuilder` | Sets the process-wide `ActivitySource` and `Meter` options. It applies before anything mediates. |
| `UseAuditTrail<TAuditTrail>` | `(InstanceLifetime lifetime = Scoped) -> MessageModuleBuilder`, `TAuditTrail : class, IAuditTrail` | Registers a container-constructed trail. The lifetime is an explicit parameter, not a consequence of the overload. |
| `UseAuditTrailInstance` | `(IAuditTrail) -> MessageModuleBuilder` | Registers a pre-created trail, necessarily a **Singleton**. The method name states the lifetime, because a trail built here with a scoped session captures it for the life of the process. |
| `UseAuditRecordWriter<TAuditRecordWriter>` | `(InstanceLifetime lifetime = Scoped) -> MessageModuleBuilder` | Replaces the built-in record writer so the application owns the record shape. |
| `UseAuditRecordWriterInstance` | `(IAuditRecordWriter) -> MessageModuleBuilder` | Registers a pre-created record writer, necessarily a **Singleton**. |
| `UseAuditActorResolver<TAuditActorResolver>` | `(InstanceLifetime lifetime = Scoped) -> MessageModuleBuilder` | Registers a container-constructed actor resolver. |
| `UseAuditActorResolver` | `(Type, InstanceLifetime = Scoped) -> MessageModuleBuilder` | Registers the resolver implementation named as a `Type`, for composition code that chooses the resolver at runtime. The type must be a concrete class implementing `IAuditActorResolver`. |
| `UseAuditActorResolverInstance` | `(IAuditActorResolver) -> MessageModuleBuilder` | Registers a pre-created actor resolver, necessarily a **Singleton**. |
| `UseAuditOutcomeMapper` | `(IAuditOutcomeMapper) -> MessageModuleBuilder` | Overrides `DefaultAuditOutcomeMapper`. |
| `UseAuditOutcomeMapper<T>` | `() -> MessageModuleBuilder`, `T : IAuditOutcomeMapper, new()` | Constructs and registers the mapper. |
| `AddAuditing` | `(Action<AuditingBuilder>) -> MessageModuleBuilder` | Configures the whole auditing feature in one call, including axis selection. This is **the path to take**. It composes the per-axis primitives above. |
| `UseFaultRefusalClassifier<TClassifier>` | `() -> MessageModuleBuilder`, `TClassifier : class, IFaultRefusalClassifier` | Registers a container-constructed classifier as a Singleton collection item. Several are allowed, and the first match wins. |
| `UseFaultRefusalClassifierInstance` | `(IFaultRefusalClassifier) -> MessageModuleBuilder` | Registers a pre-created classifier, necessarily a **Singleton**. Use this overload for a configured classifier, such as one built over a constraint map. |
| `RequireDeclaration<TValue>` | `() -> MessageModuleBuilder` | Every message must state a position on `TValue`. It is unscoped, so it applies to commands, queries, and events alike, which is usually wider than needed. |
| `RequireDeclaration<TValue, TScope>` | `() -> MessageModuleBuilder` | Only messages assignable to `TScope` must declare. This is **the form to reach for**. |
| `RequireDeclaration<TValue>` | `(Func<Type, bool> scope, string scopeDescription) -> MessageModuleBuilder` | Applies a predicate scope, with a description that LiteBus uses in the failure message. |
| `RequireUniqueAuditActions` | `() -> MessageModuleBuilder` | Two messages sharing an action code make the trail unqueryable by use case. It raises `AuditConfigurationException`, naming each duplicate and its claimants. |
| `RequireAuditActionFormat` | `(string pattern = DefaultAuditActionPattern) -> MessageModuleBuilder` | The pattern defaults to the `category.kebab-action` convention. It raises `AuditConfigurationException`, naming each offender and its action. |
| `RequireExplicitOpenGenerics` | `() -> MessageModuleBuilder` | Callers must close open generic handlers explicitly instead of relying on inference. |
| `ValidateComposition` | `(Action<IMessageCatalog>) -> MessageModuleBuilder` | Runs an application check over the composed catalog at build time. |
| `DeclareDefault<TScope, TValue>` | `(TValue value) -> MessageModuleBuilder` | Supplies a default declaration value for every message in scope. |
| `DeclareDefault` | `(MessageDeclarationItem) -> MessageModuleBuilder` | This is the untyped form of the same method. |
| `Register<T>()` / `Register(Type)` | `-> MessageModuleBuilder` | Registers any message or handler type with no axis validation. |
| `RegisterFromAssembly` | `(Assembly) -> MessageModuleBuilder` | Registers **every** type in the assembly with the message registry and calls `Contracts.AddFromAssembly`. |

#### `CommandModuleBuilder` / `QueryModuleBuilder`

| Member | Description & Impact |
| --- | --- |
| `Contracts` | `IContractWriter` for durable command/query contracts. |
| `EnableAuditing()` | Registers `CommandAuditCompletionHandler` / `QueryAuditCompletionHandler` (priority `HandlerPriorities.Observability`) and the `litebus.audit.trail` diagnostic probe. On every axis, the `AddAuditing` selection on the messaging module implies the same call at build time. |
| `EnableIdempotency()` | Commands only. Registers `IdempotentCommandShortcut<>` and `IdempotentCommandShortcut<,>`, `IdempotencyCompletionHandler` (priority `HandlerPriorities.Persistence`), and the `litebus.idempotency.store` probe. |
| `UseUnitOfWork<TUnitOfWork>()` | Both axes. Registers `TUnitOfWork` (`class, IUnitOfWork`) as itself, plus an `IUnitOfWork` mapping where the first registration wins (both Scoped), and a completion handler at `HandlerPriorities.UnitOfWork` that calls `CommitAsync` only when the mediation completed successfully. It throws `LiteBusConfigurationException` when called twice on one builder. |
| `UsePostgreSqlIdempotencyStore(...)` | Commands only, from `LiteBus.Commands.Storage.PostgreSql`. Registers the PostgreSQL `IIdempotencyStore` child module: `UseDataSource` / `UseConnectionString` (one required), `UseOptions(PostgreSqlIdempotencyStoreOptions)`, `DisableSchemaInitialization()`. |
| `UseMartenUnitOfWork()` | Commands only, from `LiteBus.Commands.Storage.Marten`. Delegates to `UseUnitOfWork<MartenUnitOfWork>`. Query mediations reuse the same session by calling the query `UseUnitOfWork` hook with `MartenUnitOfWork` directly. |
| `UseMartenIdempotencyStore(...)` | Commands only, from `LiteBus.Commands.Storage.Marten`. Registers the Marten `IIdempotencyStore` child module with an optional `Action<MartenIdempotencyStoreOptions>` configure action. |
| `UsePostgreSqlRecurringLease(...)` | Commands only, from `LiteBus.Commands.Recurring.Storage.PostgreSql`. Registers the PostgreSQL `IRecurringCommandLease` child module: `UseDataSource` / `UseConnectionString` (one required), `UseOptions(PostgreSqlRecurringLeaseOptions)`, `DisableSchemaInitialization()`. |
| `Register<T>()` / `Register(Type)` | Validates that the type is a command/query construct. Otherwise it throws `LiteBusNotSupportedException`. |
| `RegisterFromAssembly(Assembly)` | Registers every concrete, non-abstract command/query construct it finds. |
| `RegisterChildModule(IModule)` | Commands only. Declares a child module owned by `CommandModule`, which is the seam feature packages compose through. |
| `CollectSubModules()` | Commands only. Returns the declared child modules in declaration order. `CommandModule.DeclareChildren` reads the result. |
| `ApplyContracts(IMessageContractRegistry)` | Commands only. Replays deferred contract registrations onto the shared registry during `Build`. |
| `AddRecurring<TCommand>(...)` | Commands only, from `LiteBus.Commands.Recurring`. Sends one command on a fixed interval for the life of the host. Registers `TCommand` with the command module and `RecurringCommandModule<TCommand>` as a child module, so calling only this method is enough for the loop to send. Two overloads: a factory `Func<TCommand>` (`TCommand : notnull`), which the loop calls once per pass, and a parameterless form (`TCommand : notnull, new()`) that sends `new TCommand()` each pass by delegating to the factory overload. It throws `ArgumentNullException` for a null builder or factory, and `LiteBusConfigurationException` when `TCommand` implements more than one `ICommand<>` contract. |

`CommandModule` is an `ICompositeModule`. Its configuration action runs during `DeclareChildren`, so the builder keeps registrations **queued** until `Build` applies them to the live registry. A feature package can therefore register a child module from inside `AddCommands`. `Build` applies contracts first, then the axis selection `AddAuditing` implies, then queued type and assembly registrations.

A **command construct** is a type assignable to `ICommand`, or implementing any of `ICommandHandler<>`, `ICommandHandler<,>`, `ICommandPreHandler`, `ICommandPreHandler<>`, `ICommandGuard<>`, `ICommandValidator<>`, `ICommandShortcut<>`, `ICommandShortcut<,>`, `ICommandRefusalMapper<,>`, `ICommandPostHandler`, `ICommandPostHandler<>`, `ICommandPostHandler<,>`, `ICommandErrorHandler`, `ICommandErrorHandler<>`, `ICommandErrorHandler<,>`, `ICommandCompletionHandler`, `ICommandCompletionHandler<>`, `ICommandCompletionHandler<,>`, or an `IMessageDefinition<TMessage, TValue>` whose `TMessage` is an `ICommand`, or a pipeline handler written against a messaging-level contract whose message type is assignable to `ICommand`. Both `IMessageDefinition<,>` and `IMessageDefinition<>` shapes count.

A **query construct** mirrors that list with `IQueryHandler<,>`, `IQueryPreHandler`, `IQueryPreHandler<>`, `IQueryGuard<>`, `IQueryValidator<>`, `IQueryShortcut<,>`, `IQueryRefusalMapper<,>`, `IStreamQueryShortcut<,>`, `IStreamQueryRefusalMapper<,>`, `IQueryPostHandler`, `IQueryPostHandler<>`, `IQueryPostHandler<,>`, `IQueryErrorHandler`, `IQueryErrorHandler<>`, `IQueryErrorHandler<,>`, `IStreamQueryHandler<,>`, `IStreamQueryPostHandler<,>`, `IQueryCompletionHandler`, `IQueryCompletionHandler<>`, `IQueryCompletionHandler<,>`, plus `IMessageDefinition<,>` and `IMessageDefinition<>` over an `IQuery`, and messaging-level pipeline handlers whose message type is assignable to `IQuery`.

#### `EventModuleBuilder`

The members are `Contracts`, `EnableAuditing()`, `Register<T>()`, `Register(Type)`, and `RegisterFromAssembly(Assembly)`. `EnableAuditing()` registers `EventAuditCompletionHandler` (priority `HandlerPriorities.Observability`) and the `litebus.audit.trail` diagnostic probe. It writes one record per publish, not per handler.

An **event construct** is `IEvent`, `IEventHandler<>`, `IEventPreHandler`, `IEventPreHandler<>`, `IEventGuard<>`, `IEventValidator<>`, `IEventShortcut<>`, `IEventPostHandler`, `IEventPostHandler<>`, `IEventErrorHandler`, `IEventErrorHandler<>`, `IEventCompletionHandler`, `IEventCompletionHandler<>`, or `IMessageDefinition<,>` over an `IEvent`. Both `IMessageDefinition<,>` and `IMessageDefinition<>` shapes count, as do messaging-level pipeline handlers whose message type is assignable to `IEvent`. `RegisterFromAssembly` also registers an `IMessageDefinition` declared over an unmarked event handled in the same assembly, so a POCO event can carry the audit or idempotency metadata that belongs beside its handler. A POCO event type is any type that an `IEventHandler<TEvent>` names as `TEvent` without implementing `IEvent`.

#### `RecurringCommandHostOptions` (sealed class, from `LiteBus.Commands.Recurring`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | No | When `false`, the loop logs and returns without sending. |
| `Interval` | `TimeSpan` | `1 minute` | No | Delay between sends when the previous send reported no more work. It must be positive. |
| `StartupDelay` | `TimeSpan` | `TimeSpan.Zero` | No | Delay after host start before the first send. Stagger it across loops sharing a database. It must not be negative. |
| `ErrorBackoff` | `TimeSpan` | `5 minutes` | No | Delay after a send throws. The loop uses it instead of `Interval`. It must be positive. |
| `IsConcurrencyConflict` | `Func<Exception, bool>?` | `IsConcurrencyConflictByTypeName` | No | The loop logs a send classified as a conflict at Information and waits `Interval` rather than `ErrorBackoff` after it. Set it to `null` to treat every exception as a failure. |
| `RequireLease` | `bool` | `false` | No | When true, each pass acquires the named `IRecurringCommandLease` before sending. The loop skips a pass that cannot acquire it, logs at Debug (EventId 2105), and waits `Interval`. Composition throws `LiteBusConfigurationException` when no lease is registered. One lease covers the whole `HasMoreWork` busy period, and the loop ends it early when the handle's `LeaseLost` token fires. |
| `LeaseName` | `string?` | `null` (meaning the command type full name) | No | The lease name each pass acquires before sending. Loops sharing a name coordinate through the lease, so only one of them sends at a time. `Validate()` throws `ArgumentException` for a blank value. |

`IsConcurrencyConflictByTypeName(Exception)` returns `true` when the exception, an inner exception, or an `AggregateException` inner exception has a type name ending in `ConcurrencyException`, which recognizes EF Core `DbUpdateConcurrencyException` and Marten `ConcurrencyException` without referencing persistence packages. `Validate()` throws `ArgumentOutOfRangeException` when `Interval` or `ErrorBackoff` is zero or negative, or `StartupDelay` is negative. Both the module `Build` and the background service call it at startup.

#### `InboxModuleBuilder`

| Member | Type / Signature | Default | Description & Impact |
| --- | --- | --- | --- |
| `Contracts` | `IContractWriter` | deferred `MessageContractBuilder` | LiteBus replays registrations onto the shared registry during `Build`. |
| `ProcessorOptions` | `InboxProcessorOptions { get; }` | Stored options, `HookFailurePolicy` defaulting to `DeadLetter` | Computed value. It returns the options `UseProcessorOptions` stored, or `new()` defaults, and sets `HookFailurePolicy` to `ProcessorHookFailurePolicy.DeadLetter` when unset. |
| `ProcessorHostOptions` | `InboxProcessorHostOptions { get; }` | `new()` | The `EnableInboxProcessor` callback mutates it. |
| `CleanupHostOptions` | `InboxCleanupHostOptions { get; }` | `new()` | The `EnableCleanup` callback mutates it. |
| `IsInboxProcessorEnabled` | `bool` | `false` | `InboxModule.Build` validation reads it. |
| `IsCleanupEnabled` | `bool` | `false` | `InboxModule.Build` reads it. |
| `IsStorageConfigured` | `bool` | `false` | When it is still false, `Build` throws `DurableStorageConfigurationException`. |
| `IsDispatcherConfigured` | `bool` | `false` | When the processor is enabled but no dispatcher is configured, `Build` throws. |
| `IsPayloadEncryptionConfigured` | `bool` | `false` | Whether the application called `UsePayloadEncryption`. |
| `EnableInboxProcessor(Action<InboxProcessorHostOptions>? configure = null)` | fluent | - | Registers `InboxProcessorBackgroundService` and processor control services. Requires a dispatcher. |
| `EnableCleanup(Action<InboxCleanupHostOptions>? configure = null)` | fluent | - | Registers `InboxCleanupBackgroundService`. |
| `UseProcessorOptions(InboxProcessorOptions)` | fluent | - | Replaces batch, lease, and retry settings. |
| `RegisterStorage(IInboxStorageModule)` | fluent | - | Exactly one is allowed. A second call throws `DurableStorageConfigurationException`. |
| `RegisterDispatcher(IInboxDispatcherModule)` | fluent | - | Exactly one is allowed. A second call throws. |
| `RegisterIngress(IInboxIngressModule)` | fluent | - | Exactly one is allowed. A second call throws. |
| `RegisterSaga(IModule)` | fluent | - | Adds a feature-bridge child module. `EnableSaga` uses it. |
| `UsePayloadEncryption(IPayloadEncryptor)` | fluent | - | Registers `IInboxPayloadProtector`. |
| `UseAmbientTrace()` | fluent | off | Stamps accepted envelopes that carry no explicit trace with `MessageTrace.FromAmbient()`. `IsAmbientTraceEnabled` reports it. |
| `AddDiagnosticCheck<TCheck>(string name)` | fluent, `TCheck : IDiagnosticCheck` | - | Adds a consumer-owned probe (Singleton) to the host manifest. |
| `CollectSubModules()` | `IReadOnlyList<IModule>` | - | Collects modules in this order: storage, dispatcher, ingress, feature bridges. |
| `ApplyContracts(IMessageContractRegistry)` | `void` | - | Replays deferred contract registrations. |

#### `OutboxModuleBuilder`

The outbox builder has the same shape, without ingress, saga, or `IsPayloadEncryptionConfigured`, with `EnableOutboxProcessor` and `IsOutboxProcessorEnabled` in place of the inbox names. `UseContractResolver(IMessageContractResolver)` (outbox only) registers the resolver instance as the `IMessageContractResolver` the outbox envelope factory consults during enqueue, so the declared parameter type can drive contract selection instead of the runtime instance type. When no resolver is registered, writers use the runtime message instance type. `LiteBus.Messaging.DeclaredTypeMessageContractResolver` is the shipped implementation and returns the declared type. `IOutboxStorageModule : IModule` is a marker interface with no members for `RegisterStorage`.

`UseAmbientTrace()` stamps enqueued envelopes that carry no explicit trace with `MessageTrace.FromAmbient()`. `EnablePerHandlerDelivery()` writes one envelope per subscribed main handler of an event, and records the handler on `OutboxEnvelope.Handler`. At build, LiteBus throws `DurableStorageConfigurationException` for per-handler delivery composed with a transport dispatcher, unless the application calls `AllowPerHandlerDeliveryWithTransportDispatch()` to acknowledge the duplicate broker publication. `StageCollectedEvents()` registers the scoped `IDomainEventCollector` plus the staging completion handler at `HandlerPriorities.Persistence`.

`ProcessorOptions` returns the stored options `with { HookFailurePolicy = dispatcherModule.DefaultHookFailurePolicy }` when `HookFailurePolicy` is unset and a dispatcher is registered, independent of registration order and of whether `UseProcessorOptions` was called. A value the caller set takes precedence. Transport dispatch modules report `CompleteDespiteHookFailure`. The in-process dispatcher inherits the `DeadLetter` default.

The recommendation is declared on `IOutboxDispatcherModule : IModule` as a default interface member (`DeadLetter`). Only `TransportOutboxDispatchModule<TTransportModule>` overrides it, to `CompleteDespiteHookFailure`. The same interface declares a second default member, `PublishesFullEventPerEnvelope` (`false`). Transport dispatch modules return `true`, which is what the per-handler guard reads. Overriding the transport default with `DeadLetter` dead-letters a message the broker already accepted, so after replay LiteBus re-leases the message and publishes it a second time.

#### Storage builders

`InMemoryInboxStorageModuleBuilder` / `InMemoryOutboxStorageModuleBuilder`

| Member | Default | Description & Impact |
| --- | --- | --- |
| `Options` | `new()` | `UseOptions(InMemoryXStoreOptions)`. |
| `TimeProvider` | `null` | `UseTimeProvider(TimeProvider)`. When set, LiteBus also registers it as the container's `TimeProvider`. |

`PostgreSqlInboxModuleBuilder` / `PostgreSqlOutboxModuleBuilder`

| Member | Default | Description & Impact |
| --- | --- | --- |
| `DataSource` | `null` | Callers set it with `UseDataSource(NpgsqlDataSource)` (not owned) or `UseConnectionString(string)` (owned). LiteBus registers an owned data source through a container-owned `PostgreSqlDataSourceOwner<TModule>` that every store registration resolves, so disposing the service provider disposes it. `PostgreSqlDataSourceOwnershipTests` verifies ownership with Microsoft.Extensions.DependencyInjection. **Required**: `Build` throws when it is null. |
| `Options` | `new()` | `UseOptions(...)`. |
| `EnableSchemaInitialization` | `true` | `DisableSchemaInitialization()` removes the startup task. |
| `EnsureSchemaCreationOnStartup()` | - | Sets `Options.EnsureSchemaCreationOnStartup = true`. |
| `EnableAmbientTransactionProviderRegistration` | `false` | Set by `EnableAmbientTransactionProvider(TransactionalWriteMode mode = RequireActiveTransaction)`. It registers scoped `ITransactionalInbox` / `ITransactionalOutbox` over `IPostgreSqlTransactionProvider`. |
| `TransactionalWriteMode` | `RequireActiveTransaction` | See the enum catalog. |

`UseMartenTransactionalOutbox()` (outbox only, from `LiteBus.Outbox.Storage.Marten`) registers scoped `ITransactionalOutbox` backed by the current Marten `IDocumentSession`. It queues staged envelopes with `QueueSqlCommand`, and they commit with `SaveChangesAsync` alongside documents. The application registers Marten itself with `AddMarten` against the same database. LiteBus does not register Marten.

It cannot be combined with `EnableAmbientTransactionProvider()`. When the application configures both, module build throws `OutboxPostgreSqlStorageConfigurationException`.

`EfCoreInboxStorageModuleBuilder` / `EfCoreOutboxStorageModuleBuilder`

| Member | Default | Description & Impact |
| --- | --- | --- |
| `DbContextType` | `null` | Callers set it with `UseDbContext<TContext>()`, where `TContext : DbContext, IInboxDbContext` / `IOutboxDbContext`. **Required**: `Build` throws `DurableStorageConfigurationException` when it is null. |
| `Options` | `new()` | `UseOptions(...)`. |
| `RegisterSaveChangesInterceptor` | `false` | `EnableSaveChangesInterceptor()` registers `LiteBusInboxSaveChangesInterceptor` / `LiteBusOutboxSaveChangesInterceptor` (Singleton) and the scoped `ITransactionalInbox<TContext>` / `ITransactionalOutbox<TContext>`. The outbox side additionally registers a scoped non-generic `ITransactionalOutbox` (`TransactionalOutboxAdapter<TContext>`) over the generic writer, so non-generic consumers drain into the ambient EF transaction. |
| `RequireTransactionalSetup` | `false` | `EnforceTransactionalSetup()` makes `Build` throw when the application did not enable the interceptor. |

#### Ingress builders

`AmqpInboxIngressModuleBuilder`, `KafkaInboxIngressModuleBuilder`, `AwsSqsInboxIngressModuleBuilder`, `AzureServiceBusInboxIngressModuleBuilder` and `InMemoryInboxIngressModuleBuilder` all expose:

| Member | Default | Description & Impact |
| --- | --- | --- |
| `Options` | `new()` for AMQP and in-memory; `null!` for Kafka, SQS and Service Bus | `UseOptions(...)`. The three builders that start null require it. |
| `HostOptions` | `new TransportInboxIngressHostOptions()` | Controls the loop lifecycle and restart delay. |
| `EnableIngressConsumer` | `true` | `DisableIngressConsumer()` clears it and skips registering the background consumer. |
| `ConfigureHost(Action<TransportInboxIngressHostOptions>)` | - | Only the Kafka builder offers this. |

#### `SagaModuleBuilder`

| Member | Description & Impact |
| --- | --- |
| `IsStorageConfigured` | Whether the application chose a store. |
| `DefineState<TState>(string sagaDefinitionId)` and `RegisterState<TState>(string)` | Registers the saga state CLR type for a definition id (`TState : class, new()`). |
| `MapContract(string contractName, string sagaDefinitionId)` | Routes one durable contract to a saga definition. |
| `MapState<TState>(string contractName)` | Shorthand that uses the contract name as the definition id. |
| `UseInMemoryStorage()` | Selects `InMemorySagaStorageModule`. |
| `RegisterStorage(ISagaStorageModule)` | Exactly one store is allowed. A second call throws `DurableStorageConfigurationException`. Collecting with no store registered throws. |

`PostgreSqlSagaModuleBuilder` offers `UseDataSource(NpgsqlDataSource)`, `UseConnectionString(string)`, `UseOptions(PostgreSqlSagaStoreOptions)`, and `DisableSchemaInitialization()`, plus an `Options` property (`PostgreSqlSagaStoreOptions`, default `new()`). The saga builder selects it with `UsePostgreSqlStorage(Action<PostgreSqlSagaModuleBuilder>)` (from `LiteBus.Saga.Storage.PostgreSql`), which registers `PostgreSqlSagaModule` as the single storage module.

#### `DependencyDescriptor` (sealed class, `IEquatable<DependencyDescriptor>`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `DependencyType` | `Type` | constructor | **Yes** | Service type. |
| `ImplementationType` | `Type?` | `null` | No | The type-registration constructors set it. It must be a concrete class assignable to, or an open generic closing, the service type. |
| `Instance` | `object?` | `null` | No | Set by the instance constructor, which forces `Singleton`. It must be assignable to the service type. |
| `Factory` | `Func<IServiceProvider, object>?` | `null` | No | The factory constructors set it. |
| `Lifetime` | `InstanceLifetime` | `Transient` for type and factory forms, `Singleton` for the instance form | No | LiteBus validates it with `Enum.IsDefined`. |
| `IsCollectionRegistration` | `bool` | `false` | No | `true` only via the `ForCollection` factories. Such descriptors must go through `IDependencyRegistry.RegisterCollection`, and non-collection descriptors must not. |

The statics are `ForCollection(Type, Type, InstanceLifetime = Transient)`, `ForCollection(Type, object)`, and `ForCollection(Type, Func<IServiceProvider, object>, InstanceLifetime = Transient)`. Equality compares service type, lifetime, and the collection flag, then implementation type, or instance/factory by reference.

#### Saga configuration and data types

| Type | Members | Notes |
| --- | --- | --- |
| `SagaCorrelation` (sealed record) | `CorrelationId` (`string`, required), `SagaDefinitionId` (`string`, required), `TenantId` (`string?`, default `null`) | Storage primary key. |
| `SagaInstance<TState>` (sealed class) | `Correlation` (required), `State` (`TState`, required), `Version` (`int`, required), `IsCompleted` (`bool`, required), `LastAppliedMessageId` (`Guid?`, default `null`) | Loaded instance with its optimistic token. `TState : class, new()`. |
| `SagaSaveItem<TState>` (sealed record) | positional `(SagaCorrelation Correlation, TState State, int ExpectedVersion)` + `AppliedMessageId` (`Guid?`) | `From(correlation, state, expectedVersion, appliedMessageId = null)`. When inserting a new saga row, use `ExpectedVersion = 0`. |
| `SagaCompleteItem` (sealed record) | positional `(SagaCorrelation Correlation, int ExpectedVersion)` + `AppliedMessageId` (`Guid?`) | `From(correlation, expectedVersion, appliedMessageId = null)`. |
| `SagaInstanceSummary` (sealed record) | `Correlation` (required), `Version` (`int`, required), `IsCompleted` (`bool`, required), `CreatedAt` (`DateTimeOffset?`), `UpdatedAt` (`DateTimeOffset?`) | What `ISagaStore.QueryAsync` returns. |
| `SagaQueryFilter` (sealed record) | `SagaDefinitionId` (`string?`), `CorrelationId` (`string?`), `TenantId` (`string?`), `IsCompleted` (`bool?`), `Take` (`int`, default `100`) | All filters default to `null`. |
| `SagaPurgeFilter` (sealed record) | `SagaDefinitionId`, `CorrelationId`, `TenantId`, `IsCompleted` (`bool?`), `CompletedBefore` (`DateTimeOffset?`), `Take` (`int?`) | All default to `null`. `Take` bounds the purge to that many matching rows, oldest first. `null` deletes every match. A purge with no predicates and no `Take` fails with `ArgumentException`. |

---

## 4. Feature-by-Feature Deep Dive

### 4.1 Commands

The primary types are `ICommand`, `ICommand<TCommandResult>`, `ICommandMediator`, `CommandMediator`, `CommandMediatorExtensions`, `CommandModule`, and `CommandModuleBuilder`.

The mediator interface declares the following methods:

```csharp
public interface ICommandMediator
{
    Task SendAsync(ICommand command,
                   CommandMediationSettings? commandMediationSettings = null,
                   CancellationToken cancellationToken = default);

    Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                   CommandMediationSettings? commandMediationSettings = null,
                                                   CancellationToken cancellationToken = default);

    Task<MediationResult> TrySendAsync(ICommand command,
                                       CommandMediationSettings? commandMediationSettings = null,
                                       CancellationToken cancellationToken = default);

    Task<MediationResult<TCommandResult>> TrySendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                                       CommandMediationSettings? commandMediationSettings = null,
                                                                       CancellationToken cancellationToken = default);

    Task<MediationDecision> EvaluateAsync(ICommand command,
                                          CommandMediationSettings? commandMediationSettings = null,
                                          CancellationToken cancellationToken = default);
}
```

Each method answers a different question:

| Method | Answers | A refusal |
| --- | --- | --- |
| `SendAsync` | Do this. | The refusal reaches the caller as `LiteBusMessageDeniedException` / `LiteBusMessageInvalidException`, or as the value a refusal mapper supplied. |
| `TrySendAsync` | Do this and report the outcome. | The refusal comes back as a value in `MediationResult`. A genuine fault still throws. |
| `EvaluateAsync` | Whether this may happen. | The answer comes back as a `MediationDecision`. The mediator performs nothing. |

`MediationResult` carries `Outcome`, `Reason`, `Code`, `Failures`, and the predicates `IsSuccess` (`Succeeded` or `Answered`), `IsDenied` and `IsInvalid`. `MediationResult<TResult>` adds `Value` and `HasValue`. `HasValue` is separate from `IsSuccess` because a nullable result type makes `Value` ambiguous on its own, and because a refusal with a registered mapper arrives with both the mapped value and the denied outcome. The factories are `MediationResult.Succeeded()`, `Answered(reason, code)`, `Denied(reason, code)`, and `Invalid(reason, code, failures)`. The generic form adds `Succeeded(value)`, `Answered(value, reason, code)`, `Denied(reason, code, value = default, hasValue = false)`, and `Invalid(reason, code, failures, value = default, hasValue = false)`, where the `value`/`hasValue` pair carries what a registered refusal mapper supplied.

`MediationDecision` is a readonly struct with `Allowed` (the default), `Outcome`, `Reason`, `Code`, `Failures` (never null), the predicates `IsAllowed` (`Outcome == Succeeded`), `IsDenied` and `IsInvalid`, and the factories `Denied(reason, code)` and `Invalid(reason, code, failures)`. Only `Succeeded`, `Denied`, and `Invalid` outcomes are reachable because nothing was performed.

`TrySendAsync` follows the same rule the pipeline follows: a decision comes back as a value, and a fault throws as an exception. An HTTP boundary should not branch on a database timeout, and a result carrying the exception would let a caller swallow it.

`EvaluateAsync` runs only the guard and validator stages. It does not run shortcuts or pre-handlers, because those stages act instead of deciding. The shipped idempotency shortcut claims a key, so evaluating a page full of controls would burn keys for commands nobody submitted.

A guard must therefore have no effects a caller would not want from a "may I" question. A guard may load an aggregate to decide, and the load runs on every evaluation, so if loading is expensive, check `IExecutionContext.Data` before loading.

The evaluation exists to remove the second authorization method an application would otherwise write: one to authorize while doing, and one for a caller that shows or hides a control. Two methods answering the same question drift apart, and that drift is silent and affects security. A button stays visible for an action the pipeline will refuse.

The `CommandMediatorExtensions` class adds these overloads:

| Signature | Return | Behaviour |
| --- | --- | --- |
| `SendAsync(ICommand, CancellationToken)` | `Task` | The overload passes `null` settings. |
| `SendAsync<TCommandResult>(ICommand<TCommandResult>, CancellationToken)` | `Task<TCommandResult>` | The overload passes `null` settings. |
| `SendAsync(ICommand, string tag, CancellationToken)` | `Task` | The overload builds `CommandMediationSettings { Routing = new() { Tags = [tag] } }`. |
| `SendAsync<TCommandResult>(ICommand<TCommandResult>, string tag, CancellationToken)` | `Task<TCommandResult>` | The overload does the same for a typed command. |

Commands use these handler contracts:

| Contract | Method | Notes |
| --- | --- | --- |
| `ICommandHandler<in TCommand>` | `Task HandleAsync(TCommand, CancellationToken = default)` | `TCommand : ICommand`. The contract extends `IAsyncMessageHandler<TCommand>`. Each command has exactly one handler. |
| `ICommandHandler<in TCommand, TCommandResult>` | `Task<TCommandResult> HandleAsync(TCommand, CancellationToken = default)` | `TCommand : ICommand<TCommandResult>`. |
| `ICommandGuard<in TCommand>` | `Task<Verdict> DecideAsync(TCommand, CancellationToken = default)` | The guard runs in stage 1. |
| `ICommandValidator<in TCommand>` | `Task<Validity> ValidateAsync(TCommand, CancellationToken = default)` | The validator runs in stage 2 and aggregates failures. |
| `ICommandShortcut<in TCommand>` | `Task<Shortcut> TryAnswerAsync(TCommand, CancellationToken = default)` | The shortcut runs in stage 3 for void commands. |
| `ICommandShortcut<in TCommand, TCommandResult>` | `Task<Shortcut<TCommandResult>> TryAnswerAsync(...)` | The shortcut runs in stage 3 for result commands. |
| `ICommandPreHandler` / `ICommandPreHandler<in TCommand>` | `Task PreHandleAsync(TCommand, CancellationToken = default)` | The pre-handler runs in stage 4. The non-generic form targets `ICommand`, so it runs for every command. |
| `ICommandPostHandler` / `ICommandPostHandler<in TCommand>` / `ICommandPostHandler<in TCommand, in TCommandResult>` | `Task PostHandleAsync(TCommand, TCommandResult?, CancellationToken = default)` | The post-handler runs after the main handler. The one- and zero-arg forms bind `TCommandResult` to `object`. |
| `ICommandErrorHandler` / `ICommandErrorHandler<TCommand>` / `ICommandErrorHandler<TCommand, TCommandResult>` | `Task HandleErrorAsync(MessageErrorContext<TCommand, TCommandResult>, CancellationToken = default)` | The error handler runs on a recoverable fault. |
| `ICommandCompletionHandler` / `ICommandCompletionHandler<TCommand>` / `ICommandCompletionHandler<TCommand, TCommandResult>` | `Task HandleCompletionAsync(MessageCompletionContext<...>, CancellationToken)` | The completion handler runs on every path. |
| `ICommandRefusalMapper<in TCommand, out TCommandResult>` | `TCommandResult Map(TCommand, Refusal)` | The mapper turns a denial or validation failure into a return value. |

A typical command, handler, and call look like this:

```csharp
[Audited("orders.place-order", Category = "money", TargetKind = "order")]
public sealed record PlaceOrderCommand(Guid CartId) : ICommand<OrderId>;

public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, OrderId>
{
    private readonly IOrderRepository _orders;
    private readonly IAuditScope _audit;

    public PlaceOrderCommandHandler(IOrderRepository orders, IAuditScope audit)
    {
        _orders = orders;
        _audit = audit;
    }

    public async Task<OrderId> HandleAsync(PlaceOrderCommand message, CancellationToken cancellationToken = default)
    {
        var order = await _orders.PlaceAsync(message.CartId, cancellationToken);
        _audit.WithTarget(order.Id.ToString());
        return order.Id;
    }
}

// Caller
var orderId = await commandMediator.SendAsync(new PlaceOrderCommand(cartId), cancellationToken);
```

This example routes with tags, items, and a handler predicate:

```csharp
var settings = new CommandMediationSettings
{
    Routing = new CommandRoutingSettings
    {
        Tags = ["batch", "migration"],
        HandlerPredicate = descriptor => descriptor.HandlerType.Namespace!.StartsWith("Acme.Batch")
    }
};
settings.Items["skip-notifications"] = true;

await commandMediator.SendAsync(new PlaceOrderCommand(cartId), settings, cancellationToken);
```

The mediator reports these conditions as follows:

| Condition | Result |
| --- | --- |
| No main handler after filtering | `NoHandlerFoundException` (the mediator skips error handlers) |
| More than one main handler | `MultipleHandlerFoundException` with `HandlerTypes` populated |
| Guard denies and no refusal mapper produces the result type | `LiteBusMessageDeniedException` (`MessageType`, `Reason`, `Code`, `DeniedBy`) |
| Validator reports failures and no refusal mapper applies | `LiteBusMessageInvalidException` (`MessageType`, `Failures`, `RejectedBy`) |
| Untyped shortcut answers a command that produces a result | The mediator throws `PipelineContractException` naming `IMessageShortcut<TMessage, TMessageResult>` (analyzer `LB1019` catches this at compile time) |
| Shortcut answers with a result of the wrong type | The mediator throws `PipelineContractException` naming both types |
| Two refusal mappers at the same level produce the same result type | `PipelineContractException` |
| `null` command argument | `ArgumentNullException` |
| Registering a non-command type through `CommandModuleBuilder.Register` | `LiteBusNotSupportedException` |
| Descriptor resolution finds two equally specific base types | `AmbiguousMessageResolveException` |

The resolver prefers direct handler registrations over indirect ones. `SingleMainHandlerResolver` first looks at `MessageDependencies.MainHandlers` (handlers registered for the exact message type) and falls back to `IndirectMainHandlers` (base-type or interface registrations) only when the direct list is empty.

When the non-generic `SendAsync(ICommand)` or `TrySendAsync(ICommand)` receives a command whose runtime type declares exactly one `ICommand<TCommandResult>` result type, the mediator routes it through the generic overload. Post-handlers, error handlers, and completion handlers then observe the result. The non-generic `SendAsync` discards the value, and the non-generic `TrySendAsync` maps the typed result to a value-less `MediationResult` (`Denied`, `Invalid`, and `Answered` keep reason, code and failures, and anything else becomes `MediationResult.Succeeded()`). A command declaring zero or several `ICommand<>` contracts takes the no-result `SingleAsyncHandlerMediationStrategy<ICommand>` path, because no single result type exists to close the generic call over.

The mediator builds the delegate for each runtime type once and caches it.

`NoHandlerFoundException` and `MultipleHandlerFoundException` name the runtime message type, not the compile-time type, which the call erases to `ICommand`, `IQuery`, or `IEvent`.

For the unit of work, `CommandModuleBuilder.UseUnitOfWork<TUnitOfWork>()` (`TUnitOfWork : class, IUnitOfWork`) commits the application unit of work once per mediation that ended `Succeeded`, from a completion handler at `HandlerPriorities.UnitOfWork`. A denied, invalid, shortcut-answered, faulted, or cancelled mediation commits nothing, and a shortcut answer does not commit even though `MediationResult.IsSuccess` reports it as a success.

The application supplies the commit through `IUnitOfWork.CommitAsync(CancellationToken)`. LiteBus never opens a transaction. The completion stage is not cancellable, so the token is `CancellationToken.None`. A throwing commit propagates to the caller after a success.

Query mediations commit through the same `UseUnitOfWork` hook on `QueryModuleBuilder`, and when the two name different types, each axis commits its own implementation.

#### Recurring commands - `LiteBus.Commands.Recurring`

```csharp
registry.AddCommands(commands => commands
    .RegisterFromAssembly(typeof(SendDueRemindersCommand).Assembly)
    .AddRecurring(
        () => new SendDueRemindersCommand { BatchSize = 50 },
        host =>
        {
            host.Interval = TimeSpan.FromMinutes(5);
            host.StartupDelay = TimeSpan.FromSeconds(30);
        }));
```

This is a poll loop, not a scheduler. It has no cron expression, no wall-clock alignment, no missed-run catch-up, and no notion of a run that was due while the host was down. By default the loop runs concurrently on every host instance. Concurrent running suits a command that drains work by leasing rows, where the store arbitrates between instances. It does not suit a command that assumes it is the only one running.

Each registration gets its own loop and its own options. `AddRecurring` registers `TCommand` with the parent command builder, so calling only this method is enough for the loop to send. A `TCommand` implementing more than one `ICommand<>` contract fails composition with `LiteBusConfigurationException`, because a recurring command must declare at most one result type.

`RecurringCommandDeclaration<TCommand>` (`where TCommand : notnull`) is the registered intent. Its constructor `(Func<TCommand> commandFactory, RecurringCommandHostOptions hostOptions)` throws `ArgumentNullException` when either argument is null. It exposes get-only `Func<TCommand> CommandFactory`, called once per pass so a timestamp or fresh identifier is new each time, and get-only `RecurringCommandHostOptions HostOptions`. It is registered as a singleton so each loop resolves its own factory and options.

`RecurringCommandModule<TCommand>` (`IModule`, `IRequires<CommandModule>`, constructor `(RecurringCommandDeclaration<TCommand> declaration)`) is composed as a child of `CommandModule` inside `AddCommands`, so the loop cannot be registered without the axis that mediates it. Its `Build` validates the host options, registers the declaration as a singleton instance and the background service as its own transient type, and calls `RegisterBackgroundService`.

`RecurringCommandBackgroundService<TCommand>` (`IBackgroundService`) takes `(RecurringCommandDeclaration<TCommand> declaration, IMessageDispatchScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<RecurringCommandBackgroundService<TCommand>>? logger = null)`. When any of the first three arguments is null, the constructor throws `ArgumentNullException`. A null logger becomes `NullLogger.Instance`. All delays use the injected `TimeProvider`.

`ExecuteAsync` validates the options. It snapshots `Interval`, `ErrorBackoff`, and `IsConcurrencyConflict` so post-start option mutation cannot unthrottle the loop. When `Enabled` is false, it returns after logging. It resolves `ICommandMediator` once from a scope, throwing `LiteBusDependencyResolutionException` naming `ICommandMediator` when missing. It then waits `StartupDelay` and loops.

A pass reporting more work resends immediately. When shutdown raises `OperationCanceledException`, the loop returns. When an exception is conflict-classified, the loop logs at Information and waits `Interval`. For any other exception, the loop logs at Error and waits `ErrorBackoff`. An exception never escapes the loop.

Each pass builds a fresh command from the factory and opens its own dispatch scope through the mediator. The pass tests the result-bearing `ICommand<>` contract before the plain `ICommand` cast, because a result would otherwise be thrown away. A `TCommand` implementing neither throws `InvalidOperationException` naming the type.

A result implementing `IRecurringWorkReport` (`bool HasMoreWork { get; }`) tells the loop to send again immediately instead of waiting out `Interval`. Sending again immediately clears a backlog in one busy period rather than one batch per interval. Commands with no result, or with a result not implementing the report, wait the full interval. When the batch comes back full, the usual implementation reports true.

For single-flight across replicas, the loop takes a named lease before each pass when `RecurringCommandHostOptions.RequireLease` (default false) is set, through `IRecurringCommandLease.TryAcquireAsync(leaseName, ct)`. The call returns an `IRecurringCommandLeaseHandle`, or null when another instance holds the name. `LeaseName` (default null, meaning the command type full name) names the lease, and loops sharing a name single-flight against each other.

The loop skips a pass that cannot acquire the lease at Debug (EventId 2105 `LeaseSkipped`), and waits `Interval`. One lease covers the whole busy period. Immediate re-sends reuse the acquired lease, which the loop releases before waiting and on shutdown unwind. The loop treats a release failure as a pass failure (logged, then `ErrorBackoff`), and the failure never escapes the loop. The loop logs a shutdown-unwind release failure rather than faulting shutdown.

When the lease is lost mid-pass, the handle's `LeaseLost` token fires. The loop logs at Warning (EventId 2106 `LeaseLost`), ends the busy period, releases the handle, and waits `ErrorBackoff`, while the in-flight send runs under a token linked to the loss signal. Composition throws `LiteBusConfigurationException` when `RequireLease` is set and no lease is registered, and the loop throws `LiteBusDependencyResolutionException` at startup in the same case.

`LiteBus.Commands.Recurring.Storage.PostgreSql` ships `PostgreSqlRecurringCommandLease` over a renewable lease row in `litebus_recurring_leases`. The application registers it with `commands.UsePostgreSqlRecurringLease(...)` and configures it with `PostgreSqlRecurringLeaseOptions` (`LeaseDuration` is 30 seconds by default, `RenewInterval` is one third of it by default, both measured on the database clock, the lease renews the holder in the background while the pass runs, and the lease holds no connection between statements so it works behind a transaction-mode PgBouncer).

Host startup creates the schema, versioned as component `recurring`, version 1, with the `recurring.postgresql.schema` probe. Renewal logs EventId 2110 once when the lease is stolen and 2111 when renewal fails. The application registers a custom lease through a child module on `CommandModuleBuilder` so composition can see it.

### 4.2 Queries and stream queries

The primary types are `IQuery`, `IQuery<TQueryResult>`, `IStreamQuery<out TQueryResult>`, `IQueryMediator`, `QueryMediator`, and `QueryMediatorExtensions`.

```csharp
public interface IQueryMediator
{
    Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                QueryMediationSettings? queryMediationSettings = null,
                                                CancellationToken cancellationToken = default);

    IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                             QueryMediationSettings? queryMediationSettings = null,
                                                             CancellationToken cancellationToken = default);

    Task<MediationResult<TQueryResult>> TryQueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                                    QueryMediationSettings? queryMediationSettings = null,
                                                                    CancellationToken cancellationToken = default);

    Task<MediationDecision> EvaluateAsync(IQuery query,
                                          QueryMediationSettings? queryMediationSettings = null,
                                          CancellationToken cancellationToken = default);
}
```

The extensions add `QueryAsync(query, ct)`, `QueryAsync(query, tag, ct)`, `StreamAsync(query, ct)`, and `StreamAsync(query, tag, ct)`.

`TryQueryAsync` and `EvaluateAsync` behave exactly as their command counterparts in 4.1. A read that a caller is not permitted to make comes back as a value rather than an exception, and an evaluation runs guards and validators only. The mediator offers no `TryStreamAsync`, because the caller observes a stream's refusal when enumeration starts rather than when the method returns, so the mediator would have to produce a result value before the decision exists.

Queries use these handler contracts: `IQueryHandler<in TQuery, TQueryResult>` (`Task<TQueryResult> HandleAsync`), `IStreamQueryHandler<in TQuery, out TQueryResult>` (`IAsyncEnumerable<TQueryResult> StreamAsync`), plus `IQueryGuard<>`, `IQueryValidator<>`, `IQueryShortcut<,>`, `IStreamQueryShortcut<,>`, `IQueryRefusalMapper<,>`, `IStreamQueryRefusalMapper<,>`, `IQueryPreHandler`(`<>`), `IQueryPostHandler`(`<>`, `<,>`), `IStreamQueryPostHandler<,>`, `IQueryErrorHandler`(`<>`, `<,>`), `IQueryCompletionHandler`(`<>`, `<,>`).

```csharp
public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderView?>;

public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderView?>
{
    public Task<OrderView?> HandleAsync(GetOrderQuery message, CancellationToken cancellationToken = default)
        => _reads.FindAsync(message.OrderId, cancellationToken);
}

// Streaming
public sealed record ExportOrdersQuery(DateOnly From) : IStreamQuery<OrderRow>;

public sealed class ExportOrdersQueryHandler : IStreamQueryHandler<ExportOrdersQuery, OrderRow>
{
    public async IAsyncEnumerable<OrderRow> StreamAsync(
        ExportOrdersQuery message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var row in _reads.StreamAsync(message.From, cancellationToken))
        {
            yield return row;
        }
    }
}

await foreach (var row in queryMediator.StreamAsync(new ExportOrdersQuery(from), cancellationToken))
{
    await writer.WriteAsync(row, cancellationToken);
}
```

For streaming, the returned enumerable owns the per-message dispatch scope. Enumerating it twice throws `InvalidOperationException` ("A mediated asynchronous stream can be enumerated only once because it owns one dispatch scope."). Enumeration disposes the scope when it completes or faults, or when the caller disposes the enumerator.

Enumeration links the token passed to `StreamAsync` with the token supplied through `WithCancellation` or `GetAsyncEnumerator`. The pre-stages, the handler, the item loop, and the post-handlers all run under the linked token. The mediator creates the dispatch scope only when a handler resolves a service, so a stream that is never enumerated holds no container scope. The mediator restores the caller's ambient execution context before it returns the stream.

`IStreamQueryPostHandler<TQuery, TQueryResult>` receives the whole `IAsyncEnumerable<TQueryResult>` rather than materialized items. A refusal resolved through a mapper arrives as that mapped stream, and the runner enumerates it like the handler's stream. The runner then offers a fault in it to the error stage, and the ambient scope wraps every advance. When a query handler injects a mediator or durable writer, analyzer `LB1003` warns, because query handlers are expected to be side-effect free.

### 4.3 Events

The primary types are `IEvent`, `IEventMediator`, `EventMediator`, `EventMediatorExtensions`, and `AsyncBroadcastMediationStrategy<TMessage>`.

```csharp
public interface IEventMediator
{
    Task PublishAsync(IEvent @event,
                      EventMediationSettings? eventMediationSettings = null,
                      CancellationToken cancellationToken = default);

    Task PublishAsync<TEvent>(TEvent @event,
                              EventMediationSettings? eventMediationSettings = null,
                              CancellationToken cancellationToken = default)
        where TEvent : notnull;   // POCO events do not need to implement IEvent
}
```

The extensions add `PublishAsync(@event, ct)`, `PublishAsync(@event, tag, ct)`, and `PublishAsync<TEvent>(@event, tag, ct)`.

The `AsyncBroadcastMediationStrategy<TMessage>` strategy broadcasts as follows:

1. The strategy runs the pre-stages once for the event. A refusal throws (`LiteBusMessageDeniedException` / `LiteBusMessageInvalidException`). An `Answered` shortcut returns without running handlers.
2. The strategy concatenates direct and indirect main handlers, orders them by `Priority` then `RegistrationSequence`, and groups them by `Priority`.
3. The strategy runs the priority groups per `PriorityGroupsConcurrencyMode`, and the handlers inside a group per `HandlersWithinSamePriorityConcurrencyMode`.
4. Sequential and parallel handler groups both obey `FaultMode`. `ContinueAndAggregate` runs every handler in the group and aggregates the failures, and `PropagateFirst` stops the group. Neither mode carries across priority groups. Cancellation is not a handler fault. Sequential execution stops the group immediately on cancellation under either mode. Parallel execution under `ContinueAndAggregate` rethrows one `OperationCanceledException` when every handler is cancelled, so the mediation reports `Canceled`, while mixed cancellation and faults still aggregate.
5. The strategy runs post-handlers with `null` as the "result" argument, then error handlers with `null` on a recoverable fault, then completion handlers with `messageResult: null`. An event produces no result, so none of these stages sees one.
6. Each handler invocation opens its own `AmbientExecutionContext` scope so the ambient context is correct even under parallel execution.

```csharp
public sealed record OrderPlaced(Guid OrderId, decimal Total) : IEvent;

[HandlerPriority(10)]
public sealed class SendConfirmationEmail : IEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced message, CancellationToken cancellationToken = default) => ...;
}

[HandlerPriority(20)]
public sealed class UpdateReadModel : IEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced message, CancellationToken cancellationToken = default) => ...;
}

// Fan out both priority groups at once and collect every failure.
await eventMediator.PublishAsync(new OrderPlaced(id, total), new EventMediationSettings
{
    ThrowIfNoHandlerFound = true,
    Execution = new EventExecutionSettings
    {
        PriorityGroupsConcurrencyMode = ConcurrencyMode.Parallel,
        HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel,
        FaultMode = HandlerFaultMode.ContinueAndAggregate
    }
}, cancellationToken);
```

With `ThrowIfNoHandlerFound = false` (the default), an event with no handlers completes silently. When `AutoRegisterUnregisteredMessageTypes = true`, the mediator adds an event type that was never registered to the registry during the publish. Without it, the mediator throws `NoHandlerFoundException` for an unknown type.

`IEventHandler<in TEvent>` only constrains `TEvent : notnull`, so plain POCO events work. The guard, validator, shortcut, pre-handler, post-handler, error-handler, and completion-handler roles constrain `TEvent : notnull` too, so an unmarked event passes through every pipeline stage like a marked one.

One class can handle several events because `IMessageHandler` is a memberless marker. One class may implement several closed handler contracts such as `IEventHandler<OrderRefundSucceeded>` and `IEventHandler<OrderRefundSettled>`. Each registration carries its own closed contract on its descriptor, and the mediator calls each through the contract it names with the mediation cancellation token.

A handler whose descriptor message type equals the strategy message type takes the typed fast path. The mediator dispatches any other handler through its descriptor contract (open contracts closed over the runtime type).

### 4.4 Pipeline stage semantics

#### Main handlers - `IMessageHandler`

Non-generic `IMessageHandler` is a memberless marker, the base of every closed main handler contract. `IMessageHandler<in TMessage, out TMessageResult> : IMessageHandler` declares `TMessageResult Handle(TMessage message)`. `IAsyncMessageHandler<in TMessage> : IMessageHandler<TMessage, Task>` declares `Task HandleAsync(TMessage message, CancellationToken cancellationToken = default)`. `IAsyncMessageHandler<in TMessage, TMessageResult> : IMessageHandler<TMessage, Task<TMessageResult>>` declares `Task<TMessageResult> HandleAsync(...)`. `IStreamMessageHandler<in TMessage, out TMessageResult> : IMessageHandler<TMessage, IAsyncEnumerable<TMessageResult>>` (`where TMessage : notnull`) declares `IAsyncEnumerable<TMessageResult> StreamAsync(TMessage message, CancellationToken cancellationToken = default)`. Axis handler contracts (`ICommandHandler<>`, `IQueryHandler<,>`, `IEventHandler<>` and the rest) derive from these.

The mediator invokes each main handler through the closed contract recorded on its descriptor with the mediation cancellation token. A handler whose descriptor message type equals the strategy message type takes the typed fast path. The mediator dispatches any other handler through its descriptor contract with open contracts closed over the runtime type. The mediator therefore calls each registration of one class implementing several closed handler contracts through the contract the registration names.

#### Guards - `IMessageGuard<in TMessage>` (`PreStage.Guard`)

```csharp
public sealed class RequireSecondApproverGuard : ICommandGuard<ProcessPaymentCommand>
{
    private const decimal ApprovalThreshold = 10_000m;

    public Task<Verdict> DecideAsync(ProcessPaymentCommand message, CancellationToken cancellationToken = default)
        => Task.FromResult(message.Amount > ApprovalThreshold
            ? Verdict.Deny($"payments above {ApprovalThreshold:N0} need a second approver", code: "SECOND_APPROVER")
            : Verdict.Allow);
}
```

`Verdict` is a readonly struct. Its members are `Verdict.Allow` (the `default`), `Verdict.Deny(string reason, string? code = null)` (reason must be non-blank or `ArgumentException`), `IsDenied`, `Reason`, `Code`, value equality, and `==`/`!=`. A denial stops at the first guard, reports `MediationOutcome.Denied`, does **not** reach error handlers, and reaches completion handlers. The stopping decision records the denying guard on `PipelineDecision.DecidedBy`. Without a refusal mapper, the decision reaches the caller as `LiteBusMessageDeniedException.DeniedBy` and in the exception message.

`DeclarationGuard<TMessage, TValue> : IMessageGuard<TMessage>` (`TValue : class`) is the abstract base for a guard enforcing one declared metadata value. Its constructor takes an `IMessageMetadataAccessor`. Its public `DecideAsync(message, ct)` reads the runtime message type, so an open generic guard closed over a base type enforces the concrete message declaration. It passes a declared value to the abstract `DecideAsync(declaration, message, ct)`, allows a recorded exemption without calling it, and otherwise calls the virtual `DecideUndeclaredAsync`. Unless overridden, it denies with code `UndeclaredCode` (`declaration.missing`) and reason `The message '{Message}' declares no '{ValueType}'.`.

`AuthorizationGuard<TMessage> : DeclarationGuard<TMessage, AuthorizationDeclaration>` seals the decision. The guard allows an exempt message and delegates an authorized one to the abstract `AuthorizeAsync(policy, message, ct)`. LiteBus carries the policy and never evaluates it.

#### Validators - `IMessageValidator<in TMessage>` (`PreStage.Validator`)

```csharp
public sealed class ProcessPaymentCommandValidator : ICommandValidator<ProcessPaymentCommand>
{
    public Task<Validity> ValidateAsync(ProcessPaymentCommand command, CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();

        if (command.Amount <= 0)
        {
            failures.Add(new ValidationFailure("the payment amount must be greater than zero",
                nameof(command.Amount), "AMOUNT_NOT_POSITIVE"));
        }

        if (command.PaymentId == Guid.Empty)
        {
            failures.Add(new ValidationFailure("the payment identifier must be supplied",
                nameof(command.PaymentId), "PAYMENT_ID_MISSING"));
        }

        return Task.FromResult(Validity.Invalid(failures));   // returns Valid when the list is empty
    }
}
```

`Validity` is a readonly struct. Its members are `Validity.Valid` (the `default`), `Validity.Invalid(string message, string? member = null, string? code = null)`, `Validity.Invalid(IEnumerable<ValidationFailure>)`, and `Validity.Invalid(params ValidationFailure[])`. The collection overloads return `Valid` for an empty input. Other members are `IsInvalid` and `Failures` (never null).

`ValidationFailure` is a readonly struct `(string Message, string? Member = null, string? Code = null)`. `Message` must be non-blank. `ToString()` renders `member: message`, or `message` alone.

The validator stage aggregates because it is the only stage with `StageAggregation.CollectFailures`. Every validator runs (indirect first, then direct), and all reported failures merge into one `PipelineDecision.Invalid`. The decision's `Reason` is the failures joined with `"; "`. When exactly one failure was reported, `Code` is the single failure's code, and otherwise it is `null`. The decision records the first validator that reported a failure on `PipelineDecision.DecidedBy`. Without a refusal mapper, the decision reaches the caller as `LiteBusMessageInvalidException.RejectedBy` and in the exception message.

For migration, `ThrowingValidator<TMessage, TException>` (abstract, `LiteBus.Messaging.Abstractions`) adapts a v6 validator body that reports failure by throwing. It has two abstract members: `Task ValidateOrThrowAsync(TMessage, CancellationToken)` holding the old body unchanged, and `Validity Describe(TException)` translating the exception. The adapter catches only `TException`. Anything else propagates as a failure, so the stage does not report a genuine fault in a validator as a verdict. Axis specializations `ThrowingCommandValidator<TCommand, TException>` and `ThrowingQueryValidator<TQuery, TException>` exist because a validator implementing only the messaging contract is not a command or query construct and the axis module refuses to register it.

Adapted and converted validators mix in one mediation, because the stage collects across both. Collecting across both lets a file-by-file migration behave correctly before migration converts the last file. The adapter cannot recover the collected-failures behavior of a real conversion, because a throwing body stops at the first problem. It serves migration only, not permanent use.

#### Shortcuts - `IMessageShortcut<in TMessage>` and `IMessageShortcut<in TMessage, TMessageResult>` (`PreStage.Shortcut`)

```csharp
public sealed class SkipAppliedPaymentShortcut : ICommandShortcut<ProcessPaymentCommand>
{
    private static readonly ConcurrentDictionary<Guid, bool> Applied = new();

    public Task<Shortcut> TryAnswerAsync(ProcessPaymentCommand message, CancellationToken cancellationToken = default)
        => Task.FromResult(Applied.TryAdd(message.PaymentId, true)
            ? Shortcut.None
            : Shortcut.Answer("the payment was already applied"));
}

public sealed class CachedTotalShortcut : IQueryShortcut<GetCartTotalQuery, Money>
{
    public Task<Shortcut<Money>> TryAnswerAsync(GetCartTotalQuery q, CancellationToken ct = default)
        => _cache.TryGet(q.CartId, out var total)
            ? Task.FromResult(Shortcut<Money>.Answer(total, "served from cache"))
            : Task.FromResult(Shortcut<Money>.None);
}
```

`Shortcut` offers `None` (the `default`), `Answer(string? reason = null, string? code = null)`, and `Deny(string reason, string? code = null)` (reason must be non-blank or `ArgumentException`), with `IsAnswered`, `IsDenied`, `Reason`, and `Code`. `Shortcut<TMessageResult>` offers `None`, `Answer(TMessageResult result, string? reason = null, string? code = null)`, and `Deny(string reason, string? code = null)` carrying no result, with `IsAnswered`, `IsDenied`, `Result`, `Reason`, and `Code`.

`Code` means the same thing on all three decision shapes and on `Verdict`. It names something a later stage can switch on, while `Reason` is prose written for a person. It reaches `MessageCompletionContext.Code`, the audit record, and the mediation telemetry tag. Both are readonly structs with value equality covering `IsDenied`.

The first answering shortcut stops the stage and reports `MediationOutcome.Answered`. For the untyped contract on a result-producing message, it raises `PipelineContractException` because the untyped answer cannot carry a value.

A denying shortcut ends the mediation exactly as a guard denial does. `TrySendAsync` returns `Denied`, and `SendAsync` throws `LiteBusMessageDeniedException` naming the shortcut in `DeniedBy`. Completion handlers and the audit record observe the denial, error handlers do not run, and refusal mappers apply. Reserve denying for a refusal that can only be decided at the shortcut stage, such as an idempotency claim. Anything a guard could decide belongs to a guard.

#### Pipeline decisions - `PipelineDecision`

`PipelineDecision` is the readonly struct the stage runner converts `Verdict`, `Validity`, and `Shortcut` into, so every strategy acts on one shape. Its members are `Continue` (the default), `StopsPipeline`, `Outcome` (`Denied`, `Invalid`, or `Answered`), `HasResult`, `Result` (`object?`), `Reason`, `Code`, `Failures` (never null), `DecidedBy` (`Type?`, set for a guard denial, a shortcut denial, and a validation failure as well as a shortcut answer), `IsRefusal` (`Denied` or `Invalid`, from a guard, a shortcut denial, or a validator), `ToRefusal()`, `CreateRefusalException(Type)`, and `ResolveResult<TMessageResult>(Type)`. When the decision carries no result or the result type mismatches, it throws `PipelineContractException`. The `Denied`/`Invalid`/`Answered` factories are internal.

Only a shortcut answer carries a result, because a refusal does not owe the caller the value the main handler would have produced. When one is registered, the value a refused caller receives comes from a refusal mapper. Otherwise, the refusal reaches the caller as an exception.

#### Pre-handlers - `IMessagePreHandler<in TMessage>` (`PreStage.PreHandler`)

`IMessagePreStageHandler` is the memberless marker base of `IMessageGuard<TMessage>`, `IMessageValidator<TMessage>`, both `IMessageShortcut` arities, and `IMessagePreHandler<TMessage>`. The pipeline invokes a pre-stage handler through the closed contract recorded in its descriptor, not through a member on the marker, so a class implementing pipeline contracts for several message types still compiles. `IMessagePreHandler<in TMessage> : IMessagePreStageHandler` declares `Task PreHandleAsync(TMessage message, CancellationToken cancellationToken = default)`.

A pre-handler cannot stop the pipeline by returning. Throwing is a fault. Use pre-handlers for enrichment and cross-cutting preparation.

#### Post-handlers - `IMessagePostHandler<in TMessage, in TMessageResult>`

The contract declares `Task PostHandleAsync(TMessage message, TMessageResult? messageResult, CancellationToken cancellationToken = default)`. Direct post-handlers run before indirect ones. `IExecutionContext.SuppressPostHandlers()` stops any post-handler that has not run yet without changing the outcome (the mediation still reports `Succeeded`). A post-handler may overwrite `IExecutionContext.MessageResult` to change what the caller receives.

#### Error handlers - `IMessageErrorHandler<TMessage, TMessageResult>`

```csharp
public sealed class TranslateConflicts : ICommandErrorHandler<PlaceOrderCommand, OrderId>
{
    public Task HandleErrorAsync(MessageErrorContext<PlaceOrderCommand, OrderId> context,
                                 CancellationToken cancellationToken = default)
    {
        if (context.Exception is DbUpdateConcurrencyException)
        {
            context.HandledResult = OrderId.Empty;
            context.Outcome = MessageErrorOutcome.Handled;   // suppresses the original exception
        }

        return Task.CompletedTask;
    }
}
```

`MessageErrorContext` has these members: `Message` (required), `Exception` (required), `MessageResult`, `Outcome` (`MessageErrorOutcome`, default `Unhandled`), `HandledResult`, and `AsTyped<TMessage, TMessageResult>()`. Indirect error handlers run first, then direct ones. All of them see the same mutable context. If `Outcome` is still `Unhandled` after every handler, the pipeline rethrows the captured exception with its original stack trace via `ExceptionDispatchInfo`.

When there are **no** error handlers at all, the pipeline rethrows the exception immediately. The non-generic `IMessageErrorHandler` declares `Task HandleErrorAsync(MessageErrorContext context, CancellationToken cancellationToken)` with no default value on the token. `IMessageErrorHandler<TMessage, TMessageResult>` supplies it through a default implementation that adapts to the typed context. `IMessageErrorHandler<TMessage>` is `IMessageErrorHandler<TMessage, object>` for a message producing no result.

A fault the registered `IFaultRefusalClassifier` entries recognize never reaches error handlers. The pipeline classifies it first through `MediationExceptionFilters.TryClassifyRefusal`, and the fault ends the mediation as that refusal instead of a fault. Faults raised by error handlers themselves propagate unclassified, as do configuration errors, which callers never offer.

#### Completion handlers - `IMessageCompletionHandler<TMessage>` and `<TMessage, TMessageResult>`

```csharp
public sealed class RecordMediationMetrics : ICommandCompletionHandler
{
    public Task HandleCompletionAsync(MessageCompletionContext<ICommand> context, CancellationToken cancellationToken)
    {
        Metrics.Record(context.Message.GetType().Name, context.Outcome, context.Duration);
        return Task.CompletedTask;
    }
}
```

`MessageCompletionContext` has these members: `Message` (required), `Outcome` (`MediationOutcome`, required), `MessageResult`, `Exception` (the recorded fault, or null when error handlers recovered it), `Reason`, `Code`, `Duration`, `Faulted` (`Outcome is Failed or Canceled`), `AsTyped<TMessage>()`, and `AsTyped<TMessage, TMessageResult>()`.

The two-type view `MessageCompletionContext<TMessage, TMessageResult>` adds `HasResult` (whether `MessageResult is TMessageResult`) and a typed `MessageResult` (`TMessageResult?`, default when no value of the expected type was produced). The single-arg view types only `Message`. Its `MessageResult` stays `object?`, and it has no `HasResult`. Both typed views add `AsUntyped()`.

The pipeline always invokes completion handlers with `CancellationToken.None`. When a completion handler throws while `context.Exception` is already set, the pipeline appends its exception to the list stored at `exception.Data[MediationExceptionData.SuppressedCompletionFaults]` (key string `"LiteBus.SuppressedCompletionFaults"`). The original fault still propagates.

A non-recoverable fault (missing or ambiguous handler, idempotency misconfiguration, application-thrown refusal) completes as `Failed` with the exception rather than `Succeeded`. When error handlers recovered the fault, the completion context carries `Failed` with a null `Exception`, so completion handlers can tell a recovered fault from a propagated one. A completion handler that throws on that recovered path propagates instead of being suppressed.

The shipped idempotency shortcut records each key it is granted as a `ClaimedIdempotencyKey` in `IExecutionContext.Data`. The idempotency completion handler settles only that recorded claim. It completes the key on `Succeeded` and releases it on any other outcome, including `Answered` when a later shortcut answered after this mediation claimed. It passes the recorded claim token, so a settle that arrives after a takeover is a no-op.

A mediation that was denied, failed validation, was answered from the store, or lost a claim conflict ends holding no record. Settling by the recorded claim therefore never releases another delivery's in-flight key.

#### Refusal mappers - `IMessageRefusalMapper<in TMessage, out TMessageResult>`

```csharp
public sealed class PaymentRefusalMapper : ICommandRefusalMapper<ProcessPaymentCommand, PaymentOutcome>
{
    public PaymentOutcome Map(ProcessPaymentCommand message, Refusal refusal)
        => refusal.IsDenied
            ? PaymentOutcome.Rejected(refusal.Reason, refusal.Code)
            : PaymentOutcome.Invalid(refusal.Reason);
}
```

`Refusal` is a readonly struct. Its members are `Refusal.Denied(reason, code = null)`, `Refusal.Invalid(reason, code = null)`, `Outcome` (`Denied` or `Invalid`), `Reason`, `Code`, `IsDenied`, `CreateException(messageType, cause = null)` mapping the outcome to the denial or invalid exception with the cause as inner, and `ToString()`.

Selection picks the single mapper whose `MessageResultType` is assignable to the caller's expected result type, preferring direct registrations over indirect ones. When two candidates stand at the same level, the pipeline throws `PipelineContractException`. With no mapper, the pipeline throws `LiteBusMessageDeniedException` or `LiteBusMessageInvalidException`. For a void command, a refusal always throws (there is no value to map).

To turn faults into refusals, `IFaultRefusalClassifier.TryClassify(exception, out refusal)` recognizes a fault a stage raised and names the refusal the mediation ends as instead, including a commit fault raised in the completion stage. `PostgreSqlConstraintRefusalClassifier` maps unique (23505) and exclusion (23P01) violations on configured constraints (ordinal comparison, found anywhere in the inner chain) to `Refusal.Denied` with the mapped code. The application constructs it from an `IReadOnlyDictionary<string, string>` or its nested `Options` record.

The application registers classifiers with `MessageModuleBuilder.UseFaultRefusalClassifier<T>()` or `UseFaultRefusalClassifierInstance`. Classifiers are Singleton collection items. The pipeline consults them in registration order with first match winning. They must be stateless pure predicates, and the pipeline never offers them a cancellation or an already-carried refusal.

A classified fault skips error handlers, runs completion with the refusal outcome, is audited as a denial or invalid outcome rather than a failure, and reaches a registered refusal mapper. A classified invalid refusal carries no validator failures on the `Try*` value and one synthesized member-less failure on the thrown exception. A classified completion fault supersedes the earlier ending for the caller, while handlers that already ran keep their observation.

`EvaluateAsync` maps a classified decision-stage fault to the denied or invalid decision the message would end as when sent.

### 4.5 Handler discovery, tags, priorities and open generics

`IMessageRegistry.Register(Type)` is the single entry point for discovery. For a given type, the registry runs these steps:

1. The registry detects an `IMessageDefinition` implementation and binds its declarations (see 4.7).
2. Otherwise, the registry runs the descriptor builders, five public ones (`HandlerDescriptorBuilder` for main handlers, `CompletionHandlerDescriptorBuilder`, `PostHandlerDescriptorBuilder`, `PreStageHandlerDescriptorBuilder`, `RefusalMapperDescriptorBuilder`, each exposing `bool CanBuild(Type type)` and `IEnumerable<IHandlerDescriptor> Build(Type type)`) plus the internal `ErrorHandlerDescriptorBuilder`, and collects the descriptors they produce.
3. If no descriptors were produced, the registry treats the type as a message type (unless its namespace is `System` or starts with `System.`, in which case the registry ignores it). When a type carries a pipeline marker interface but exposes no contract naming a message type, the registry throws `PipelineContractException`.
4. If the type is an open generic type definition whose descriptors have a generic-parameter message type, the registry stores it as an open generic handler and closes it over every known and future concrete message type.

   The closable arities are **1** (binds the message type) and **2** (binds the message type and the result type the message declares). The registry accepts arity 2 only when the handler implements a contract taking both of its parameters in order, one of `IMessageHandler<,>`, `IStreamMessageHandler<,>`, `IMessagePostHandler<,>`, `IMessageCompletionHandler<,>`, `IMessageErrorHandler<,>`, `IMessageShortcut<,>`, or `IMessageRefusalMapper<,>` (or an axis contract deriving from one). For anything else, the registry throws `UnsupportedOpenGenericHandlerException` (analyzer `LB1005`).

   The registry reads the result type from the message's own `IProducesResult<TMessageResult>` contract, which `ICommand<T>`, `IQuery<T>`, and `IStreamQuery<T>` all derive from, so the binding does not depend on a main handler being registered yet. For a message declaring no result, or declaring two, the registry skips it for an arity-2 handler rather than failing, because a generic handler covers the messages it fits.
5. Handler descriptors get a monotonically increasing `RegistrationSequence`. The registry then cross-links handlers and messages and commits them.

The registry normalizes message types. It reduces a generic type that still contains generic parameters to its generic type definition. Closed generic messages keep their exact type. Both `Register` and `RegisterFromScan` on `IMessageWriter` carry `[RequiresUnreferencedCode]` and `[DynamicallyAccessedMembers(PublicConstructors | PublicMethods | Interfaces)]` on the `Type` parameter, because registration inspects CLR types through reflection.

`ActualTypeOrFirstAssignableTypeMessageResolveStrategy.Find` tries an exact match first. Otherwise it selects the assignable descriptor with the greatest inheritance depth (base-chain length plus the registered type's interface count; generic definitions contribute chain length only). When depths tie, it throws `AmbiguousMessageResolveException` carrying the tied candidates. With no assignable descriptor, it returns null.

`IMessageWriter.RegisterFromScan(Type)` behaves exactly like `Register(Type)` and additionally records that nothing in the composition code named the type. Every `RegisterFromAssembly` uses it. The distinction matters only for an open generic pipeline handler, which is closed over every registered message it fits and so inserts a stage into every message in the application with no registration line for a reviewer to read:

* `IMessageReader.OpenGenericClosures` reports each open generic handler and the concrete message types the registry closed it over. A handler that fits nothing appears with an empty set. That matters because nothing else reports a handler that never runs.
* `IMessageReader.ScannedOpenGenericHandlers` reports the ones that arrived through a scan and were never named by a registration line. A handler that was both scanned and explicitly registered is absent, whether the scan or the registration came first.
* `MessageModuleBuilder.RequireExplicitOpenGenerics()` fails composition with `PipelineContractException` naming each one and the `Register(typeof(X<>))` line that resolves the violation.
* Whether strict mode is on or not, `LiteBusCompositionSummary` reports every open generic and its closure count.

Order-independence makes the check satisfiable. `RegisterFromAssembly` walks every type in the assembly the open generic lives in, so it reaches a handler the composition already named. A scan configured before the explicit line reaches it first.

If a scan could take the registration line away, the application would have to move the handler into an assembly nothing scans to satisfy strict mode. That would be a worse design imposed by a minor registration convenience. The exception's own advice to add a `Register(typeof(X<>))` line would then name a fix that does not work. The registry therefore records the two origins separately and subtracts them. Clearing the scanned mark does not cost the closure, because the handler still runs for every message it fits.

Strict mode is opt-in rather than the default, because an assembly scan has picked up open generic handlers since v4. Turning that off changes what a scan is rather than fixing a defect. The summary provides the visibility the behavior actually lacked.

When the handler's message type parameter is, or is constrained to be, assignable to that axis, `CommandModuleBuilder.Register`, and its query and event equivalents, accept a handler written against a messaging-level pipeline contract:

```csharp
// Registers on the command builder, and the same class constrained to IQuery registers on the query builder.
internal sealed class AuthorizationGuard<TMessage> : IMessageGuard<TMessage>
    where TMessage : ICommand
{ ... }
```

The pipeline reads membership from the constraint through `MessagingHandlerContracts.NamesMessageAssignableTo`, over the `PipelineContracts` table, so every axis recognises a new pipeline role at once. The builder still refuses a handler constrained to neither axis with `LiteBusNotSupportedException`. Nothing says which axis it is for, so accepting it would silently close it over every message in whichever axis happened to register it.

Axis acceptance excludes main handler contracts, because a command handler and a query handler mean different things. One class answering both is a modelling error rather than a cross-cutting concern.

Registering such a handler on the messaging module also works, but it is not equivalent. It closes over every registered message including events, with no way to select commands and queries but not events.

To explain a message's pipeline, `IMessageReader.Explain(Type)`, in `LiteBus.Messaging.Registry`, returns a `MessagePipelinePlan`. The plan holds the message, the result type the message declares, and every handler that will run, in run order, as `MessagePipelineStep` records carrying `Stage`, `Priority`, `HandlerType`, `ContractType`, `IsIndirect`, and `IsClosedOpenGeneric`. `ToString()` renders the block a log line or a test failure shows.

```csharp
var plan = provider.GetRequiredService<IMessageReader>().Explain(typeof(CloseOrganizationCommand));

// CloseOrganizationCommand -> OrganizationClosed
//   guard               0  AttributeActorGuard`1  (open generic)
//   guard            2000  AuthorizationGuard`1   (open generic)
//   validator           0  CloseOrganizationValidator
//   shortcut            0  IdempotentCommandShortcut`2  (open generic)
//   main                0  CloseOrganizationCommandHandler
//   completion    1000200  CommandAuditCompletionHandler
//   completion    2000100  CommitUnitOfWork
```

The plan follows the pipeline's own ordering rules. The pre stages, main, error, and refusal steps list indirect handlers before direct ones, while the post stage lists direct handlers before indirect ones. The plan orders each group by priority then registration sequence. Completion orders by priority then registration sequence across both groups with no direct/indirect split.

The main and refusal steps list every candidate, even though a command resolves one main handler and one mapper with the direct registration preferred. The plan applies no tags, so it describes what is registered rather than what one tag-filtered mediation runs. A message the registry does not hold produces a plan with no steps rather than a failure.

Descriptor metadata for `IHandlerDescriptor` comprises `MessageType`, `Priority`, `RegistrationSequence`, `Tags`, `HandlerType`, and `ContractType`.

Role-specific descriptors add `MessageResultType` (`IMainHandlerDescriptor`, `IPostHandlerDescriptor`, `IRefusalMapperDescriptor`, `ICompletionHandlerDescriptor`, nullable there), `Stage` (`IPreStageHandlerDescriptor`), and a `PipelineDispatch? Dispatch` (marked `EditorBrowsableState.Never`). `IErrorHandlerDescriptor` is a marker extending `IHandlerDescriptor` with no additional members.

`IMessageDescriptor` carries `MessageType`, `IsGeneric`, `Metadata`, and `IReadOnlyCollection<>` pairs for direct and indirect registrations: `Handlers`/`IndirectHandlers` (`IMainHandlerDescriptor`), `PostHandlers`/`IndirectPostHandlers`, `PreStageHandlers`/`IndirectPreStageHandlers`, `CompletionHandlers`/`IndirectCompletionHandlers`, `ErrorHandlers`/`IndirectErrorHandlers`, and `RefusalMappers`/`IndirectRefusalMappers`. `IMessageReader` also exposes `Handlers`, every handler descriptor in registration order, and `Count`, the number of committed message descriptors.

Tags select handlers, as this example shows:

```csharp
[HandlerTag("legacy")]
[HandlerTag("migration")]
public sealed class LegacyPlaceOrderHandler : ICommandHandler<PlaceOrderCommand> { ... }

[HandlerTags("batch", "nightly")]
public sealed class BatchPlaceOrderHandler : ICommandHandler<PlaceOrderCommand> { ... }

await commandMediator.SendAsync(command, "batch", cancellationToken);   // selects the batch handler
```

A descriptor participates when `descriptor.Tags.Count == 0` or `descriptor.Tags` intersects the mediation tags. Analyzer `LB1011` warns about a tag that no mediation call in the compilation references.

Handlers run in **ascending** priority order. A lower number runs earlier, a higher number runs later, and ties break on `RegistrationSequence`. `HandlerPriorities` reserves a window for framework handlers and names the application band on each side of it:

| Constant | Value | Meaning |
| --- | --- | --- |
| `HandlerPriorities.Default` | `0` | The registry assigns this value when no `[HandlerPriority]` is present. |
| `HandlerPriorities.ReservedFloor` | `1000000` | Lowest value reserved for LiteBus-shipped handlers. |
| `HandlerPriorities.Persistence` | `1000100` | LiteBus handlers that persist state. |
| `HandlerPriorities.Observability` | `1000200` | LiteBus handlers that observe and record, such as the audit writers. |
| `HandlerPriorities.ReservedCeiling` | `2000000` | First value above the reserved window. It is a pure boundary marker, and nothing sits on it. |
| `HandlerPriorities.UnitOfWork` | `2000100` | The application commits its unit of work here, after every LiteBus handler. |

Application handlers belong below `ReservedFloor` or at or above `ReservedCeiling`. LiteBus may reorder only `Persistence` and `Observability` between releases, and only relative to each other. The floor and ceiling are stable.

The band from `ReservedCeiling` up to `UnitOfWork` holds application infrastructure that has to run after every LiteBus handler and still before the commit, such as a handler that flushes a buffered projection. The two used to share the value `2000000`, which left no such band. A handler registered on the ceiling tied with the commit, and registration sequence resolved the order, which is assembly scan order.

An application that needs an audit record atomic with the change it describes registers an `ICommandCompletionHandler` at `HandlerPriorities.UnitOfWork`. It gates the commit on `context.Outcome` and stages the record from its `IAuditTrail` rather than writing it. The commit flushes both. A record for a non-success outcome cannot be written in the transaction being rolled back and has to be written separately.

An open generic handler and its registration look like this:

```csharp
public sealed class LogAnyCommand<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default) => ...;
}

// Register the open definition; LiteBus closes it per concrete command type.
liteBus.AddCommands(commands => commands.Register(typeof(LogAnyCommand<>)));
```

When two descriptors are closures of one generic handler definition over the same contract definition, the registry links only the direct one to a message. It skips an indirect closure when a direct one exists and removes it when a direct one arrives later, so an open generic registration runs once per message regardless of registration order.

A distinct handler type over a base type still runs indirectly before direct ones. `LiteBusCompositionSummary.DuplicateStageCoverages` reports a handler type that still covers one message stage twice (a class implementing both a base and a concrete contract).

For runtime dispatch, handlers registered under a closed contract carry a pre-bound `PipelineDispatch`. For handlers registered under an open contract, the pipeline binds a dispatch lazily and caches it in a `ConcurrentDictionary` keyed by the closed contract type (`PipelineHandlerInvoker.ResolveRuntimeDispatch`). When the pipeline cannot dispatch a contract, it throws `PipelineContractException`.

`PipelineDispatch.For(Type)` and `PipelineDispatch.StageFor(Type)` are public but `EditorBrowsable(Never)`. Only `For` carries `[RequiresUnreferencedCode]`, so trimming needs care with it. `For` returns null rather than throwing for a non-generic, open, or unknown contract. `StageFor` reads `Guard`, `Validator`, or `Shortcut` off the contract and returns the `PreStage.PreHandler` fallback for anything else.

### 4.6 Execution context

```csharp
public interface IExecutionContext
{
    CancellationToken CancellationToken { get; }
    DateTimeOffset HandledAt { get; }
    IDictionary<string, object> Items { get; }
    IHandleContextData Data { get; }
    IReadOnlyCollection<string> Tags { get; }
    object? MessageResult { get; set; }
    bool PostHandlersSuppressed { get; }
    void SuppressPostHandlers();
}
```

`HandledAt` is the instant the mediation started. The context reads the instant once from the messaging module `TimeProvider` at creation. Every stage of one mediation observes the same instant, and a nested mediation reads its own.

LiteBus registers `IExecutionContext` as a scoped dependency, so a handler declares it as a constructor parameter instead of using the ambient static:

```csharp
public sealed class CloseAccountCommandHandler : ICommandHandler<CloseAccountCommand>
{
    private readonly IExecutionContext _context;

    public CloseAccountCommandHandler(IExecutionContext context) => _context = context;
}
```

The mediator opens the ambient scope before it creates the dispatch scope. Each mediation has one dispatch scope, so the scoped resolution returns the context of the mediation in flight and the container's per-scope cache holds exactly one. The mediator restores the caller's ambient context before it returns a task or stream, so the ambient scope never leaks into the caller. The mediator creates the dispatch scope only when a handler resolves a service.

Resolving the context outside a mediation throws `NoExecutionContextException`, which is what `AmbientExecutionContext.Current` already does. A singleton must not take it as a dependency.

Access the context from anywhere outside dependency injection through `AmbientExecutionContext`:

| Member | Behaviour |
| --- | --- |
| `AmbientExecutionContext.Current` | When no context is set, Get throws `NoExecutionContextException`. Set replaces the ambient value (assigning `null` clears it). |
| `AmbientExecutionContext.HasCurrent` | It returns `bool` and never throws. |
| `AmbientExecutionContext.GetCurrentOrDefault()` | `IExecutionContext?`. |
| `AmbientExecutionContext.CreateScope(IExecutionContext)` | It returns `ExecutionContextScope` implementing both `IDisposable` and `IAsyncDisposable`. Disposal restores the previous context and is idempotent. |
| `AmbientExecutionContext.ResetForTesting()` | It clears the `AsyncLocal` value. Use it for test isolation only. |

These are the well-known `Items` keys:

| Key constant | String value | Written by |
| --- | --- | --- |
| `MessageTraceContextKeys.CorrelationId` | `__LiteBus.Trace.CorrelationId` | inbox/outbox dispatch (`MessageProcessorDiagnostics.ApplyTraceMetadata`) |
| `MessageTraceContextKeys.CausationId` | `__LiteBus.Trace.CausationId` | Same as above. |
| `MessageTraceContextKeys.TenantId` | `__LiteBus.Trace.TenantId` | Same as above. |
| `MessageTraceContextKeys.TraceContext` | `__LiteBus.Trace.TraceContext` | Same as above. |
| `InboxExecutionContextKeys.IsInboxExecution` | `__LiteBus.Inbox.IsInboxExecution` | `CommandInboxDispatcher` (value `true`) |
| `InboxExecutionContextKeys.MessageId` | `__LiteBus.Inbox.MessageId` | `CommandInboxDispatcher` |
| `InboxExecutionContextKeys.ContractName` | `__LiteBus.Inbox.ContractName` | `CommandInboxDispatcher` |
| `AmbientAuditScope.ItemKey` (internal) | `__LiteBus.Audit.Scope` | `AmbientAuditScope` |

```csharp
public sealed class SkipNotificationsPostHandler : ICommandPostHandler<PlaceOrderCommand>
{
    public Task PostHandleAsync(PlaceOrderCommand message, object? result, CancellationToken ct = default)
    {
        var ctx = AmbientExecutionContext.Current;

        if (ctx.Items.TryGetValue(InboxExecutionContextKeys.IsInboxExecution, out var replay) && replay is true)
        {
            ctx.SuppressPostHandlers();   // do not re-notify on an inbox replay
        }

        return Task.CompletedTask;
    }
}
```

`IExecutionContext.Data` returns an `IHandleContextData`, a store keyed by the CLR type of the value rather than by a string. The mediation creates it once.

| Member | Behavior |
| --- | --- |
| `Set<T>(T value)` | Stores the value under `typeof(T)`, replacing any existing value. Throws `ArgumentNullException` on null. |
| `Get<T>()` | Returns the value or throws `HandleContextDataNotFoundException` (exposes `DataType` and `Key`). |
| `TryGet<T>(out T value)` | Returns `false` instead of throwing when absent. |
| `Contains<T>()` / `Remove<T>()` | Checks presence and removes the value. |
| `Set<T>(object key, T value)` | Stores the value under the type and a caller-supplied key, for a mediation holding several values of one type. |
| `Get<T>(object key)` / `TryGet<T>(object key, out T value)` | Reads values by key. |
| `Contains<T>(object key)` / `Remove<T>(object key)` | Checks presence and removes values by key. |

The unkeyed slot and a keyed slot of the same type are separate slots, so a stage that stores unkeyed cannot erase a keyed entry by accident. The store compares keys with `object.Equals`, so an identifier value object works directly as a key:

```csharp
// A command naming two accounts, which the unkeyed store cannot express.
context.Data.Set(new AccountId("a-1"), debit);
context.Data.Set(new AccountId("a-2"), credit);

var debit = context.Data.Get<Account>(new AccountId("a-1"));
```

The implementation is `HandleContextData` in `LiteBus.Messaging.Abstractions`. It is public so a test double implementing `IExecutionContext` has a working store to return. A `Lock` guards access, because event handlers can run in parallel over one execution context.

The store exists so a pre-stage handler can hand a resolved object to the main handler. The motivating case is a guard whose authorization decision depends on a loaded aggregate. Without a typed channel, the handler loads the same aggregate again, which is why authorization tends to stay inside handlers instead of moving to the stage that owns the decision.

```csharp
// Guard
var occurrence = await _occurrences.LoadAsync(message.OccurrenceId, cancellationToken);
if (occurrence is null) return Verdict.Deny("the occurrence does not exist");
if (!await _authorizer.MayCancelAsync(occurrence, cancellationToken)) return Verdict.Deny("not permitted");
AmbientExecutionContext.Current.Data.Set(occurrence);
return Verdict.Allow;

// Handler
var occurrence = AmbientExecutionContext.Current.Data.Get<Occurrence>();
```

Use `Items` (`IDictionary<string, object>`) only where the key comes from outside the process or the value is a flag. Prefer `Data` for anything with a type to key on. `Items` and the audit scope property bag are safe for concurrent use by event handlers running in parallel over one mediation. When two handlers race to set the same key, the value left is whichever landed last.

### 4.7 Message metadata and definitions

Two declaration sources feed one type-keyed metadata bag per message type, exposed as `IMessageDescriptor.Metadata`:

```csharp
public interface IMessageMetadata
{
    IReadOnlyCollection<object> Values { get; }
    bool TryGet<TValue>([MaybeNullWhen(false)] out TValue value) where TValue : notnull;
    bool Contains<TValue>() where TValue : notnull;
}
```

Source 1 is attributes that implement `IMessageDeclarationSource`:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class RequiresPermissionAttribute : Attribute, IMessageDeclarationSource
{
    public RequiresPermissionAttribute(string permission) => Permission = permission;

    public string Permission { get; }

    public Type DeclarationType => typeof(RequiredPermission);

    public object CreateDeclaration() => new RequiredPermission(Permission);
}
```

The registry collects only attributes implementing this interface, so unrelated attributes never enter the metadata bag.

Source 2a is definition classes implementing `IMessageDefinition<TMessage>` and declaring in one method. Prefer this shape because it scales to any number of declarations:

```csharp
internal sealed class PlaceOrderCommandDefinition : IMessageDefinition<PlaceOrderCommand>
{
    public void Describe(IMessageDeclarations declarations)
    {
        declarations.Audited("orders.place-order", category: "money", targetKind: "order");
        declarations.Declare(new RequiredPermission("orders.write"));
    }
}
```

`IMessageDeclarations` has these members: `Declare<TValue>(TValue)`, `Audited(action, category, targetKind, reasonRequired)`, `NotAudited(rationale)`, `Authorized(policy)`, `AuthorizationExempt(rationale)`, and `Exempt<TValue>(rationale)`. `NotAudited` records both halves: the `AuditExemptDeclaration` the record writer reads and the exemption every requirement reads. `AuthorizationExempt` does the same for `AuthorizationDeclaration`.

Declaring the same value type twice in one `Describe` raises `MessageDeclarationException` naming the definition and the message, because the second would silently replace the first. A `Describe` that declares nothing raises the same exception, because it is indistinguishable from a definition nobody finished. The registry unwraps anything `Describe` throws from the reflective invocation, so a configuration error reads as one rather than as a `TargetInvocationException`.

Source 2b is definition classes implementing closed `IMessageDefinition<TMessage, TValue>`. This shape types one declaration against the compiler, and it fits best when a message declares exactly one thing, which is why `IAuditDefinition<TMessage>` and `IIdempotencyDefinition<TMessage>` are built on it. Past one declaration the shape costs more than it saves, because the second and every later value must be written as an explicit interface implementation naming the message type and the value type again:

```csharp
public sealed class PlaceOrderCommandDefinition :
    IAuditDefinition<PlaceOrderCommand>,
    IMessageDefinition<PlaceOrderCommand, RequiredPermission>
{
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.place-order") with
    {
        Category = "money",
        TargetKind = "order"
    };

    RequiredPermission IMessageDefinition<PlaceOrderCommand, RequiredPermission>.Value
        => new("orders.write");
}
```

A definition must have a parameterless constructor (public or not). The registry instantiates it **once** during registration, so definitions cannot take dependencies. `IAuditDefinition<TMessage>` is the shipped specialization (`AuditDeclaration Audit { get; }` mapped onto `IMessageDefinition<TMessage, AuditDeclaration>.Value`). Both shapes write into the same type-keyed bag, and one class may implement both, so a codebase uses whichever fits each message, and a reader looks a value up by its own type either way.

Precedence resolves through `MessageMetadata.Wins` and `MetadataSourceKind`:

* For the same declaring message type, `Definition` (1) beats `Attribute` (0).
* For different declaring types, the more derived declaring type wins.
* When two unrelated declaring types both cover one message, the registry throws `MessageDeclarationException` telling the caller to declare the value on the message itself.
* A declaration whose value is not an instance of its declared key type throws `MessageDeclarationException`.
* Two definitions declaring the same value type for the same message throw `MessageDeclarationException`.
* A definition that implements neither definition shape, exposes no readable `Value`, exposes no `Describe`, declares nothing, or returns `null` throws `MessageDeclarationException`.

A declaration written for a base type covers derived messages through `IsAssignableFrom`. Open generic types never cover each other.

A requirement fails composition for any registered message in its scope that neither declares the value nor records an exemption from it. The requirement takes three scopes:

```csharp
registry.AddMessaging(messaging => messaging
    // Every registered message. Usually too wide: requiring a permission of every message also demands one from
    // every query, and the exemptions written to satisfy that say nothing.
    .RequireDeclaration<RetentionClass>()

    // Every message assignable to a marker. The form to reach for, and what makes the feature pay for itself.
    .RequireDeclaration<RequiredPermission, ICommand>()
    .RequireDeclaration<RequiredPermission, IActingAccountCommand>()

    // An arbitrary predicate, with the words the error uses. A predicate cannot describe itself.
    .RequireDeclaration<RetentionClass>(type => type.Namespace!.Contains("Billing"), "every billing message"));
```

The scope is a type rather than a namespace on purpose. A namespace is a string that a refactoring tool moves without telling anyone, so a requirement keyed on one silently stops applying when a folder is renamed. For an authorization rule, that failure is an unguarded command that used to be guarded.

The builder registers the check through `IModuleConfiguration.RegisterCompositionValidation(Action)`, and it runs after every module has been built, because the messaging module is foundational and has no commands or queries to inspect during its own build. Both host adapters (the Microsoft DI `AddLiteBus` and the Autofac `AddLiteBus` on `ContainerBuilder` in namespace `LiteBus.Extensions.Autofac`) run `moduleConfiguration.CompositionValidations` after the module loop.

The check skips abstract types and interfaces, because they are shapes, and a declaration on one covers the messages beneath it. The `MessageDeclarationException` names every offender grouped by the omitted value type, alongside the scope of the requirement it violated, not only the first.

A family default covers many messages at once. `MessageModuleBuilder.DeclareDefault<TScope, TValue>(value)` declares a value for every message assignable to `TScope`, and `DeclareDefault(MessageDeclarationItem)` does the same when the types are values rather than something the call site can name:

```csharp
registry.AddMessaging(messaging => messaging
    .DeclareDefault<IOrganizationCommand, RequiredAuthorization>(
        new RequiredAuthorization(PermittedAction.ManageOrganization, Subject.Organization))
    .RequireDeclaration<RequiredAuthorization, IOrganizationCommand>());
```

No new rule decides precedence: a declaration resolves to the one written closest to the message, so a command carrying its own `TValue` keeps it, and the rest of the family inherits the default. That is the rule a definition written for a base type has always followed, stated here without a file. The application declares the default on the messaging module, which builds first, so a default reaches messages the axis modules register afterwards.

Two defaults for one scope and value type raise `PipelineContractException`, as do a default and a definition both declared against the same message. One of them would have to be discarded and nothing says which. A value that is not an instance of its key type raises `MessageDeclarationException`, because a reader looking it up by that type would find nothing.

At the lower layer, `IMessageWriter.AddDeclaration(MessageDeclarationItem)` records a declaration without a definition class, through the same path a definition uses. `MessageDeclarationItem` is a sealed record with required `MessageType` (`Type`), `DeclarationType` (`Type`), and `Value` (`object`). The static `For<TMessage, TValue>(TValue value)` (both `notnull`) keys the value by its own type. The default `AddDeclaration` implementation throws rather than accepting the call and dropping it, because a silently dropped authorization default is an unguarded command that looks configured.

Applications can add their own composition checks. `MessageModuleBuilder.ValidateComposition(Action<IMessageCatalog>)` runs an application rule over every registered message at the same point. `IMessageCatalog` is `IEnumerable<MessageCatalogEntry>` plus `Count` and `Audited()`. A `MessageCatalogEntry` carries `MessageType`, `Metadata`, and the convenience `Audit` (the `AuditedDeclaration`, or null for an exempt or undeclared message). To fail composition, throw from the callback, and name every offender in one message rather than the first.

```csharp
messaging.ValidateComposition(catalog =>
{
    var wrong = catalog.Audited()
        .Where(entry => !entry.Audit!.Action.StartsWith(entry.Audit.Category + "."))
        .Select(entry => entry.MessageType.Name)
        .ToList();

    if (wrong.Count > 0)
    {
        throw new InvalidOperationException($"Audit actions must start with their category: {string.Join(", ", wrong)}");
    }
});
```

Two checks every audited application needs ship as built-ins, because getting either wrong corrupts the trail rather than breaking the build:

* `RequireUniqueAuditActions()`: two messages sharing an action code make the trail unqueryable by use case, which is the one thing an action code is for. It raises `AuditConfigurationException` naming each duplicate and its claimants.
* `RequireAuditActionFormat(pattern)` defaults to `MessageModuleBuilder.DefaultAuditActionPattern` (`^[a-z0-9]+(?:-[a-z0-9]+)*\.[a-z0-9]+(?:-[a-z0-9]+)*$`), the `category.kebab-action` convention every LiteBus example uses. It raises `AuditConfigurationException` naming each offender and its action.

The hook exists because the underlying `RegisterCompositionValidation` was public but unreachable in practice: the only way to receive an `IModuleConfiguration` is to implement `IModule`, which demands a full module implementation for a five-line assertion.

The catalogue is also available at runtime. `IMessageCatalog` is registered as a Singleton, so it resolves at runtime and not only inside a composition check. The container takes the snapshot on first resolve, after every module has registered its messages.

```csharp
var catalog = provider.GetRequiredService<IMessageCatalog>();

IReadOnlyList<AuditCatalogueRow> rows = catalog.ToRows();   // Action, MessageType, Category, TargetKind, ReasonRequired
string document = catalog.ToMarkdown();                      // one formatter over those rows
```

`AuditCatalogue.ToRows` and `ToMarkdown` are extension methods in `LiteBus.Messaging.Registry`. Rows are the primary surface, because what a compliance process consumes differs per team. A library emitting only Markdown would serve one team and get in the way of the rest. Rows are ordered by action so two runs produce the same document. An exempt or undeclared message is absent, because this builds a catalogue of audited actions.

The other half of an authorization matrix is the application's. A required permission is an application value type, so the application projects it from `MessageCatalogEntry.Metadata` alongside these rows:

```csharp
var matrix = catalog
    .Where(entry => entry.Metadata.TryGet<RequiredPermission>(out _))
    .Select(entry =>
    {
        entry.Metadata.TryGet<RequiredPermission>(out var permission);
        return new { entry.MessageType.Name, permission!.Name, entry.Audit?.Action };
    });
```

Exemptions use `[DeclarationExempt(typeof(TValue), "rationale")]` (full name `DeclarationExemptAttribute`, `[AttributeUsage(Class | Struct, AllowMultiple = true, Inherited = false)]`, constructor `(Type declarationType, string rationale)` with a non-blank rationale, members `DeclarationType`, `Rationale`, and `CreateExemption()`). The attribute is repeatable, and `MessageDescriptor` aggregates every instance into one `DeclarationExemptions` metadata value (`Values`, `Covers<TValue>()`, `Covers(Type)`, `TryGet(Type, out DeclarationExemption)`).

`DeclarationExemptions` offers constructors `(IEnumerable<DeclarationExemption> exemptions)` (null-checked entries, first wins per declaration type) and `(Type declarationType, string rationale)`. It implements `IMessageDeclarationExemptionSource` (whose single member is `DeclarationExemption CreateExemption()`) rather than `IMessageDeclarationSource`, because the latter maps one attribute to one value and lets the last win, while several exemptions must collapse into one set. A definition may contribute `DeclarationExemptions` directly, replacing the attribute set rather than adding to it.

There is one mechanism, not two. `[DeclarationExempt(typeof(AuditDeclaration), "rationale")]` exempts a message from auditing and satisfies `RequireDeclaration<AuditDeclaration>()`. `[AuditExempt("rationale")]` is the shorthand for exactly that and records the same exemption, so every exemption a message carries reads from one place whichever spelling wrote it.

`[AuditExempt]` additionally produces the `AuditExemptDeclaration` the record writer reads, because auditing is one of the two declarations whose positions are both modelled as values. `[AuthorizationExempt("rationale")]` mirrors it for `AuthorizationDeclaration`.

An attribute becomes analyzable through `[MessageDeclaration(typeof(TValue))]` (full name `MessageDeclarationAttribute`, `[AttributeUsage(AttributeTargets.Class, Inherited = false)]` on the attribute class, constructor `(Type declarationType)`, `Type DeclarationType { get; }`). It states which value that attribute declares. The annotation exists because `IMessageDeclarationSource.DeclarationType` is a runtime property an analyzer cannot execute, and `LB1020` needs a static answer.

Registration throws `PipelineContractException` when the annotation and the property name different types. LiteBus annotates `[Audited]` and `[AuditExempt]` with `typeof(AuditDeclaration)` and `[Authorized]` and `[AuthorizationExempt]` with `typeof(AuthorizationDeclaration)`. Definition classes need no annotation, because their declaration is the second type argument of `IMessageDefinition<TMessage, TValue>`.

Application code reads declarations through `IMessageMetadataAccessor` (singleton, `MessageMetadataAccessor` over `IMessageReader`). It is the supported surface. Reaching for `IMessageRegistry.Find(...)!.Metadata` instead makes the descriptor shape part of the application.

```csharp
public interface IMessageMetadataAccessor
{
    IMessageMetadata ForMessage(Type messageType);
    IMessageMetadata ForMessage<TMessage>() where TMessage : notnull;
    bool TryGet<TValue>(Type messageType, [MaybeNullWhen(false)] out TValue value) where TValue : notnull;
    bool TryGet<TMessage, TValue>([MaybeNullWhen(false)] out TValue value) where TMessage : notnull where TValue : notnull;
}
```

Metadata follows the descriptor the mediation resolved. An unregistered runtime type mediated through a base descriptor reads that descriptor's metadata, exactly as if the type had been registered and inherited the base declarations, so such mediations are audited, guarded, and idempotent the same as registered ones.

Only a type nothing registered covers raises `MessageMetadataNotFoundException` (exposes `MessageType`) rather than returning an empty bag, because an empty answer turns a missing registration into a permission check that silently passes. An ambiguous fallback raises `AmbiguousMessageResolveException`, the same exception mediating the type would raise. A closed generic message resolves to its generic type definition, matching `IMessageReader.Find`.

The messaging module registers the accessor through `AddMessaging`, so it is available with or without auditing.

The intended use is one generic pre-stage handler enforcing a declaration for every message that carries it:

```csharp
internal sealed class PermissionGuard<TCommand> : ICommandGuard<TCommand>
    where TCommand : ICommand
{
    // ctor omitted
    public Task<Verdict> DecideAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        if (!_metadata.TryGet<TCommand, RequiredPermission>(out var required))
        {
            return Task.FromResult(Verdict.Allow);
        }

        return Task.FromResult(_actor.Holds(required) ? Verdict.Allow : Verdict.Deny("insufficient permission"));
    }
}

registry.AddCommands(builder => builder.Register(typeof(PermissionGuard<>)));
```

Declaration values may be delegates. A definition is instantiated once and cannot take dependencies, but its value is an ordinary object and may carry a `Func` over the message. That covers every scope derivable from the message itself, not only constants:

```csharp
public sealed record AuthorizationScope(Func<object, string> Resolve)
{
    public static AuthorizationScope Fixed(string scope) => new(_ => scope);

    public static AuthorizationScope FromMessage<TMessage>(Func<TMessage, string> resolve)
        where TMessage : notnull
        => new(message => resolve((TMessage) message));
}
```

The delegate is built at registration, so it can project from the message but cannot resolve services. Anything needing a database read belongs in the guard, which hands its result forward through `IExecutionContext.Data`.

### 4.8 Auditing

**Types:** `AuditDeclaration` / `AuditedDeclaration` / `AuditExemptDeclaration`, `AuditedAttribute` / `AuditExemptAttribute`, `IAuditDefinition<TMessage>`, `AuditRecord`, `AuditActor`, `AuditOutcome`, `IAuditTrail`, `IAuditScope`, `IAuditActorResolver`, `IAuditRecordWriter`, `IAuditOutcomeMapper`, `DefaultAuditOutcomeMapper`, `AuditReasonMissingException`, `AuditTrailDiagnosticCheck`, `AuditingBuilder`, `CommandAuditCompletionHandler`, `QueryAuditCompletionHandler`, `EventAuditCompletionHandler`.

The declaration half works without the writing half. `AddMessaging` registers `IAuditScope` and `IAuditOutcomeMapper`, not `EnableAuditing()`, so both resolve whether or not any axis produces records. An application that wants the declaration model with its own writer injects `IAuditScope`, reads `AuditDeclaration` through `IMessageMetadataAccessor`, and never calls `EnableAuditing()`. In that configuration nothing consumes the scope, so pushed values are discarded. That behavior is intended. The `litebus.audit.trail` probe stays quiet because auditing was never enabled.

Call `AddAuditing` to configure the feature in one call:

```csharp
liteBus.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
    .UseTrail<SqlAuditTrail>()                        // scoped by default; pass InstanceLifetime to change it
    .UseActorResolver<RequestActorResolver>()         // who acted; see below
    .UseOutcomeMapper<UseCaseAuditOutcomeMapper>()    // optional
    .ForCommands()
    .ForQueries()));

liteBus.AddCommands(commands => commands.RegisterFromAssembly(assembly));
liteBus.AddQueries(queries => queries.RegisterFromAssembly(assembly));
```

`AuditingBuilder` exposes `UseTrail<T>(InstanceLifetime)`, `UseTrailInstance`, `UseActorResolver<T>(InstanceLifetime)`, `UseActorResolverInstance`, `UseOutcomeMapper<T>()`, `UseOutcomeMapperInstance`, `ForCommands()`, `ForQueries()`, `ForEvents()` and `ForAllAxes()`. Configuring a trail but selecting no axis raises `AuditConfigurationException` at composition. No probe can report that state at runtime. Nothing is ever audited, so nothing ever fails. Calling `AddAuditing` twice adds to the same selection instead of replacing it. An axis that is enabled once stays enabled.

The per-axis primitives still exist. `AddAuditing` composes them:

```csharp
liteBus.AddMessaging(messaging => messaging.UseAuditTrail<SqlAuditTrail>());
liteBus.AddCommands(commands => commands.EnableAuditing());
liteBus.AddQueries(queries => queries.EnableAuditing());
liteBus.AddEvents(events => events.EnableAuditing());   // one record per publish, not per handler
```

`EnableAuditing()` registers a completion handler at priority `HandlerPriorities.Observability` for `ICommand` / `IQuery` / `IEvent` respectively, and it registers the `litebus.audit.trail` diagnostic probe. When auditing is enabled but no `IAuditTrail` is registered, the probe reports `Unhealthy`. When a trail is registered but no `IAuditActorResolver` is registered, the probe reports `Degraded`, because every record would then carry no actor. Otherwise the probe reports `Healthy` with `component`, `trailRegistered`, `trailType`, `trailIsSingleton` and `actorResolverRegistered` data.

It resolves the trail through `IMessageDispatchScopeFactory` twice and compares the instances. The comparison reveals the lifetime from outside the container. Resolving through the scope factory also avoids an error. Resolving a scoped trail from a root provider fails under `ValidateScopes`. When no trail is registered, resolving `IAuditRecordWriter` throws `AuditConfigurationException`.

An application that replaced the record writer is exempt from these checks. The probe first reads `LiteBusCompositionSummary.AuditRecordWriter`. When it is set, the probe reports `Healthy` with `component` and `recordWriter` and asserts nothing else. A writer that LiteBus did not build need not use an `IAuditTrail` or an `IAuditActorResolver`, so demanding either would report a correct configuration as broken. It reads the summary instead of resolving `IAuditRecordWriter`, because resolving the built-in writer with no trail registered throws. That throw is the state the probe exists to report.

To own the record shape, replace the writer with `MessageModuleBuilder.UseAuditRecordWriter<TWriter>(lifetime)` or `UseAuditRecordWriterInstance`, exposed on the auditing builder as `UseRecordWriter<TWriter>` and `UseRecordWriterInstance`:

```csharp
registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
    .UseRecordWriter<EntroAuditRecordWriter>()
    .ForAllAxes()));
```

`AuditRecord` is a handoff to `IAuditTrail`, not a persistence schema. A different set of fields therefore needs no new abstraction here. It needs a different writer. `IAuditRecordWriter.WriteAsync(MessageCompletionContext, CancellationToken)` is the entire contract between the pipeline and auditing. The three axis completion handlers depend on nothing else. A replacement therefore keeps the completion-stage placement, the `HandlerPriorities.Observability` priority, and the per-axis selection, and it replaces only the record building.

A replacement takes over everything the built-in writer does. Each part of that work maps to public API:

| What the built-in writer does | What a replacement uses |
| --- | --- |
| Skips a message that declares no audit position | `IMessageRegistry.Find`, then `Metadata.TryGet<AuditDeclaration>()` |
| Enforces a required justification | `AuditedDeclaration.ReasonRequired`, `AuditReasonMissingException` |
| Reads the target, reason, properties, and actor that a handler supplied | Inject `IAuditScope` and read the ambient state through its getters |
| Stamps the correlation and tenant identifiers | `AmbientExecutionContext.GetCurrentOrDefault()`, `MessageTraceContextKeys` |
| Classifies the outcome and the failure code | `IAuditOutcomeMapper`, which is already injectable |

Skipping an undeclared message is part of that contract instead of a detail. Without it, every message on the selected axes would produce a record. `LiteBusCompositionSummary.AuditRecordWriter` names the replacement in the startup line. Replacing the writer replaces all of record building, so without that startup line a misregistration would look the same as auditing working.

A generic `AuditRecord<T>` or `IAuditTrail<TRecord>` would spread a type parameter through the writer, the three axis completion handlers, the probe, the builder, and `AddAuditing`. Every application that wants the default would then carry a cost that serves only the minority that does not, and the parameter would add nothing a custom writer cannot already do. The fixed shape is also a deliberate choice. It maps onto NIST SP 800-53 AU-3, PCI DSS Requirement 10, and the DMTF CADF event model, so a trail can reach a SIEM later without being remodelled.

`AuditRecord.Actor` is the first column an audit review reads, and it is the one part LiteBus cannot derive. Supply it with an `IAuditActorResolver`, which runs at the completion stage. It therefore attributes a denied or failed command as well as a successful one:

```csharp
internal sealed class RequestActorResolver : IAuditActorResolver
{
    public AuditActor? Resolve(MessageCompletionContext context) => context.Message switch
    {
        IActingAccountCommand acting => AuditActor.User(acting.ActingAccountId.ToString()),
        _ => AuditActor.System(ProcessNameOf(context.Message.GetType()))
    };
}
```

`AuditActor` carries `Id` and `Kind` (both required), and `DisplayName` and `OnBehalfOf`. It provides the factories `User(id)`, `System(processName)`, and `For(kind, id)`. Add a display name with `with { DisplayName = name }`. Returning `null` is legitimate and means nothing established an actor. Prefer `AuditActor.System` when the application knows a worker acted. A scheduled job and an unattributed action are different answers. An audit query has to tell them apart. A handler that knows more than the resolver overrides it with `IAuditScope.WithActor`.

Resolving at the completion stage makes a pre-stage actor guard unnecessary. A guard that denies stops the pipeline before any pre-handler runs, and that denied path is exactly the case a trail exists to record. Attribution established in a pre-handler is therefore lost on the denied path.

The message itself is deliberately not on `AuditRecord`. LiteBus hands it to the resolver instead, so a payload cannot reach audit storage by default. A record that holds a payload turns an audit table into an erasure liability.

Declare a position with one of these attributes:

```csharp
[Audited("orders.place-order", Category = "money", TargetKind = "order", ReasonRequired = false)]
public sealed record PlaceOrderCommand(Guid CartId) : ICommand<OrderId>;

[AuditExempt("browsing a public storefront is not a sensitive action")]
public sealed record GetStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
```

Analyzer `LB1018` ("Message states no audit position", **disabled by default**) reports messages that declare neither position. Enable it with `dotnet_diagnostic.LB1018.severity = warning`. It is the preconfigured instance of `LB1020`. It shares `DeclarationAnalysis` and uses `AuditDeclaration` as the required value type.

`DeclarationAnalysis` recognizes four shapes. Both `LB1018` and `LB1020` cover all four shapes because they share the pass:

| Shape | How it is read |
| --- | --- |
| An attribute annotated `[MessageDeclaration(typeof(TValue))]`, including `[Audited]` | The attribute on the message type |
| `[DeclarationExempt(typeof(TValue))]`, including `[AuditExempt]` | The attribute's first constructor argument |
| `IMessageDefinition<TMessage, TValue>`, including `IAuditDefinition<TMessage>` | The contract's type arguments |
| `IMessageDefinition<TMessage>.Describe(IMessageDeclarations)` | The calls in the method body |

The keyed contract names its value type, so the interface settles the value type. The `Describe` shape names nothing in the contract, so the body has to be read. `Declare<TValue>` and `Exempt<TValue>` are matched on the type argument. `Audited(...)` and `NotAudited(...)` both count as declaring `AuditDeclaration`. `Authorized(...)` and `AuthorizationExempt(...)` both count as declaring `AuthorizationDeclaration`. Each pair states a position, and the rule asks whether the message answered rather than which answer it gave. Coverage still walks base types and interfaces, so a definition written for a marker covers the family in both cases.

Three cases are unreadable and are treated as declared: a definition in a referenced assembly, an implementation the pass cannot locate, and a body that hands the collector to another method. The pass deliberately resolves these cases in favour of the build. A false negative leaves a gap that the composition check still enforces at startup, while a false positive produces a build that cannot pass without turning the rule off. For an analyzer that is opt-in, a false positive is the worse failure.

Contribute the runtime half from the handler:

```csharp
public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, OrderId>
{
    private readonly IAuditScope _audit;

    public PlaceOrderCommandHandler(IAuditScope audit) => _audit = audit;

    public async Task<OrderId> HandleAsync(PlaceOrderCommand message, CancellationToken ct = default)
    {
        var order = Order.Place(message.CartId);

        _audit.WithTarget(order.Id.ToString())
              .WithReason("customer checkout")
              .WithProperty("channel", "web");

        return order.Id;
    }
}
```

`IAuditScope` exposes `Actor`, `TargetId`, `Reason` and `Properties`, and the pushers `WithActor`, `WithTarget`, `WithReason` and `WithProperty`. All throw `ArgumentException` on a blank argument, `ArgumentNullException` on a null actor, and `NoExecutionContextException` when called outside a mediation, because the scope needs the ambient execution context to store state.

`AuditedDeclaration.WithTargetFrom<TMessage>(targetSelector)` and `WithReasonFrom<TMessage>(reasonSelector)` read the target and reason from the message. A definition therefore declares them once instead of every handler supplying them, as in `Declare<AuditDeclaration>(AuditDeclaration.Audited(...).WithTargetFrom<T>(...))`. Target precedence is scope, then selector. Reason precedence is scope, then the pipeline decision reason, then selector, so a denial record explains the denial.

Selectors run lazily, only when earlier sources supplied no value. A null or blank selector result means no value. `ReasonRequired` applies to the final resolved reason. A selector that throws fails the record write the same way a throwing actor resolver does. Attributes cannot carry delegates, so selectors need a definition.

Map outcomes with `DefaultAuditOutcomeMapper.MapByOutcome`:

| `MediationOutcome` | `AuditOutcome` |
| --- | --- |
| `Succeeded` | `Succeeded` |
| `Answered` | `Succeeded` |
| `Denied` | `Denied` |
| `Invalid` | `Invalid` |
| `Canceled` | `Canceled` |
| `Failed` (and anything else) | `Failed` |

`IAuditOutcomeMapper.MapFailureCode` has a default implementation. It returns the decision's own `Code` for a `Denied` or `Invalid` outcome, `null` for no exception and for `LiteBusMessageDeniedException` / `LiteBusMessageInvalidException`, and the exception type name otherwise. Only a refusal contributes its code. A shortcut answers with a code too, and that mediation reports `Succeeded`. Carrying it here would put a cache-hit code in the field that a review reads as the reason something did not work.

```csharp
public sealed class UseCaseAuditOutcomeMapper : IAuditOutcomeMapper
{
    public AuditOutcome Map(MessageCompletionContext context) => context.Outcome switch
    {
        MediationOutcome.Failed when context.Exception is ForbiddenException => AuditOutcome.Denied,
        _ => DefaultAuditOutcomeMapper.MapByOutcome(context)
    };
}
```

The trail contract is one method:

```csharp
public interface IAuditTrail
{
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
```

The record arrives at the completion stage, at priority `HandlerPriorities.Observability`. The completion stage is not cancellable, so the token is always `CancellationToken.None`. When `ReasonRequired = true` and the outcome is `Succeeded` with no reason, the write raises `AuditReasonMissingException` instead of writing an incomplete record. It derives from `Exception` rather than from `LiteBusConfigurationException`, because a handler that omitted a call is a data problem in one mediation, and an application catching composition faults at startup must not also catch it. The throw happens at `HandlerPriorities.Observability`, which runs before the commit at `HandlerPriorities.UnitOfWork`. The work is therefore rolled back instead of being recorded without its justification.

LiteBus never opens or commits a transaction, but it guarantees a position from which an application can commit. To make a record atomic with its change, register a completion handler at `HandlerPriorities.UnitOfWork` and stage the record from the trail rather than writing it:

```csharp
[HandlerPriority(HandlerPriorities.UnitOfWork)]
public sealed class CommitUnitOfWork : ICommandCompletionHandler
{
    private readonly IDocumentSession _session;

    public CommitUnitOfWork(IDocumentSession session) => _session = session;

    public async Task HandleCompletionAsync(MessageCompletionContext<ICommand> context, CancellationToken cancellationToken)
    {
        if (context.Outcome is MediationOutcome.Succeeded or MediationOutcome.Answered)
        {
            await _session.SaveChangesAsync(CancellationToken.None);
        }
    }
}

public sealed class MartenAuditTrail : IAuditTrail
{
    // ctor omitted
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        if (record.Outcome == AuditOutcome.Succeeded)
        {
            _session.Store(record);   // staged; the commit above flushes it with the change
            return Task.CompletedTask;
        }

        return _archive.WriteAsync(record, cancellationToken);   // the transaction is rolling back
    }
}
```

Three guarantees make it work:

1. `UnitOfWork` is above `ReservedCeiling`, so it runs after the writer at `Observability` in every release. It is also above the ceiling itself, so an application handler placed on the ceiling is ordered before the commit instead of tied with it.
2. The completion stage orders by priority alone, so registration breadth cannot reorder the commit against the writer.
3. When a completion handler throws on an otherwise clean mediation, the exception propagates, so a commit conflict reaches the caller instead of being swallowed.

Do not put the commit in a post-handler. When the main handler throws, a post-handler is skipped, and everything LiteBus writes afterwards falls outside the transaction by construction.

### 4.9 In-process idempotency

**Types:** `IdempotencyDeclaration`, `IIdempotencyDefinition<TMessage>`, `IIdempotencyStore`, `IdempotencyClaim`, `IdempotencyClaimOutcome`, `SuppliedIdempotencyKey`, `ClaimedIdempotencyKey`, `IdempotencyRefusalCodes`, `IdempotencyConfigurationException` (all `LiteBus.Messaging.Abstractions`); `IdempotencyKeyResolver`, `ResolvedIdempotencyKey`, `IdempotencyStoreDiagnosticCheck` (`LiteBus.Messaging.Idempotency`); `IdempotentCommandShortcut<TCommand>`, `IdempotentCommandShortcut<TCommand, TCommandResult>`, `IdempotencyCompletionHandler` (`LiteBus.Commands.Idempotency`, internal); `PostgreSqlIdempotencyStore` (`LiteBus.Commands.Storage.PostgreSql`); `MartenIdempotencyStore`, `MartenIdempotencyStoreOptions`, `MartenIdempotencyRecord` (`LiteBus.Commands.Storage.Marten`); `InMemoryIdempotencyStore` (`LiteBus.Testing.Mediation`).

In-process idempotency differs from durable-messaging idempotency (`Idempotency`, `IdempotencyConflictMode` in `DurableMessaging`), which removes duplicate inbox and outbox envelopes. It removes duplicate deliveries of one command mediation.

Declare idempotency on the command definition:

```csharp
public sealed class ApplyPaymentCommandDefinition : IIdempotencyDefinition<ApplyPaymentCommand>
{
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<ApplyPaymentCommand>(command => command.PaymentId.ToString())
            with { Scope = "payments", ReplayResult = false };
}
```

| Member | Meaning |
| --- | --- |
| `KeySelector` (`Func<object, string>?`) | A pure projection from the message, or `null` when the declaration was created by `KeyedByContext` and the key is supplied through the execution context instead. It runs once per mediation and cannot resolve services. |
| `FingerprintSelector` (`Func<object, string>?`) | A content fingerprint projection, or `null` when the declaration carries no fingerprint and any repeat of the key replays. Set through `WithFingerprint<TMessage>(fingerprintSelector)`. |
| `Scope` | A key prefix, defaulting to the message full type name. The effective key is `"{Scope ?? FullName}:{key}"` with each segment backslash-escaped and an ambient tenant prefixed as a third segment. A blank explicit scope throws `IdempotencyConfigurationException`. |
| `ReplayResult` | Whether a repeat replays the recorded result. It requires a serializable result. A `KeyedByContext` declaration that replays results should also set a fingerprint, or any repeat of the key is answered with the stored result even when it is a different request reusing a predictable key. |

The record also has a public `IdempotencyDeclaration(Func<object, string> keySelector)` constructor. A null selector throws. `KeyedBy<TMessage>` wraps a typed selector in it. A blank key raises `IdempotencyConfigurationException`. Every blank key shares one key space, so without that check the first message would answer all the others.

When the key cannot be projected from the message because deriving it needs services, declare `IdempotencyDeclaration.KeyedByContext()` and let a guard supply the key. Guards run before shortcuts, so a guard can verify the message and hand the key forward with `context.Data.Set(new SuppliedIdempotencyKey(key))` before the idempotency shortcut reads it. `SuppliedIdempotencyKey` rejects a blank value with `ArgumentException`. A guard that also sets the `MessageTraceContextKeys.TenantId` execution-context item scopes the resolved key to that tenant.

The pipeline reads the key once and reuses it in both the shortcut that claims it and the completion handler that settles it, so the derivation runs once per mediation. When a message declared this way reaches the shortcut with no key in context, the shortcut raises `IdempotencyConfigurationException` naming the message type. A generated fallback would make every delivery look distinct, so the declaration would appear to work while doing nothing.

Enable idempotency with `CommandModuleBuilder.EnableIdempotency()`. It registers both shortcut arities plus `IdempotencyCompletionHandler`, and the `litebus.idempotency.store` probe. The probe reports `component`, `storeRegistered`, and `storeType`, and it reports `Unhealthy` when no store is registered. All three ignore a command that declares nothing, so one call covers the axis.

`AddMessaging` registers `IdempotencyKeyResolver` (constructed with `IMessageMetadataAccessor`) as a singleton. The store is registered by the command storage packages or the application. Its `Resolve(object message, IExecutionContext? executionContext = null)` returns `null` when the message declares nothing. Otherwise it returns a `ResolvedIdempotencyKey(string Key, IdempotencyDeclaration Declaration, string? Fingerprint)` readonly record struct carrying the scoped key and the selected fingerprint. The context defaults to the ambient context of the mediation in flight.

The store contract is three methods:

```csharp
public interface IIdempotencyStore
{
    Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default);
    Task CompleteAsync(string key, string claimToken, string? payload, CancellationToken cancellationToken = default);
    Task ReleaseAsync(string key, string claimToken, CancellationToken cancellationToken = default);
}
```

`IdempotencyClaim` is `(IdempotencyClaimOutcome Outcome, string? Payload = null, string? ClaimToken = null)` with factories `Granted(claimToken)`, `InFlight`, `FingerprintMismatch`, and `AlreadyCompleted(payload = null)`. A blank token passed to `Granted` throws `ArgumentException`.

Each outcome has an effect. `Granted` runs, `AlreadyCompleted` replays, `InFlight` denies a delivery whose key is still held, and `FingerprintMismatch` denies a repeat with different content. Both denials travel the refusal path with `IdempotencyRefusalCodes` codes (`idempotency.in-flight`, `idempotency.fingerprint-mismatch`), so `TrySendAsync` returns `Denied` and `SendAsync` throws `LiteBusMessageDeniedException` with that `Code`.

The store takes over an unsettled claim older than the store claim timeout with a fresh token and grants it. Applied keys never expire. The store compares fingerprints only on applied keys. A mismatch needs both fingerprints to be non-null and different. When either side is null, the repeat replays. When the store answers with an outcome outside the four, or with a grant without a token, the shortcut throws `PipelineContractException`.

The contract documents three correctness rules:

1. Claim by insert and treat a primary-key violation as the refusal, because read-then-write loses the race.
2. Claim through the same unit of work the handler writes through, so the key and the change commit together.
3. Fence every grant with the token `TryClaimAsync` returns. Settle with it, and act only while the stored claim still carries it, so a late settle after a takeover is a no-op instead of corrupting the new claimant.

The `IdempotencyCompletionHandler` applies one effect per outcome at `HandlerPriorities.Persistence`, which is inside the reserved window and therefore before an application's commit at `HandlerPriorities.UnitOfWork`. The handler settles only the claim that the same mediation was granted. The shortcut records each granted key in the execution-context data as a `ClaimedIdempotencyKey`. Without that record, the handler returns. A denial, a validation failure, a repeat answered from the store, and a claim denied as in-flight or mismatched therefore settle nothing and cannot delete another delivery's in-flight claim.

| `MediationOutcome` | Effect |
| --- | --- |
| `Succeeded` | `CompleteAsync(key, claimToken, payload)`, where the payload is the serialized result when `ReplayResult` is set and a result was produced |
| `Answered` | `ReleaseAsync(key, claimToken)` when this mediation claimed a key and a later shortcut answered. Nothing is released when the mediation holds no record, which is the repeat-answered-from-the-store case |
| `Denied` / `Invalid` / `Failed` / `Canceled` | `ReleaseAsync(key, claimToken)`, so a transient failure does not turn the retry into a false repeat |

For result commands, the arity-2 shortcut deserializes the recorded payload through `IMessageSerializer`. When a repeated result command has `ReplayResult = false` in its declaration, or its store returned no payload, the shortcut raises `PipelineContractException` naming the fix instead of answering with `default(TResult)`.

The registry does not close an untyped open generic shortcut for a message that declares a result, since an untyped shortcut cannot carry a value. A closed registration of that pair is still a configuration error. An open generic applies to every message it fits, so it is skipped instead of raising an error. One shipped shortcut can therefore cover the void commands in an axis.

`PostgreSqlIdempotencyStore(NpgsqlDataSource, PostgreSqlIdempotencyStoreOptions? = null)` claims by insert with fingerprints, answers a held key `InFlight` instead of granting it, and takes over an unsettled claim older than `ClaimTimeout` with an optimistic `claimed_at` check. `ClaimTimeout` is five minutes by default, measured on the database clock, and it must be positive. Every grant carries a token the store checks before settling. `CompleteAsync` inserts nothing for a key the caller does not hold, and `ReleaseAsync` removes only unsettled rows still carrying the token. Register it with `commands.UsePostgreSqlIdempotencyStore(...)` as a command child module. It brings a versioned schema (component `idempotency`, version 1, `fingerprint` and `claim_token` columns, `completed_at` retention index), a startup initializer, and the `idempotency.postgresql.schema` probe. Each call uses its own auto-commit connection, so the claim is not atomic with the handler change.

`MartenIdempotencyStore(IDocumentStore, MartenIdempotencyStoreOptions? = null, TimeProvider? = null)` keeps claims as `MartenIdempotencyRecord` documents (`Id`, `Fingerprint`, `IsCompleted`, `Payload`, `ClaimToken`, `ClaimedAt`, `CompletedAt`) in its own serializable sessions with the same four outcomes, token fencing, and takeover semantics. Register it with `commands.UseMartenIdempotencyStore(...)`.

`InMemoryIdempotencyStore(TimeProvider? = null, TimeSpan? claimTimeout = null)` implements the same semantics with a `TimeProvider`-driven `ClaimTimeout` (five minutes by default, must be positive), and it ships from the testing package deliberately. It loses all keys on restart and is per-process, so it cannot guarantee idempotency across restarts or processes.

To key a durable envelope by the command key, call `Idempotency.FromCommandKey(string? suffix = null, IdempotencyConflictMode conflictMode = ReturnExisting)`. It returns a `Keyed` idempotency value from the `ClaimedIdempotencyKey` the in-flight command was granted, so a retried command enqueues the same rows. A suffix keys several envelopes distinctly as `{key}:{suffix}`. The suffix is backslash-escaped, so the composed key cannot collide with a different key and suffix pair. A blank suffix throws `ArgumentException` before any ambient read.

Outside a keyed command mediation, the call throws `InvalidOperationException`. `ClaimedIdempotencyKey(string Value, string ClaimToken, bool ReplayResult = false)` is public in `LiteBus.Messaging.Abstractions` so other code can read the granted key and the token that settles it. A blank token throws `ArgumentException`.

### 4.10 Message contracts and serialization

Durable stores persist a stable `(Name, Version)` pair instead of an assembly-qualified type name, so CLR types can be renamed or moved.

```csharp
public interface IContractWriter
{
    IContractWriter Register<TMessage>(string name, int version = 1) where TMessage : notnull;
    IContractWriter Register(Type messageType, string name, int version = 1);
    IContractWriter AddFromAssembly(Assembly assembly);   // [RequiresUnreferencedCode]
}

public interface IContractReader
{
    MessageContract GetContract(Type messageType);
    Type GetMessageType(string contractName, int contractVersion);
    MessageContract? TryGetContract(Type messageType);
    Type? TryGetMessageType(string contractName, int contractVersion);
}
```

Two extension methods, `IContractWriter.RegisterFromAssembly(Assembly)` and `IMessageContractRegistry.RegisterFromAssembly(Assembly)`, both delegate to `AddFromAssembly`.

```csharp
// Explicit registration (preferred; analyzer LB1017 nudges toward it)
liteBus.AddInbox(inbox =>
{
    inbox.Contracts.Register<ProcessPaymentCommand>("payments.process");        // version 1
    inbox.Contracts.Register<ProcessPaymentCommandV2>("payments.process", 2);
    inbox.Contracts.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly);
    inbox.UseInMemoryStorage();
    inbox.UseInProcessDispatch();
});

// Attribute form
[MessageContract("payments.process", 2)]
public sealed record ProcessPaymentCommandV2(Guid PaymentId, decimal Amount, string Currency) : ICommand;
```

Registrations behave as follows:

| Condition | Result |
| --- | --- |
| An open generic message type passed to `Register` | `ArgumentException`. Register each closed shape with its own name and version. |
| `version <= 0` | `ArgumentOutOfRangeException` |
| Blank `name` | `ArgumentException` |
| Same type registered again with different values, or a name/version pair reused for another type | `MessageContractAlreadyRegisteredException` |
| Explicit registration disagreeing with `[MessageContract]` on the same type | `MessageContractMismatchException` naming both sides |
| Lookup for an unregistered type | `MessageContractNotRegisteredException(Type)` |
| Lookup for an unregistered `(name, version)` | `MessageContractNotRegisteredException(string, int)` |

`MessageContractBuilder` is the deferred writer used by composite module builders (`InboxModuleBuilder.Contracts`, `OutboxModuleBuilder.Contracts`). It records registrations as data (`HasRegistrations`) and replays them with `ApplyTo(IMessageContractRegistry)` once the live registry exists.

`IMessageContractResolver` selects which CLR type contract lookup uses during accept/enqueue. The interface declares the single method `Type ResolveContractType(Type declaredType, object message)`. When no resolver is registered, contract lookup uses the runtime instance type. Register `DeclaredTypeMessageContractResolver` to use the declared parameter type instead:

```csharp
services.AddSingleton<IMessageContractResolver, DeclaredTypeMessageContractResolver>();
```

Serialization uses one interface:

```csharp
public interface IMessageSerializer
{
    Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;
    Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default);
}
```

`SystemTextJsonMessageSerializer` uses `new JsonSerializerOptions(JsonSerializerDefaults.Web)` unless options are supplied to the constructor. It serializes with `message.GetType()`, so derived shapes round-trip. It wraps `JsonException` / `NotSupportedException` in `MessageSerializationException` with `Operation` set to `"serialized"` or `"deserialized"`. A `null` deserialization result also raises a `MessageSerializationException`. Replace the default with your own registration after `AddLiteBus`, or register a custom `IMessageSerializer` implementation.

### 4.11 Payload encryption

```csharp
public interface IPayloadEncryptor
{
    Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default);
    Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default);
}

public interface IContextualPayloadEncryptor : IPayloadEncryptor
{
    Task<string> EncryptAsync(string plaintext, PayloadProtectionContext context, CancellationToken ct = default);
    Task<string> DecryptAsync(string ciphertext, PayloadProtectionContext context, CancellationToken ct = default);
}
```

Register per axis so the inbox and outbox never share a key by accident:

```csharp
liteBus.AddInbox(inbox => inbox.UsePayloadEncryption(new AesGcmPayloadEncryptor(inboxKey)) /* ... */);
liteBus.AddOutbox(outbox => outbox.UsePayloadEncryption(new AesGcmPayloadEncryptor(outboxKey)) /* ... */);
```

The module wraps the encryptor in `IInboxPayloadProtector` / `IOutboxPayloadProtector` (both derive from `IPayloadEncryptor`). The protectors also implement `IContextualPayloadEncryptor`. They forward the contextual overloads to the wrapped encryptor when it implements them, and they fall back to the plain contract otherwise. `PayloadProtection.ProtectAsync` / `UnprotectAsync` are the static methods that the envelope factories and every dispatcher call, each in `(string payload, IPayloadEncryptor? encryptor, CancellationToken)` and `(string payload, IPayloadEncryptor? encryptor, PayloadProtectionContext? context, CancellationToken)` forms. They throw `ArgumentNullException` on a null payload.

When the encryptor also implements `IContextualPayloadEncryptor` and a context is supplied, the call uses the contextual overload, which binds `MessageId`, `ContractName`, `ContractVersion`, `TenantId` and `Axis` (`"inbox"` or `"outbox"`) as authenticated metadata. A `null` encryptor passes the payload through unchanged. Both methods honour cancellation before doing any work.

### 4.12 Inbox

The inbox accepts a command durably now and executes it later through a processor. Each envelope executes exactly once, using at-least-once dispatch.

```csharp
public interface IInbox
{
    Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(InboxAcceptItem<TMessage> item, CancellationToken ct = default)
        where TMessage : notnull;

    Task<InboxReceipt<TMessage>> AcceptAsync<TMessage>(TMessage message, CancellationToken ct = default)
        where TMessage : notnull;                         // default interface method

    Task<InboxReceipt> AcceptAsync(InboxAcceptItem item, CancellationToken ct = default);

    Task<IReadOnlyList<InboxReceipt>> AcceptBatchAsync(IReadOnlyList<InboxAcceptItem> items, CancellationToken ct = default);
}
```

`ITransactionalInbox` has the same shape without the untyped single accept. `ITransactionalInbox<TContext>` adds the EF Core flavour, where rows are written when the scoped context saves changes.

Compose the inbox as follows:

```csharp
liteBus.AddInbox(inbox =>
{
    inbox.Contracts.Register<ProcessPaymentCommand>("payments.process");
    inbox.UseInMemoryStorage();          // or UsePostgreSqlStorage / UseEntityFrameworkCoreStorage
    inbox.UseInProcessDispatch();        // or Use{Amqp,Kafka,AwsSqs,AzureServiceBus,InMemory}Dispatch
    inbox.EnableInboxProcessor(host =>
    {
        host.PollInterval = TimeSpan.FromMilliseconds(250);
        host.UseAdaptivePolling = true;
    });
    inbox.UseProcessorOptions(new InboxProcessorOptions
    {
        BatchSize = 50,
        DispatcherConcurrency = 8,
        LeaseDuration = TimeSpan.FromMinutes(2),
        LeaseHeartbeatInterval = TimeSpan.FromSeconds(30),
        Retry = new RetryOptions { MaxAttempts = 8, InitialDelay = TimeSpan.FromSeconds(2) }
    });
    inbox.EnableCleanup(cleanup =>
    {
        cleanup.Retention = TimeSpan.FromDays(7);
        cleanup.Interval = TimeSpan.FromHours(6);
    });
});
```

Accept commands as follows:

```csharp
// Simplest form
var receipt = await inbox.AcceptAsync(new ProcessPaymentCommand(paymentId, amount), ct);

// Scheduled, idempotent, correlated, tenant-scoped
var item = InboxAcceptItem<ProcessPaymentCommand>.From(command) with
{
    Metadata = InboxAcceptMetadata.Immediate with
    {
        Identity = new MessageIdentity.Supplied(messageId),
        Idempotency = new Idempotency.Keyed($"payment:{paymentId}", IdempotencyConflictMode.Strict),
        Visibility = new MessageVisibility.After(TimeSpan.FromMinutes(5)),
        Trace = new MessageTrace.Distributed(correlationId, causationId, traceParentJson),
        Tenant = new TenantScope.Isolated(tenantId)
    }
};
var scheduled = await inbox.AcceptAsync(item, ct);

// Heterogeneous batch
var receipts = await inbox.AcceptBatchAsync(
[
    InboxAcceptItem.From(commandA),
    InboxAcceptItem.From(commandB, typeof(ProcessPaymentCommand)),
], ct);

if (receipt.Outcome == InboxAcceptOutcome.AlreadyAccepted)
{
    // Duplicate collapsed onto the original row; receipt carries the original id, trace and tenant.
}
```

`InboxAcceptItem.From` has three overloads. `(object message, InboxAcceptMetadata?)` leaves `MessageType` null, so each entry uses its runtime type for contract lookup. `(object message, Type messageType, InboxAcceptMetadata?)` sets an explicit lookup type for heterogeneous batches. `From<TMessage>(InboxAcceptItem<TMessage>)` converts a typed item and stamps `MessageType` from `item.Message.GetType()`.

Under `IdempotencyConflictMode.Strict` an identical resubmission (same id, contract name and version, payload, idempotency key, correlation and causation ids, normalized tenant) returns the stored receipt instead of throwing. Only a differing duplicate throws `IdempotencyConflictException`. On every inbox store, a message identifier already stored for another tenant makes the store throw `IdempotencyConflictException` regardless of conflict mode.

`InboxAcceptItem<TMessage>.FromAmbient(message)` stamps the trace of the message being handled onto the accepted command through `MessageTrace.FromAmbient()`. It selects `Distributed` when an ambient trace context is present alongside correlation and causation, `Workflow` when correlation and causation are known, `Correlated` when only correlation is known, and `None` otherwise. Correlation is the ambient correlation id, or the incoming message id when the ambient id is absent. Causation is the incoming message id, or the ambient causation id when no message id is stored.

This stamping is an explicit opt-in. Nothing reads ambient trace unless the caller opts in, and the tenant stays `Unscoped`. `InboxModuleBuilder.UseAmbientTrace()` applies the same stamping inside the envelope factory to every accepted envelope whose item trace is `MessageTrace.None`. An explicit trace always takes precedence. Outside a mediation, the envelope stays untraced.

The `InboxEnvelopeFactory` creates envelopes in this order:

1. Validate the instance is assignable to the declared type (`ArgumentException` otherwise).
2. Run `InboxCommandMessageGuard.EnsureVoidCommand`, which throws `InvalidOperationException` for an `ICommand<TResult>`. Analyzer `LB1004` catches that case at compile time.
3. Resolve the contract.
4. Stamp `CreatedAt` from `TimeProvider`.
5. Resolve the message id.
6. Resolve the tenant id.
7. Serialize.
8. Optionally encrypt with a `PayloadProtectionContext`.
9. Project trace columns.
10. Build the `Pending` envelope with `AttemptCount = 0`.

`IInboxEnvelopeFactory` exposes `CreateAsync(InboxAcceptItem item, CancellationToken)` and `CreateBatchAsync(IReadOnlyList<InboxAcceptItem> items, CancellationToken)`. The batch form returns envelopes in input order. It is the one creation path shared by `IInbox`, `ITransactionalInbox`, and EF Core staging. `InboxEnvelopeFactory` takes `(IContractReader contractRegistry, IMessageSerializer messageSerializer, TimeProvider clock, IInboxPayloadProtector? payloadProtector = null, IMessageContractResolver? contractTypeResolver = null, InboxEnvelopeOptions? envelopeOptions = null)`. Without a resolver, the runtime message type drives contract lookup. The protection context carries `Axis = "inbox"`, and the options carry the `UseAmbientTrace` flag. A missing options instance behaves as disabled.

Public `InboxAcceptanceService` (namespace `LiteBus.Inbox.Abstractions`) is the shared acceptance pipeline used by writer and transactional writer implementations. It is constructed with an `IInboxEnvelopeFactory`. Its `AcceptAsync<TMessage>`, `AcceptAsync`, and `AcceptBatchAsync` take persistence delegates that persist or stage the created envelopes and return receipts. When a batch result count mismatches the input count, the call throws `InvalidOperationException`.

The operator API on `IInboxManager` behaves as follows:

| Method | Return | Behaviour |
| --- | --- | --- |
| `QueryAsync(filter, pageRequest, ct)` | `Task<InboxMessagePage>` | A keyset page. It throws `ArgumentException` on an invalid cursor and `ArgumentOutOfRangeException` when `PageSize` is below 1 or above `InboxMessagePageRequest.MaxPageSize`. |
| `GetMessageAsync(messageId, ct)` | `Task<InboxEnvelope?>` | A single-row query with `PageSize = 1`. |
| `RequeueDeadLettersAsync(ct)` | `Task<int>` | Pages dead letters 200 at a time and requeues each page. |
| `RequeueAsync(messageIds, ct)` | `Task<RequeueResult>` | Empty input short-circuits to `(0, 0)`. |
| `PurgeAsync(filter, confirm = false, ct)` | `Task<int>` | It throws `InboxManagementException` for an unrestricted filter without `confirm: true`. |
| `GetStatusCountsAsync(ct)` | `Task<IReadOnlyDictionary<InboxStatus, int>>` | The queue depth by status. |
| `GetSchemaInfoAsync(ct)` | `Task<StoreSchemaInfo>` | The expected schema version versus the recorded one. |
| `GetRetentionStatusAsync(ct)` | `Task<RetentionRunStatus>` | A snapshot from `InboxRetentionCoordinator`. |
| `RunRetentionPurgeAsync(ct)` | `Task<int>` | An immediate retention pass. It returns 0 when `Retention` is null or non-positive. It records success or failure on the coordinator and rethrows on failure. |

`InboxRetentionCoordinator` (constructed with `InboxCleanupHostOptions`) exposes `RecordSuccess(int deletedCount, DateTimeOffset runAt)`, `RecordFailure(string errorMessage, DateTimeOffset runAt)`, and `GetStatus()`. `GetStatus` returns the `RetentionRunStatus` snapshot that `GetRetentionStatusAsync` and `RunRetentionPurgeAsync` report.

The store roles are `IInboxStore` (`AddAsync`, `AddBatchAsync`), `IInboxLeaseStore` (`LeasePendingAsync` plus inherited `RenewLeaseAsync` and `ReleaseLeaseAsync`, whose default implementation releases nothing and returns `false`), `IInboxStateWriter` (`PersistAsync`, which throws `InvalidOperationException` for an envelope that is not `Completed`, `Failed`, or `DeadLettered`), `IInboxDeadLetterStore` (`RequeueAsync(IReadOnlyList<Guid>)` and a default single-id overload), `IInboxRetentionStore` (`DeleteCompletedOlderThanAsync`), `IInboxDiagnosticsStore` (`GetStatusCountsAsync`, `GetSchemaInfoAsync`), `IInboxMessageQuery` (`QueryAsync`) and `IInboxPurgeStore` (`PurgeAsync`). The composites are `IInboxProcessingStore` (lease plus state writer) and `IInboxOperationsStore` (dead letter plus retention plus diagnostics plus query plus purge). `ITransactionalInboxStore : IInboxStore` marks a writer that joins the caller's transaction.

`AddAsync` returns `InboxAppendResult(InboxEnvelope Envelope, InboxAcceptOutcome Outcome)`: the envelope that remains the stored source of truth, and whether the append inserted it or resolved an existing envelope. `AddBatchAsync` returns one result per input in order. Two extensions exist. `IInboxStore.EnqueueAsync(envelope, ct)` aliases `AddAsync`, and `IInboxDeadLetterStore.RequeueAsync(IReadOnlyList<string>, ct)` parses string ids and throws `ArgumentException` on a malformed value.

The processor dead-letters an envelope when `AttemptCount` reaches `Retry.MaxAttempts`, when the dispatch exception is not retryable, or when the application classifier `RetryOptions.IsPermanentFailure` recognizes the failure. A dispatch exception is not retryable for a missing handler, multiple handlers, a guard denial, or a validation failure, found through `MediationExceptionFilters.IsRetryableDispatchException` including the inner exception chain. The classifier is `null` by default, in which case every failure the built-in set allows follows the retry schedule. Set it to dead-letter on the first matching failure regardless of `MaxAttempts`. The processor does not call it for a cancellation that it treats as a shutdown. A classifier that throws is treated as returning `false`, so a faulty predicate keeps the retry schedule instead of losing the message.

**Exceptions.** `InboxDispatchException`, `InboxIngressException`, `InboxStorageException`, `InboxManagementException`, `IdempotencyConflictException`.

### 4.13 Outbox

The outbox persists an event inside the caller's transaction and publishes it later.

```csharp
public interface IOutbox
{
    Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(OutboxEnqueueItem<TEvent> item, CancellationToken ct = default)
        where TEvent : notnull;

    Task<OutboxReceipt<TEvent>> EnqueueAsync<TEvent>(TEvent message, CancellationToken ct = default)
        where TEvent : notnull;                        // default interface method

    Task<OutboxReceipt> EnqueueAsync(OutboxEnqueueItem item, CancellationToken ct = default);

    Task<IReadOnlyList<OutboxReceipt>> EnqueueBatchAsync(IReadOnlyList<OutboxEnqueueItem> items, CancellationToken ct = default);
}
```

`ITransactionalOutbox` and `ITransactionalOutbox<TContext>` mirror that shape for transactional writes.

```csharp
liteBus.AddOutbox(outbox =>
{
    outbox.Contracts.Register<PaymentProcessed>("payments.processed");
    outbox.UseInMemoryStorage();
    outbox.UseInProcessDispatch();       // or a broker dispatcher
    outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(250));
    outbox.EnableCleanup(cleanup => cleanup.Retention = TimeSpan.FromDays(3));
});

// In a command handler
await _outbox.EnqueueAsync(new PaymentProcessed(paymentId, amount), cancellationToken);

// With an explicit publication target and idempotency key
await _outbox.EnqueueAsync(
    OutboxEnqueueItem<PaymentProcessed>.WithTopic(evt, "payments.v1") with
    {
        Metadata = OutboxEnqueueMetadata.Immediate with
        {
            Target = new PublicationTarget.Exchange("payments"),
            Idempotency = new Idempotency.Keyed($"payment-processed:{paymentId}")
        }
    },
    cancellationToken);
```

The `OutboxEnvelopeFactory` creates envelopes in this order:

1. Validate the instance is assignable to the declared type (`ArgumentException` otherwise).
2. Resolve the contract through the optional `IMessageContractResolver` (runtime type when omitted).
3. Stamp `CreatedAt` from `TimeProvider`.
4. Resolve the message id.
5. Resolve the tenant id.
6. Serialize.
7. Encrypt with a `PayloadProtectionContext` carrying `Axis = "outbox"`.
8. Project trace columns.
9. Validate the distributed trace context is valid JSON text (`ArgumentException` naming the metadata otherwise, since the outbox stores it as JSON).
10. Build the `Pending` envelope with `AttemptCount = 0`.

`Topic`/`Exchange`/`Queue` targets persist their `Name` when it is non-blank, and null otherwise. `ContractDefault` always persists null. The factory takes optional `IMessageReader?` and `OutboxEnqueueOptions?` constructor parameters. A factory constructed directly without the options behaves as if both flags were false.

Public `OutboxWriterCore` (namespace `LiteBus.Outbox.Abstractions`) is the shared enqueue pipeline used by writer and transactional writer implementations. It is constructed with an `IOutboxEnvelopeFactory`. Its `EnqueueAsync<TEvent>`, `EnqueueAsync`, and `EnqueueBatchAsync` take persistence delegates, each in a single-envelope and a batch form. The shipped writers pass the batch form, so a single enqueue persists the whole per-handler fan-out in one `AddBatchAsync` call. When a batch result count mismatches the input count, the call throws `InvalidOperationException`.

Writers expand every item through `IOutboxEnvelopeFactory.CreateManyAsync`. Its default interface methods return the single `CreateAsync` envelope, so custom factories inherit broadcast behavior. The shipped factory's `CreateBatchAsync` fans out per item through the same per-handler path and flattens in item order, then handler-name order within each item. Overrides of it apply only to direct factory calls. A single enqueue returns the first per-handler receipt in handler-name order. Batch receipts hold one receipt per envelope in item order.

`OutboxEnqueueItem<TEvent>.FromAmbient(message)` stamps the trace of the message being handled onto the enqueued event through `MessageTrace.FromAmbient()`, with the same variant precedence as the inbox method. It is an explicit opt-in, and the tenant stays `Unscoped`. `OutboxModuleBuilder.UseAmbientTrace()` applies the same stamping inside the envelope factory to every enqueued envelope whose item trace is `MessageTrace.None`.

`OutboxModuleBuilder.EnablePerHandlerDelivery()` writes one envelope per subscribed main handler of an event. It orders envelopes by handler full name and records the handler on `OutboxEnvelope.Handler` (null means broadcast). The in-process dispatcher publishes each envelope to that handler alone through a main-handler-only predicate, so a failing reaction retries, dead-letters and is replayed on its own while the others publish. Pre, post, error and completion observers still run on a targeted dispatch.

Direct and indirect main handlers count, deduplicated by full name. When zero main handlers are subscribed at enqueue, the call throws `NoHandlerFoundException`. A handler removed before dispatch falls back to the dispatch `RequireHandler` option. From supplied identity the factory derives deterministic per-handler ids (SHA-256 of `{id}:{handler}`, first 16 bytes), and from keyed idempotency it derives `{key}:{handler}` preserving the conflict mode, so a retried enqueue resolves to the same rows. A handler added after the enqueue does not receive events already enqueued. Composing per-handler delivery with a transport dispatcher throws `DurableStorageConfigurationException` at build unless `AllowPerHandlerDeliveryWithTransportDispatch()` acknowledges the duplicate broker publication, since broker dispatchers publish the full event per envelope.

`OutboxModuleBuilder.StageCollectedEvents()` registers the scoped `IDomainEventCollector` and an open-generic completion handler at `HandlerPriorities.Persistence`. The handler drains the collector into `ITransactionalOutbox` when the mediation succeeded, before the unit-of-work commit at `HandlerPriorities.UnitOfWork`. A handler records consequences with `Collect` instead of publishing inline. An empty drain never touches the outbox. When collected events exist without a registered scoped `ITransactionalOutbox`, the drain throws `InvalidOperationException` naming `StageCollectedEvents` and listing the registrations that provide one (ambient PostgreSQL provider, EF Core interceptor adapter, Marten writer, test double). Staged envelopes carry stamped ambient trace when the outbox opted into it.

`LiteBus.Outbox.Storage.Marten` provides `MartenTransactionalOutbox`, an `ITransactionalOutbox` that queues the schema-owned outbox insert on the current Marten `IDocumentSession` with `QueueSqlCommand`, so documents and outbox rows commit together in one `SaveChangesAsync`. The application registers Marten itself with `AddMarten` against the same database as the outbox. LiteBus does not register Marten. Register it as follows:

```csharp
outbox.UsePostgreSqlStorage(postgreSql => postgreSql
    .UseConnectionString(connectionString)
    .UseMartenTransactionalOutbox());
```

`UseMartenTransactionalOutbox()` cannot be combined with `EnableAmbientTransactionProvider()`. Both register `ITransactionalOutbox`, so module build throws `OutboxPostgreSqlStorageConfigurationException` when both are configured. Contract resolution, serialization, and payload encryption follow the same path as `IOutbox`. Only the write goes through the session, and disposing the session without saving stores nothing.

The writer resolves duplicates before queueing. It checks rows already staged in the session and committed rows read on a separate connection, because the queued insert ends with `ON CONFLICT DO NOTHING` and reports nothing about skipped rows. `ReturnExisting` (the default) returns the stored receipt without queueing a second row, while `Strict` returns the stored receipt for an identical resubmission and throws `IdempotencyConflictException` when the conflicting submission differs. Staging the same message twice in one session queues one row. The second attempt returns `AlreadyEnqueued`. `ON CONFLICT DO NOTHING` absorbs a concurrent insert of the same id or key that wins the race, so the receipt then reports `Enqueued` though the other row was kept.

`PostgreSqlOutboxSchema.CreateInsertStatement(envelope, options?)` builds the parameterized insert for one envelope against the current schema version, with the same columns and value conversions `PostgreSqlOutboxStore` writes. It returns a `PostgreSqlOutboxInsertStatement` whose `Sql` uses Npgsql positional parameters (`$1` through `$n`) and whose `Values` holds the values in placeholder order with `DBNull.Value` for every absent value. `FormatSql(placeholder)` replaces each positional marker with one placeholder character, keeping explicit casts (Marten `QueueSqlCommand` uses `?`).

The statement ends with `ON CONFLICT DO NOTHING` and has no `RETURNING` clause, so idempotency resolution stays the caller's job. Callers must not edit the returned statement. A schema change needs an update to `CreateInsertStatement` in one place.

`IOutboxManager` mirrors `IInboxManager` with `OutboxMessageFilter`, `OutboxMessagePage`, `OutboxStatus`, and `RunRetentionPurgeAsync` calling `DeletePublishedOlderThanAsync`, except for one deliberate difference. The outbox `RunRetentionPurgeAsync` throws `OutboxManagementException` when retention is null or non-positive, while the inbox version returns 0. It adds `GetFailureSummaryAsync`. It returns one `OutboxFailureSummary` row per contract name and version that has at least one `Failed` or `DeadLettered` message, ordered by `LastFailedAt` descending and then by contract name, or an empty list when nothing is failing. Each row carries `ContractName`, `ContractVersion`, `FailedCount` (awaiting retry), `DeadLetteredCount` (needing an operator replay), `LastError` and `LastErrorType` from the most recently attempted failing message of the contract, and `LastFailedAt` (its `LastAttemptedAt`, `null` when none has been attempted). Dead-letter triggers and the `RetryOptions.IsPermanentFailure` classifier work as described for the inbox above.

Store roles mirror the inbox. `IOutboxStore` returns `OutboxAppendResult(OutboxEnvelope Envelope, OutboxEnqueueOutcome Outcome)` from `AddAsync` and one result per input in order from `AddBatchAsync`. On every outbox store, a message id already stored for another tenant makes the store throw `IdempotencyConflictException` regardless of conflict mode. The extension `EnqueueAsync(envelope, ct)` aliases `AddAsync`. The remaining roles are `IOutboxLeaseStore`, `IOutboxStateWriter`, `IOutboxDeadLetterStore` (`RequeueAsync(IReadOnlyList<Guid>)` plus a default single-id overload, plus a `RequeueAsync(IReadOnlyList<string>, ct)` extension that returns `(0, 0)` for empty input and throws `ArgumentException` naming `messageIds` for a value that does not parse as a `Guid`), `IOutboxRetentionStore` (`DeletePublishedOlderThanAsync(DateTimeOffset olderThan, ...)`, which deletes rows with `COALESCE(published_at, created_at)` strictly before `olderThan`, so retention uses publication time when set), `IOutboxDiagnosticsStore` (`GetStatusCountsAsync`, `GetFailureSummaryAsync`, `GetSchemaInfoAsync`), `IOutboxMessageQuery`, `IOutboxPurgeStore`, `IOutboxProcessingStore`, `IOutboxOperationsStore` and `ITransactionalOutboxStore`.

**Exceptions.** `LiteBusDispatchException` (raised by `EventOutboxDispatcher` around any non-dispatch failure with a message naming the contract), `OutboxManagementException`, `IdempotencyConflictException`.

### 4.14 Durable processors

**Type:** `PipelinedMessageProcessor<TEnvelope, TOptions>` (internal, shared), specialized by `PipelinedInboxProcessor` and `PipelinedOutboxProcessor` through `IPipelinedMessageProcessorOperations<TEnvelope, TOptions>`.

`IMessageProcessor.ProcessPendingAsync(CancellationToken cancellationToken = default)` runs one pass and returns a `ProcessorPassResult`. `IInboxProcessor` and `IOutboxProcessor` extend it without adding members. They document at-least-once execution, so dispatch targets, handlers, and downstream consumers must be idempotent.

One `ProcessPendingAsync` pass runs as follows:

1. Start the pass activity (`inbox.processor.pass` / `outbox.processor.pass`) and a `ProcessorPassStopwatch`. The stopwatch is a readonly struct. `StartNew()` captures `Environment.TickCount64`, and `GetElapsedTime()` returns the elapsed `TimeSpan` without allocating a `Stopwatch`.
2. `LeasePendingAsync(leaseOwner, options, now, ct)` claims up to `BatchSize` rows in `CreatedAt` order, optionally filtered by `TenantId`. With a null filter the lease spans tenants, while a blank filter matches unscoped rows only. A row is claimable when it is `Pending` or `Failed` and its `VisibleAfter` is null or already past, when it is `Processing` / `Publishing` with a `LeaseExpiresAt` at or before `now`, or when it is `Processing` / `Publishing` with a null `LeaseExpiresAt` and a `CreatedAt` older than `now - LeaseDuration` (the stale cutoff). Each claimed row receives `LeaseOwner`, an incremented `LeaseGeneration`, `LeaseExpiresAt = now + LeaseDuration`, and an incremented `AttemptCount`. The PostgreSQL, Entity Framework Core and in-memory inbox stores reject a zero or negative `LeaseDuration` with `ArgumentOutOfRangeException`. The in-memory outbox store substitutes its `DefaultLeaseDuration` for a zero duration and rejects only a negative one. The operations adapter forwards to the store-level `IInboxLeaseStore.LeasePendingAsync(InboxLeaseRequest request, CancellationToken cancellationToken = default)` (`IOutboxLeaseStore` / `OutboxLeaseRequest` on the outbox side). The request is a sealed record with required `BatchSize`, `LeaseOwner`, `Now` and `LeaseDuration`, and an optional `TenantId`.
3. Log the batch (`EventId 3001`) and record `leases_acquired`. An empty batch finalizes the pass immediately.
4. Start one worker task per envelope, limited by a `SemaphoreSlim(DispatcherConcurrency)`.
5. Each worker runs under `ProcessorLeaseHeartbeat.RunWithHeartbeatAsync`. It renews once before acquiring its `DispatcherConcurrency` slot, then renews every `LeaseHeartbeatInterval` until the operation ends. A zero or negative `LeaseHeartbeatInterval` disables renewal, and dispatch then runs directly on the pass token. When a renewal fails (a `false` return or a store exception), the heartbeat logs `EventId 3002`, records `lease_lost`, and cancels the dispatch token. A dispatcher that observes the cancellation returns no outcome. The row stays untouched with no persist and no release attempted, so the pass under-reports that envelope. On any other path out of the cancelled heartbeat (an up-front renewal failure before dispatch runs, cancellation while waiting for a worker slot, or a dispatcher that ignored the cancellation and returned normally), the worker attempts `PersistLeaseLossOutcomeAsync`. It builds retryable `Failed` with `MessageProcessorDiagnostics.LeaseLostDuringProcessingError` ("Lease lost during processing; scheduling retry.") and records it only when the store accepts the write. The worker logs and counts a skipped persist without recording the envelope, so the pass under-reports it too.
6. To dispatch, start the `inbox.processor.message` / `outbox.processor.message` activity parented to the envelope `TraceContext` and tagged with `litebus.message_id`. Then run `IProcessorEnvelopeHook.BeforeDispatchAsync`, `PrepareDispatchScope`, `ShouldDispatch`, and `IInboxDispatcher.DispatchAsync` / `IOutboxDispatcher.DispatchAsync`. Then record the dispatch-duration histogram and transition to `AsCompleted(now)` / `AsPublished(now)` with the processor clock. `ShouldDispatch` asks every hook, even after one declines. When any hook returns `false`, the processor completes or publishes the envelope without dispatching.
7. Map failures as follows. A dispatch cancelled by shutdown (`MediationExceptionFilters.IsCallerCancellation`, which also sees a cancellation that a dispatcher wrapped in its own exception type) returns no outcome. No attempt is consumed, no error is recorded, and visibility does not move. A handler-internal `OperationCanceledException` raised while the pass token is still live (for example a dependency timeout) follows the normal failure path instead. Otherwise, format the error as a `MessageFailure` and log `DispatchFailed`. Then dead-letter when `AttemptCount >= Retry.MaxAttempts`, when `MediationExceptionFilters.IsRetryableDispatchException(exception)` is false, or when the `RetryOptions.IsPermanentFailure` classifier recognizes the failure. That filter walks the inner exception chain and excludes `NoHandlerFoundException`, `MultipleHandlerFoundException`, and refusals. A classifier that throws is treated as not permanent and logged. If none of those hold, mark the envelope failed with `VisibleAfter = now + Retry.CalculateDelay(AttemptCount)`.
8. `AfterDispatchAsync` hooks run on the persist token (`HonorShutdownTokenOnPersist ? shutdown : CancellationToken.None`) while the lease is still held. With the default `false`, a hook that never returns delays shutdown instead of risking a duplicate execution or publish. A hook failure follows the resolved `HookFailurePolicy`. Under `CompleteDespiteHookFailure` the processor logs and persists the success, while under `DeadLetter` it dead-letters the source envelope. The policy is nullable and resolves at build. An explicit value always takes precedence. Otherwise the inbox resolves `DeadLetter` and the outbox resolves the dispatcher recommendation (`CompleteDespiteHookFailure` for transport dispatchers, `DeadLetter` for in-process dispatch). `AbandonDispatchScope` runs for every hook when the after-dispatch sequence stops on a failure.
9. Persist the terminal state through `PersistAsync`, using the shutdown token when `HonorShutdownTokenOnPersist` is true. A dispatch cancelled by shutdown instead releases its lease through `ILeaseRenewable.ReleaseLeaseAsync` on its own 5-second budget. The row returns to `Pending` (or `Failed` when it already carries an error) with the lease cleared, the attempt count decremented, and visibility unchanged. The release is logged as `EventId 3011`, or `3012` when the release fails, in which case lease expiry reclaims the row with the attempt counted. A store using the default `ReleaseLeaseAsync` implementation releases nothing, and the row is reclaimed on lease expiry. When a `PersistResult` has `SkippedCount > 0`, the pass records `persist_skipped` / `persist_rejected`. When persistence throws after a completed dispatch, the pass logs `EventId 3003`, records `persist_failed`, and continues with the remaining envelopes. It does not abort the batch. The pass likewise logs and counts a lease-loss persist failure without aborting the batch.
10. `FinalizePass` builds the `ProcessorPassResult` and records telemetry. A pass cancelled by host shutdown rethrows `OperationCanceledException` once its workers have settled.

`IProcessorPassRecorder<in TEnvelope>` collects post-transition envelopes from dispatch workers through `RecordSucceeded`, `RecordFailed` and `RecordDeadLettered`. `ProcessorPassAccumulator<TEnvelope>` is the sealed non-thread-safe implementation (one instance per pass) with `Updates`, `SucceededCount`, `FailedCount`, `DeadLetteredCount`, `TotalCount` and `ToResult(int leasedCount, TimeSpan elapsed)`. `ConcurrentProcessorPassAccumulator<TEnvelope>` exposes the same surface but is thread-safe. It uses a lock-guarded list plus `Interlocked` increments, `Updates` returns a snapshot copy, and counts are read through `Volatile.Read`. The pipelined pass records into the concurrent accumulator. It creates an empty one even for an empty batch.

The `ProcessorBackgroundService<TProcessor>` loop validates host options and exits when `Enabled` is false. It applies `StartupDelay` through the injected `TimeProvider` (a drain request skips the remaining delay), then it loops. Each iteration runs `WaitIfPausedAsync`, then one pass, then `SignalPassComplete` in a `finally` block, so a failed pass still releases the gate. It then waits on `IProcessorWorkSignal.WaitForWorkOrDelayAsync(PollInterval, token)`, unless the pass leased a full `BatchSize` with `UseAdaptivePolling` on. With a `PollInterval` at or below zero, the loop never waits.

A drain request also interrupts the poll wait so the final pass starts immediately. The drain pass runs on the host stopping token, so draining never cancels dispatch. A drained pass completes and persists in-flight messages normally. When a pass throws unexpectedly, the loop records a loop error, logs, and continues. A failed pass still waits before the next lease. The drain pass instead reports the failure to the drain waiter and rethrows. Work-signal failures are also recorded before the loop continues, so a broken LISTEN/NOTIFY connection cannot kill the loop.

The control surface is one interface per axis:

```csharp
public interface IInboxProcessorControl   // and IOutboxProcessorControl
{
    ProcessorState State { get; }
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
```

`PauseAsync` sets the state to `Paused` and awaits the in-flight pass. Repeated pauses are idempotent, and cancelling the wait does not resume the processor. `ResumeAsync` releases the gate and is likewise idempotent.

`DrainAsync` requests one final pass, then the loop exits. Concurrent drains await the same pass. Draining releases a paused loop so the drain pass runs. On timeout, the call throws `TimeoutException` and stops only that caller's wait. `Timeout.InfiniteTimeSpan` is accepted, and any other negative value throws `ArgumentOutOfRangeException`.

Calling pause or resume after drain started throws `InvalidOperationException`. The implementations also implement `IProcessorBackgroundControl` (`DrainRequestedToken`, `IsDraining`, `WaitIfPausedAsync`, `SignalPassComplete`, `SignalDrainComplete`) and `IAsyncDisposable`.

`IProcessorWorkSignal` extends `IAsyncDisposable`. The container that created the signal owns its disposal. Its method is `IProcessorWorkSignal.WaitForWorkOrDelayAsync(TimeSpan pollInterval, CancellationToken)`. `IInboxWorkSignal` / `IOutboxWorkSignal` are axis markers.

`InboxPollingWorkSignal` / `OutboxPollingWorkSignal` only call `Task.Delay`, returning immediately for a non-positive interval. `PostgreSqlInboxWorkSignal` / `PostgreSqlOutboxWorkSignal` use LISTEN/NOTIFY on a dedicated connection with a quoted channel name when `UseListenNotify` is enabled, reconnect 1 second after a broken listener connection, and are disposed with the container. Disposal is idempotent, and a wait after disposal falls back to the poll delay.

Processor hooks use these contracts:

```csharp
public interface IProcessorEnvelopeHook
{
    Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default);
    void PrepareDispatchScope(IProcessorEnvelope envelope) { }
    bool ShouldDispatch(IProcessorEnvelope envelope) => true;
    void AbandonDispatchScope(IProcessorEnvelope envelope) { }
    Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default);
}

public interface IProcessorEnvelope
{
    Guid MessageId { get; }
    string ContractName { get; }
    int ContractVersion { get; }
    string? CorrelationId { get; }
    string? CausationId { get; }
    string? TenantId { get; }
}
```

`IProcessorEnvelope`, `IProcessorEnvelopeHook` and `ProcessorHookRunner` are declared in the `LiteBus.Messaging.Abstractions.Processing` namespace. The runner resolves hooks from `IEnumerable<IProcessorEnvelopeHook>`. It falls back to a single `IProcessorEnvelopeHook` registration, then to none. The runner combines `ShouldDispatch` with `&=` across hooks, and it asks every hook even after one declines, because a hook may hold state that it releases in `AbandonDispatchScope`. `AbandonDispatchScope` runs in a `finally` whenever dispatch did not complete. Every hook runs even when an earlier one throws. Afterwards, one failure rethrows with its stack, and several failures are wrapped in an `AggregateException`. Implementations must release in-memory state without performing durable writes or throwing an exception.

The retention cleanup services (`InboxCleanupBackgroundService` / `OutboxCleanupBackgroundService`) validate, exit when disabled or when `Retention` is null, then loop. Each iteration calls `DeleteCompletedOlderThanAsync(now - Retention)` / `DeletePublishedOlderThanAsync(...)`, records success on the coordinator, and sleeps `Interval`. All delays go through the injected `TimeProvider`, and host shutdown breaks the loop without throwing. On failure, the service increments the `cleanup.errors` counter, records the failure, and logs (`EventId 1101`). It then sleeps the current backoff (5 seconds after the first failure) and doubles it up to `Interval`.

### 4.15 Dispatch adapters

| Extension | Package | Registers |
| --- | --- | --- |
| `InboxModuleBuilder.UseInProcessDispatch()` | `LiteBus.Inbox.Dispatch.InProcess` | `CommandInboxDispatchModule` (requires `InboxModule` and `CommandModule`), which registers `IInboxDispatcher` = `CommandInboxDispatcher` |
| `OutboxModuleBuilder.UseInProcessDispatch(Action<InProcessOutboxDispatchOptionsBuilder>? = null)` | `LiteBus.Outbox.Dispatch.InProcess` | `EventOutboxDispatchModule` (requires `OutboxModule` and `EventModule`), which registers `IOutboxDispatcher` = `EventOutboxDispatcher`. `InProcessOutboxDispatchOptions.RequireHandler` defaults to `true` |
| `UseInMemoryDispatch(Action<TransportInboxDispatcherOptions>? = null)` | `LiteBus.Inbox.Dispatch.InMemory` | `TransportInboxDispatchModule<InMemoryTransportModule>` |
| `UseAmqpDispatch(Action<TransportInboxDispatcherOptions>)` | `LiteBus.Inbox.Dispatch.Amqp` | `TransportInboxDispatchModule<AmqpTransportModule>` |
| `UseKafkaDispatch(...)` | `LiteBus.Inbox.Dispatch.Kafka` | `TransportInboxDispatchModule<KafkaTransportModule>` |
| `UseAwsSqsDispatch(...)` | `LiteBus.Inbox.Dispatch.AwsSqs` | `TransportInboxDispatchModule<AwsSqsTransportModule>` |
| `UseAzureServiceBusDispatch(...)` | `LiteBus.Inbox.Dispatch.AzureServiceBus` | `TransportInboxDispatchModule<AzureServiceBusTransportModule>` |
| the same five `Use*Dispatch` names on `OutboxModuleBuilder` | `LiteBus.Outbox.Dispatch.*` | `TransportOutboxDispatchModule<TTransportModule>` |

`TransportInboxDispatchModule<TTransportModule>` / `TransportOutboxDispatchModule<TTransportModule>` declare `IRequires<TTransportModule>`, so the matching `Add*Transport(...)` call at the root is mandatory. Registering a second dispatcher on one axis throws `DurableStorageConfigurationException`. The outbox transport variant reports `DefaultHookFailurePolicy => CompleteDespiteHookFailure` and `PublishesFullEventPerEnvelope => true`, while `EventOutboxDispatchModule` keeps the `DeadLetter` default. The inbox builder always resolves an unset policy to `DeadLetter`.

The outbox transport module registers the configured `TransportOutboxDispatcherOptions` instance and `IOutboxDispatcher` = `TransportOutboxDispatcher` (Transient). It also registers `TransportOutboxDispatcherOptions.TenantRoutingStrategy` as Singleton `ITenantRoutingStrategy` when set.

`CommandInboxDispatcher` resolves the CLR type from the contract, decrypts, deserializes, requires the result to implement `ICommand` (`InvalidOperationException` otherwise), and builds `CommandMediationSettings` with `IsInboxExecution`, `MessageId`, `ContractName`, and the trace items. It then sends through `ICommandMediator`. Dispatch failures propagate unwrapped.

`EventOutboxDispatcher` resolves the same way. It then builds `EventMediationSettings` with `ThrowIfNoHandlerFound = RequireHandler` (default `true`, so a message with no handler fails the attempt and eventually dead-letters) plus the envelope trace items, and it publishes through `IEventMediator`. If the instance implements `IEvent` it uses the non-generic overload. Otherwise it builds and caches a closed generic `PublishAsync<TEvent>` delegate per message type with `CreateDelegate`, so handler failures surface unwrapped instead of arriving wrapped in a `TargetInvocationException`.

The dispatcher rethrows the caller's cancellation unwrapped, so shutdown does not spend the retry budget. The dispatcher wraps every other non-`LiteBusDispatchException` failure in `LiteBusDispatchException`, with contract-registration guidance only for `MessageContractNotRegisteredException` and `NoHandlerFoundException`. The `configure` action receives the mutable `InProcessOutboxDispatchOptionsBuilder` (`RequireHandler { get; set; } = true`, internal `Build()`), e.g. `outbox.UseInProcessDispatch(dispatch => dispatch.RequireHandler = false)`.

`TransportInboxDispatcher` / `TransportOutboxDispatcher` resolve the CLR type, decrypt, and deserialize for validation only when `ValidatePayloadBeforeDispatch` is true (default false). They then resolve the route, UTF-8 encode the payload, and publish a `TransportPublishRequest` with `MessageId = envelope.Id.ToString("D")`, `CorrelationId`, `ContentType` (default `application/json`), `Persistent` (default true) and `Mandatory` (default true) from the dispatcher options, and the canonical `TransportHeaders`. The outbox dispatcher also sets `MessageGroupId` (tenant id, otherwise contract name) and `DeduplicationKey` (idempotency key, otherwise message id) for SQS FIFO queues. Route precedence is the tenant routing strategy when one is registered, then the `ResolveRoute` delegate when set, then the envelope `Topic` (outbox only) when present, and finally the contract name. When the strategy returns null or whitespace, the outbox dispatcher fails the dispatch with `LiteBusDispatchException` naming the message. When the delegate returns null or whitespace, the dispatcher fails the dispatch with `LiteBusDispatchException` / `InboxDispatchException` naming the message.

### 4.16 Transports

The transport contracts are three interfaces:

```csharp
public interface ITransportPublisher
{
    Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default);
}

public interface IMessageConsumer : IAsyncDisposable
{
    Task StartAsync(TransportConsumerOptions options,
                    Func<TransportMessage, CancellationToken, Task> handler,
                    CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task WaitUntilStoppedAsync(CancellationToken cancellationToken = default);
}

public interface ITenantRoutingStrategy
{
    string ResolveRoute(string? tenantId, string contractName, string? topic);
}
```

`IMessageConsumer` instances hold one subscription. Transport modules register the interface as transient, and a second `StartAsync` on one instance throws. `StartAsync` returns a task that completes when the consume loop has been initiated. Startup and subscription failures are reported through `WaitUntilStoppedAsync`, which faults with the terminal consumer failure.

Disposing a consumer twice is tolerated. `TransportMessage` carries `Body`, `Headers`, `Destination`, `Route`, `MessageId`, `CorrelationId`, and `Redelivered`. The handler settles it through `AcceptAsync`, `DiscardAsync`, or `ReturnToQueueAsync`. The handler must call exactly one of those before returning.

The canonical headers on `TransportHeaders` are:

| Constant | Header name | Written by |
| --- | --- | --- |
| `MessageId` | `litebus-message-id` | always (`Guid` in `D` format) |
| `ContractName` | `litebus-contract-name` | always |
| `ContractVersion` | `litebus-contract-version` | always (`int`) |
| `CorrelationId` | `correlation-id` | when present |
| `CausationId` | `causation-id` | when present |
| `TenantId` | `tenant-id` | when present |
| `TraceContext` | `litebus-trace-context` | when present |
| `IdempotencyKey` | `litebus-idempotency-key` | when present |
| `VisibleAfter` | `litebus-visible-after` | when present (round-trip `O` format) |
| `VisibleAfterDelay` | `litebus-visible-after-delay` | consumed on ingress (ISO 8601 duration or tick count) |
| `ContentEncoding` | `litebus-content-encoding` | SQS base64 bodies |
| `Route` | `litebus-route` | SQS mapper when `Route` is present (Kafka carries the route as the record key) |
| `ContentType` | `content-type` | SQS and Kafka mappers |

`TransportEnvelopeHeaderMapper.BuildHeaders(TransportEnvelopeHeaderSource)` produces this dictionary. `TransportEnvelopeHeaderSource` is `(Guid MessageId, string ContractName, int ContractVersion, string? CorrelationId, string? CausationId, string? TenantId, string? TraceContext, string? IdempotencyKey, DateTimeOffset? VisibleAfter)`. `TransportHeaderValues.GetString`, `GetInt32` and `ConvertToString` read header values that brokers may deliver as strings, byte arrays or numerics.

`GetInt32` accepts `int`, `byte`, `sbyte`, `short`, in-range `long`, invariant-culture numeric strings, and `byte[]` of length 1 (the single byte value) or 4 (little-endian `BitConverter`), and it returns `null` for anything else. `ConvertToString` decodes `byte[]`, `ReadOnlyMemory<byte>` and `Memory<byte>` as UTF-8 and passes strings through, and it falls back to invariant `Convert.ToString` for anything else. A missing or invalid required header raises `TransportHeaderMappingException` (factories `MissingRequiredHeader(name)` and `InvalidRequiredHeader(name, detail)`).

`ITransportCircuitBreakerRegistry` exposes `IsAnyOpen`, a `long` total `FailureCount`, and `GetPublisherCircuit(string destination)`, which returns the stable per-destination breaker and throws on a blank destination. `ITransportCircuitBreaker` exposes `IsOpen` (`true` when open or while a half-open recovery probe is admitted), `FailureCount` (consecutive failures while closed, otherwise the configured threshold), `AcquirePermit()` (throws `TransportCircuitBreakerOpenException` when open or while another probe is active), and `RecordSuccess` / `RecordFailure` / `ReleasePermit`.

`TransportCircuitBreaker` is constructed as `new TransportCircuitBreaker(TransportCircuitBreakerOptions? options = null, TimeProvider? timeProvider = null)` (defaults to new options and `TimeProvider.System`). It throws when `FailureThreshold` is negative, or when `BreakDuration` is zero or negative while the threshold is positive. A threshold of 0 disables breaking. It opens after `FailureThreshold` consecutive failures (default 5) and rejects calls with `TransportCircuitBreakerOpenException` for `BreakDuration` (default 30 seconds). Afterwards, exactly one caller is admitted as a half-open recovery probe, while other callers are rejected. A successful probe closes the circuit, and a failed probe reopens it with a new break duration. Outcomes from stale generations are ignored.

`TransportCircuitBreakerPermit` is a readonly struct with no public members. It is the opaque, generation-scoped value returned by `AcquirePermit` and supplied to `RecordSuccess`, `RecordFailure`, or `ReleasePermit`. `ReleasePermit` is for outcomes that do not indicate broker health (caller cancellation, unroutable publication, client-side configuration failure). Releasing a half-open probe reopens the circuit without starting a new break duration. Gauges `litebus.transport.circuit_breaker.open` and `litebus.transport.circuit_breaker.failure_count` are tagged with `litebus.transport.broker`. Counters `litebus.transport.circuit_breaker.failure_recorded` and `litebus.transport.circuit_breaker.opened` count closed-circuit failures and openings.

Each transport adapter registers as follows:

| Transport | Root call | Registered services | Diagnostic probe |
| --- | --- | --- | --- |
| In-memory | `AddInMemoryTransport()` (or with `InMemoryTransportOptions`) | `InMemoryTransportOptions`, `InMemoryTransportBroker` (Singleton, `IDisposable`), `ITransportCircuitBreakerRegistry`, `ITransportPublisher` = `InMemoryPublisher` (Singleton), `IMessageConsumer` = `InMemoryConsumer` (Transient) | none |
| AMQP (RabbitMQ, LavinMQ) | `AddAmqpTransport(AmqpConnectionOptions)` | `AmqpConnectionOptions`, `IAmqpConnectionManager` = `AmqpConnectionManager` (Singleton), `ITransportCircuitBreakerRegistry` (from `options.CircuitBreaker`), `ITransportPublisher` = `AmqpPublisher` (Singleton), `IMessageConsumer` = `AmqpConsumer` (Transient), `IAmqpPublisher` / `IAmqpConsumer` bindings | `transport.amqp.connectivity` |
| Kafka | `AddKafkaTransport(KafkaTransportOptions)` | `KafkaTransportOptions`, internal `KafkaProducerOwner` (Singleton, flushed once on dispose), `IConsumer<string, byte[]>` (Transient, one owned client per `KafkaConsumer` closed on stop and disposed with it, fixed `EnableAutoCommit = false` and `AutoOffsetReset.Earliest`), `IAdminClient` (Singleton, from `BootstrapServers` + `ClientId`), `ITransportCircuitBreakerRegistry`, `ITransportPublisher` = `KafkaPublisher` (Singleton factory), `IMessageConsumer` = `KafkaConsumer` (Transient) | `transport.kafka.connectivity` |
| AWS SQS | `AddAwsSqsTransport(AwsSqsTransportOptions)` | `AwsSqsTransportOptions`, `IAmazonSQS` (Singleton), `ITransportCircuitBreakerRegistry`, `ITransportPublisher` = `SqsPublisher` (Singleton), `IMessageConsumer` = `SqsConsumer` (Transient) | `transport.sqs.connectivity` |
| Azure Service Bus | `AddAzureServiceBusTransport(AzureServiceBusTransportOptions)` | `AzureServiceBusTransportOptions`, `ServiceBusClient` (Singleton), `ITransportCircuitBreakerRegistry`, `ITransportPublisher` = `AzureServiceBusPublisher` (Singleton), `IMessageConsumer` = `AzureServiceBusConsumer` (Transient) | `transport.azure_service_bus.connectivity` |

Every adapter also calls `TransportMetricsRegistration.RegisterIfNeeded(configuration, broker, moduleType)`. It records the `TransportBrokerIdentity` and registers `TransportObservableMetrics` plus the `TransportObservableMetricsInitializer` startup task. When a second transport module is composed, it throws `ModuleCompositionException` naming both modules.

`TransportConsumerHandlerInvoker.CreateBoundedHandler(handler, maxInFlight)` wraps a delivery handler with a semaphore admission gate. `CreateBoundedOutcomeHandler(handler, maxInFlight)` additionally reports a `TransportConsumerInvocationOutcome` (`Handled` or `Requeued`). When a handler throws a non-cancellation failure, the invoker returns its delivery via `ReturnToQueueAsync`, and the consume loop continues. During graceful shutdown (`ShouldRequeueOnFailure` false), the invoker leaves the delivery to broker-specific close semantics.

`InvokeWithOutcomeAsync(message, handler, ct)` starts a consume activity and returns `Handled` on success. On a non-cancellation failure, it records the exception and calls `message.ReturnToQueueAsync`, returning `Requeued`. The broad catch is intentional. Raw `IMessageConsumer` hosts rely on it, while ingress layers its own policy on top. The invoker rethrows a caller cancellation during requested shutdown instead. `InvokeAsync` discards the outcome. `ShouldRequeueOnFailure(exception, ct)` returns `false` only for `OperationCanceledException` during requested shutdown.

`TransportPublishRequest` requires `Destination` and `Body`. `ContentType` defaults to `application/json`, while `Persistent` and `Mandatory` default to `true`. `MessageId` is optional (the Azure Service Bus publisher copies it only when supplied). `MessageGroupId` and `DeduplicationKey` are FIFO addressing. The SQS adapter copies a non-blank `DeduplicationKey` to SendMessage `MessageDeduplicationId` and a non-blank `MessageGroupId` to `MessageGroupId` on queue URLs ending in `.fifo`, and sends neither on standard queues.

When the broker returns a mandatory publication as unroutable, the publish fails with `AmqpMessageReturnedException`, which carries `Exchange`, `RoutingKey`, `ReplyCode`, and `ReplyText` over the client return exception. The publisher releases the circuit-breaker permit instead of counting a broker failure, so a routing mistake cannot open the circuit. `AmqpReceivedMessage.QueueName` names the queue on which a typed `IAmqpConsumer` delivery arrived, and `AmqpConsumerOptions.MaxInFlightMessages` (default 32) gives typed subscriptions the same admission control as the generic consumer path. A failed consumer start releases its channel, so the start can be retried.

`AmqpReceivedMessage` is a sealed class with required `Body` (`ReadOnlyMemory<byte>`, copy before the handler returns), `Headers`, `DeliveryTag` (`ulong`), `AckDelegate` (`Func<bool, CancellationToken, Task>`) and `NackDelegate` (`Func<bool, bool, CancellationToken, Task>`). `Exchange`, `RoutingKey`, `QueueName`, `MessageId` and `CorrelationId` are `string?` defaulting to null, and `Redelivered` defaults to `false`. `AcceptAsync` acks, `DiscardAsync` nacks without requeue for poison messages, and `ReturnToQueueAsync` nacks with requeue for transient failures. `AmqpHeaderValues` is a static class with `GetString` / `GetInt32` delegating to `TransportHeaderValues`.

`IAmqpConnectionManager` (extends `IAsyncDisposable`) exposes `GetConnectionAsync` (shared connection, created on first use), `CreateChannelAsync` (caller-owned channel) and `CreatePublisherChannelAsync` (confirmation channel; the default implementation delegates to `CreateChannelAsync` for custom managers). `AmqpConnectionManager` is constructed as `new AmqpConnectionManager(AmqpConnectionOptions options)` and opens one lazily created shared connection (serialized creation) guarded by a `TransportCircuitBreaker` built from `options.CircuitBreaker`.

`IAmqpConsumer` (extends `IAsyncDisposable`) exposes `StartAsync(AmqpConsumerOptions, Func<AmqpReceivedMessage, CancellationToken, Task>, CancellationToken)` (the handler must call `AcceptAsync` / `DiscardAsync` / `ReturnToQueueAsync` before returning), `StopAsync` and `WaitUntilStoppedAsync`. `IAmqpPublisher` exposes `PublishAsync(AmqpPublishRequest request, CancellationToken cancellationToken = default)`. The native and neutral registrations are separate instances. `ITransportPublisher` (Singleton) and `IAmqpPublisher` (Transient) resolve to different `AmqpPublisher` instances, and `IMessageConsumer` and `IAmqpConsumer` (both Transient) resolve to different `AmqpConsumer` instances. A second `Start` throws `InvalidOperationException` ("The AMQP consumer is already started."). Deliveries run under a subscription-owned token that `StopAsync` cancels, and each delivery settles at most once. On a handler failure, the consumer nacks only when the handler never settled.

The neutral `IMessageConsumer.StartAsync` validates `Destination` non-blank and `PrefetchCount` 0..65535. It maps `Destination` to `QueueName`, `PrefetchCount` to `ushort`, and `MaxInFlightMessages` / `DeclareDestination` / `DurableDestination` / `Exclusive` / `ConsumerTag` / `DestinationArguments` across, and it maps deliveries to `TransportMessage` with `MessagingSystem` `rabbitmq`, `Destination` = active queue and `Route` = routing key. `AmqpPublisher` is constructed as `new AmqpPublisher(IAmqpConnectionManager connectionManager, ITransportCircuitBreakerRegistry circuitBreakerRegistry)`. Publishes are serialized on one lazily created confirm channel, and the neutral `PublishAsync` maps `Destination` to `Exchange` and `Route ?? string.Empty` to `RoutingKey`. `OperationInterruptedException`, `PublishException`, and `BrokerUnreachableException` increment the per-exchange circuit, while the publisher traces and rethrows unexpected failures without touching resilience state.

`KafkaConsumer` is constructed as `new KafkaConsumer(IConsumer<string, byte[]> consumer, KafkaTransportOptions options)`. `StartAsync` validates `Destination` non-blank and `MaxInFlightMessages` (at least 1). It throws `InvalidOperationException` ("The Kafka consumer is already started.") when the consumer is already started. It then starts the loop with `Task.Run` and returns without waiting for the first record. The loop subscribes to `Destination`, polls `Consume` every 250 ms, runs each delivery through a handler bounded by `MaxInFlightMessages`, and commits offsets manually after acknowledgement. `ReturnToQueueAsync` seeks back to the failed offset so the record is consumed again, and `AcceptAsync` commits the offset. Deliveries are at-least-once, so handlers must be idempotent. Each consumer owns its client. `StopAsync` closes it for a prompt group leave, and dispose releases it. On a transient consume error, the loop backs off one second and keeps polling. Only a fatal error faults the loop, and the fault surfaces through `WaitUntilStoppedAsync`. A requeue seek that fails transiently retries with backoff until it succeeds or shutdown cancels it.

`KafkaPublisher` exposes a public `(IProducer<string, byte[]> producer, ITransportCircuitBreakerRegistry circuitBreakerRegistry)` constructor while the module uses an internal owner-based constructor. `Dispose` is intentionally a no-op because `KafkaProducerOwner` flushes the shared producer once at shutdown, so dispatch-scope teardown never blocks on a flush. `ProduceException<string, byte[]>` and `KafkaException` record a destination-scoped circuit failure, and the publisher rethrows them. Caller cancellation releases the permit. Unexpected failures are traced and rethrown without touching resilience state. Every publish starts a `send` activity tagged `messaging.system` = kafka.

The adapter sends bodies that are not valid UTF-8 text under the SQS-allowed code points as base64 with the `litebus-content-encoding` attribute, and it decodes them on receive. Caller headers cannot replace mapper-owned attributes. When attributes would exceed the ten-attribute SQS limit, the mapper packs non-reserved headers as JSON into the `litebus-headers` attribute and expands them on receive.

`SqsPublisher` is constructed as `new SqsPublisher(IAmazonSQS sqsClient, ITransportCircuitBreakerRegistry circuitBreakerRegistry)`. `AmazonSQSException` increments the destination circuit. Client-side SDK failures surface as `AwsSqsTransportConfigurationException`, and the publisher releases the permit instead of recording failure. Unexpected failures are traced and rethrown. `SqsConsumer` is constructed as `new SqsConsumer(IAmazonSQS sqsClient, AwsSqsTransportOptions options, TimeProvider? timeProvider = null)` (defaults to `TimeProvider.System`) and runs a long-poll loop with manual delete semantics. `StartAsync` validates `Destination` non-blank, `ReceiveBatchSize` 1..10, and `MaxInFlightMessages` (at least 1). A second `Start` throws `InvalidOperationException` ("The SQS consumer is already started."). The consumer deletes a delivery whose body or packed headers cannot be mapped as poison instead of requeueing it, so one bad delivery cannot stall the loop. The delete is best-effort. Mapping tolerates a missing body, attributes, or message attributes. Loop faults are reported through `WaitUntilStoppedAsync`.

`AzureServiceBusConsumer` is constructed as `new AzureServiceBusConsumer(ServiceBusClient client, AzureServiceBusTransportOptions options, ILogger<AzureServiceBusConsumer>? logger = null)` with a `NullLogger` fallback. It completes messages manually with structured processor recovery. `StartAsync` validates `Destination` non-blank, `PrefetchCount` (at least 0), `MaxInFlightMessages` (at least 1) and `MaxConcurrentCalls` (at least 1) when set. A second `Start` throws `InvalidOperationException` ("The Azure Service Bus consumer is already started."). The recovery loop restarts failed processor sessions with backoff doubling from `ConsumerErrorRetryInterval` capped at `ConsumerErrorRetryMaxInterval`. The backoff resets to the base interval on success. Each restart logs Warning `EventId` 4001 ("ProcessorSessionRestarting").

`AzureServiceBusPublisher` is constructed as `new AzureServiceBusPublisher(ServiceBusClient client, ITransportCircuitBreakerRegistry circuitBreakerRegistry)` and caches one `ServiceBusSender` per destination name (ordinal comparer). `Dispose` runs once at container disposal, bounds the async sender close to 5 seconds, and catches `TimeoutException`, `ServiceBusException`, `ObjectDisposedException` and `OperationCanceledException` so broker close problems never fail host shutdown. `DisposeAsync` disposes the cached senders. `ServiceBusException` increments the destination circuit. Unexpected failures are traced and rethrown without touching resilience state.

`InMemoryTransportBroker` is constructed as `new InMemoryTransportBroker()` or `new InMemoryTransportBroker(InMemoryTransportOptions options)` (`DestinationCapacity` must be at least 1). Endpoints are keyed ordinally by destination name. `Dispose` completes every destination channel and clears endpoints. Disposal is terminal, and repeated calls return without accessing cleared state.

`InMemoryConsumer` is constructed as `new InMemoryConsumer(InMemoryTransportBroker broker)`. When a handler throws, the consumer returns the delivery to the channel for redelivery by default, while handlers that reject without throwing must call `ReturnToQueueAsync` / `DiscardAsync` explicitly (unlike broker transports, where the broker owns retry and dead-letter policy). When the handler returns or the loop stops, the consumer returns a delivery the handler never settled to the channel for redelivery. `StartAsync` validates that options and handler are non-null, that `Destination` is non-blank, and that `MaxInFlightMessages` is at least 1. A second `Start` throws `InvalidOperationException` ("The in-memory consumer is already started."). `InMemoryPublisher` is constructed as `new InMemoryPublisher(InMemoryTransportBroker broker, ITransportCircuitBreakerRegistry circuitBreakerRegistry)`. `ChannelClosedException` increments the destination circuit. Unexpected failures are traced and rethrown without touching resilience state.

### 4.17 Inbox ingress (broker to inbox)

```csharp
liteBus.AddAmqpTransport(new AmqpConnectionOptions { HostName = "rabbit", VirtualHost = "/app" });

liteBus.AddInbox(inbox =>
{
    inbox.Contracts.Register<ProcessPaymentCommand>("payments.process");
    inbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
    inbox.UseInProcessDispatch();
    inbox.EnableInboxProcessor();
    inbox.UseAmqpIngress(ingress => ingress.UseOptions(new AmqpInboxIngressOptions
    {
        QueueName = "payments-inbound",
        PrefetchCount = 32,
        RequeueOnFailure = true,
        Safety = new TransportInboxIngressSafetyOptions
        {
            MaxMessageBytes = 512 * 1024,
            RequireStableIdentity = true,
            TrustApplicationHeaders = false,
            MaxInFlightMessages = 16,
            EnableBatchAccept = true,
            BatchSize = 25,
            BatchMaxWait = TimeSpan.FromMilliseconds(150),
            AuthorizeDeliveryAsync = async (message, ct) =>
            {
                if (!message.Headers.ContainsKey("x-tenant-signature"))
                {
                    throw new InboxIngressException("missing tenant signature");
                }
                await Task.CompletedTask;
            }
        }
    }));
});
```

`TransportInboxIngressConsumer` is an `IBackgroundService`. It starts the broker consumer, maps each delivery through `TransportInboxIngressMapper.ToInboxAcceptMetadata`, accepts the delivery into the inbox, then settles it. The mapper derives identity, idempotency, visibility, trace, and tenant from `TransportHeaders` and broker properties, and it reads `RequireStableIdentity` and `TrustApplicationHeaders` to decide which source each value comes from.

Before deserialization, the handler runs `AuthorizeDeliveryAsync` and rejects bodies over `MaxMessageBytes`. It then requires the contract name header and a positive integer contract version.

With the default `TrustApplicationHeaders = false`, identity and idempotency come from the broker delivery id only. The mapper uses a GUID id directly, and hashes any other id with the normalized destination into a deterministic GUID for identity and into a destination-scoped `ingress:{destination}:{id}` idempotency key. The tenant stays unscoped, and the mapper ignores visibility headers, so accepted messages become visible immediately. When the broker id is missing and `RequireStableIdentity` is true (the default), the handler throws `InboxIngressException`. When `RequireStableIdentity` is false, the mapper falls back to generated identity with no idempotency key.

The mapper always honors correlation headers. When `TrustApplicationHeaders` is true, the `litebus-message-id`, `litebus-idempotency-key` and `tenant-id` headers may supply identity, idempotency, and tenant scope instead, and the mapper honors visibility headers.

On a loop failure the consumer logs `EventId 3002` and restarts after `RetryPollInterval` (default 5 seconds; a zero value falls back to `MinimumRestartDelay` of 1 second). A batch flush failure logs `EventId 3003`. A batch accept failure falls back to per-message acceptance, and the consumer logs the fallback as Warning `EventId 3006 BatchAcceptFallback`, so one poison delivery cannot fail the batch. A non-requeue acceptance failure discards the delivery, and the consumer logs the discard as Warning `EventId 3005 DeliveryDiscardedAfterAcceptFailure`.

A cancelled batch skips the per-message fallback, releases admission for the whole batch, and rethrows, so buffered deliveries stay unacknowledged. A cancelled acceptance rethrows before settlement, so the delivery stays unacknowledged for the broker to redeliver. When broker acknowledgement fails after a successful accept, the consumer logs `EventId 3004` and increments `ingress.ack_failed_after_accept`, then returns the delivery to the queue. The existing row absorbs the redelivery, because a redelivery after a successful accept is the expected at-least-once outcome.

`IngressAckPolicy.ShouldRequeue(exception, requeueOnFailure)` refuses to requeue when `requeueOnFailure` is false. It also refuses when the unwrapped exception, or anything in its inner chain, is one of `MessageContractNotRegisteredException`, `InboxDispatchException`, `InboxIngressException`, `InboxStorageException`, `MessageSerializationException`, `InvalidOperationException`, `ArgumentException`, `FormatException`, `JsonException`, or `OperationCanceledException`. These are poison-message classes that would requeue in a loop. A cancelled acceptance stays unacknowledged, so closing the channel requeues it. `IngressAckPolicy.UnwrapException` removes `TargetInvocationException` and single-inner `AggregateException` wrappers in a loop.

`TransportInboxIngressHandler` (`IInbox inbox`, `IContractReader contractRegistry`, `IMessageSerializer messageSerializer`, `TransportInboxIngressOptions options`) maps deliveries to acceptance calls. `AcceptAsync(TransportMessage, CancellationToken)` builds one `InboxAcceptItem` and accepts it, wrapping `TransportHeaderMappingException` in `InboxIngressException`. `AcceptBatchAsync(IReadOnlyList<TransportMessage>, CancellationToken)` returns immediately for an empty list. Otherwise it builds every item before one `AcceptBatchAsync` store round trip, and wraps mapping failures the same way.

Each item first passes the `Safety.MaxMessageBytes > 0` gate, then `Safety.AuthorizeDeliveryAsync` runs. The handler then requires the contract name header and a positive integer contract version. It resolves the CLR type, deserializes the UTF-8 body, and maps headers through `TransportInboxIngressMapper.ToInboxAcceptMetadata`.

`AmqpInboxIngressHandler` exposes the same `AcceptAsync` / `AcceptBatchAsync` pair over `AmqpReceivedMessage`. A one-item batch goes through `AcceptAsync`. It delegates to the transport-neutral handler after converting each delivery to a `TransportMessage` with queue-aware `Destination` (`QueueName ?? Exchange`), so broker-scoped idempotency does not merge rows across queues bound to one exchange.

Each broker provides one ingress module through a `Use*Ingress` builder extension. The extension takes a required configure action and registers an inbox child module through `RegisterIngress`. The five extensions are `UseAmqpIngress`, `UseKafkaIngress`, `UseAwsSqsIngress`, `UseAzureServiceBusIngress`, and `UseInMemoryIngress`. Every module requires `InboxModule` plus its transport module (`AmqpTransportModule`, `KafkaTransportModule`, `AwsSqsTransportModule`, `AzureServiceBusTransportModule`, `InMemoryTransportModule`).

Every module validates `Safety`, registers `TransportInboxIngressOptions` plus `TransportInboxIngressHandler`, and registers the consumer background service when enabled. AMQP additionally registers `AmqpInboxIngressOptions` and `AmqpInboxIngressHandler`. Missing destination configuration throws `DurableStorageConfigurationException`. That means a blank AMQP `QueueName`, null `Options` or a blank `Destination` for Kafka, SQS and Azure Service Bus, or a blank `Destination` for in-memory. The SQS module only accepts `ReceiveBatchSize` from 1 to 10. The Azure Service Bus module validates a non-negative `PrefetchCount` and a `MaxConcurrentCalls` of at least 1 when set.

An inbox module accepts at most one ingress source. `RegisterIngress` throws `DurableStorageConfigurationException` for a second one, because ingress host options, the transport consumer, and background-service ownership are singular.

### 4.18 Saga orchestration

```csharp
liteBus.AddInbox(inbox =>
{
    inbox.Contracts.Register<StartCheckout>("checkout.start");
    inbox.Contracts.Register<PaymentAuthorized>("checkout.payment-authorized");
    inbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(cs));
    inbox.UseInProcessDispatch();
    inbox.EnableInboxProcessor();

    inbox.EnableSaga(saga =>
    {
        saga.DefineState<CheckoutSagaState>("checkout");
        saga.MapContract("checkout.start", "checkout");
        saga.MapContract("checkout.payment-authorized", "checkout");
        saga.UsePostgreSqlStorage(pg => pg.UseConnectionString(cs));   // or saga.UseInMemoryStorage()
    });
});
```

`EnableSaga` registers two child modules: `SagaModule` and `SagaInboxCommandScopeModule`. `SagaModule` is a `ChildrenFirst` composite that owns the storage module. `SagaModule.Build` registers `ISagaStateTypeRegistry`, `SagaExecutionContext`, `ISagaContext` (projected from `SagaExecutionContext`), and `IProcessorEnvelopeHook` to `SagaProcessorHook`, all as Singleton. `SagaInboxCommandScopeModule` requires `MessageModule` and registers the singleton `SagaInboxCommandScopePreHandler` in the dependency registry and in the message registry. That pre-handler is an `ICommandPreHandler`. During in-process dispatch, when the mediation carries the inbox execution and message id items, it re-attaches the ambient saga scope.

Storage is mandatory and singular. Registering a second storage module throws, and building without one throws `DurableStorageConfigurationException`. `SagaModule.Build` also validates that every `MapContract` definition id has a registered state type. When one has none, it throws `DurableStorageConfigurationException` naming the contract and definition.

Handlers read and write state through the ambient saga context:

```csharp
public sealed class PaymentAuthorizedHandler : ICommandHandler<PaymentAuthorized>
{
    private readonly ISagaContext _saga;

    public PaymentAuthorizedHandler(ISagaContext saga) => _saga = saga;

    public Task HandleAsync(PaymentAuthorized message, CancellationToken cancellationToken = default)
    {
        var state = _saga.GetState<CheckoutSagaState>();
        var alreadyAuthorized = state.PaymentAuthorized;
        state.PaymentAuthorized = true;

        if (alreadyAuthorized)
        {
            _saga.Complete();
        }

        return Task.CompletedTask;
    }
}
```

```csharp
public interface ISagaContext
{
    bool IsActive { get; }
    SagaCorrelation? Correlation { get; }
    TState GetState<TState>() where TState : class, new();
    void SetState<TState>(TState state) where TState : class, new();
    void Complete();
}

public interface ISagaStore
{
    Task<SagaInstance<TState>?> LoadAsync<TState>(SagaCorrelation correlation, CancellationToken cancellationToken = default)
        where TState : class, new();
    Task SaveAsync<TState>(SagaSaveItem<TState> item, CancellationToken cancellationToken = default) where TState : class, new();
    Task CompleteAsync(SagaCompleteItem item, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SagaInstanceSummary>> QueryAsync(SagaQueryFilter filter, CancellationToken cancellationToken = default);
    Task<int> PurgeAsync(SagaPurgeFilter filter, CancellationToken cancellationToken = default);
}
```

`SagaProcessorHook` activates only for envelopes with a non-blank correlation id whose contract name maps to a registered saga definition and state type. Any other envelope dispatches with an inactive scope. The hook serializes dispatches for one tenant, definition, and correlation through an in-process correlation gate. It snapshots the state JSON at load.

On `AfterDispatchAsync` the hook compares the current state against that snapshot. A dispatch that only reads state saves nothing. In-place mutations persist without `SetState`. Combining a state change with `Complete()` in one dispatch throws `InvalidOperationException`.

When a redelivery carries the stored `LastAppliedMessageId`, the hook suppresses the dispatch through `ShouldDispatch`, while the inbox message still completes. When the saga already completed, the hook dispatches a new correlated message with an inactive scope instead of suppressing it.

The hook saves dirty state with the expected version. A conflict raises `SagaConcurrencyException` (carrying the `Correlation`) without retry. Completion-only dispatches retry up to 3 attempts (`MaxCompletionAttempts`). Each retry reloads on conflict and returns early when another worker already completed. When `Complete()` runs with no row at version 0, the hook saves the initial snapshot and completes the new row. `AbandonDispatchScope` releases the correlation gate.

`SagaPurgeFilter.Take` bounds a purge to at most `Take` of the oldest matching rows. A filter with no predicate and no `Take` throws `ArgumentException`. A `Take` of zero or less throws `ArgumentOutOfRangeException`.

`ISagaStateTypeRegistry` maps definitions and contracts to state types. `RegisterStateType<TState>(string sagaDefinitionId)` registers a state type, and the last registration wins. `MapContract(string contractName, string sagaDefinitionId)` maps a contract to a definition. `ResolveDefinitionId(string contractName)` returns the mapped definition, or the contract name itself when a state type is registered under it, or null. `ResolveStateType(string sagaDefinitionId)` returns the registered CLR type or null.

`GetState` with no active scope throws `InvalidOperationException`. `SagaQueryFilter.CorrelationId` is an exact match in both stores. In-memory comparison uses ordinal equality, and PostgreSQL compares `correlation_id = @correlation_id`.

`LastAppliedMessageId` suppresses redelivery of only the most recently applied message. When an inbox terminal persist fails after a save, a newer message for the same correlation can advance the row before the failed message retries. The retry then runs its handler a second time. Handlers with external side effects therefore need their own idempotency.

In-memory saga storage registers `ISagaStore` to `InMemorySagaStore` as Singleton (`InMemorySagaStorageModule`). The store takes `(IMessageSerializer serializer, TimeProvider? clock = null)` and enforces the same purge rules (`Take > 0`, at least one predicate or a `Take` limit).

`PostgreSqlSagaModule` requires a data source (`UseDataSource` or `UseConnectionString`) and throws `DurableStorageConfigurationException` without one. It registers the data source owner, `ISagaStore`, `PostgreSqlSagaStoreRegistration` (data source plus `PostgreSqlSagaStoreOptions`), and the `PostgreSqlSagaSchemaInitializer` startup task when schema initialization is enabled. It registers no diagnostic probe. `PostgreSqlSagaSchema` is the static schema API (`CurrentSchemaVersion = 1`, `SqlFiles`, `GetCreateScript`, idempotent advisory-locked `EnsureAsync`, `ValidateAsync` throwing `PostgreSqlSchemaDriftException` on drift), with copy-paste migration paths in `PostgreSqlSagaSchemaSqlPaths` (`V1Create`, `V1EnsureIndexes`, `{{TokenName}}` placeholders).

`PostgreSqlSagaStore.SaveAsync` rejects a negative `ExpectedVersion`. When the expected version is 0 it inserts a version-1 row (`ON CONFLICT DO NOTHING`, where zero inserted rows throw `SagaConcurrencyException`). Otherwise it conditionally updates with a version and `is_completed = false` predicate, and throws `SagaConcurrencyException` on mismatch.

### 4.19 Storage adapters

#### In-memory

```csharp
inbox.UseInMemoryStorage(store => store
    .UseOptions(new InMemoryInboxStoreOptions { Capacity = 10_000, DefaultLeaseDuration = TimeSpan.FromSeconds(30) })
    .UseTimeProvider(new ManualTimeProvider(DateTimeOffset.UnixEpoch)));
```

`Capacity` defaults to 0, which means the store accepts without a limit. Once a positive capacity is reached, the store rejects new commands, unless the submission is idempotent.

`DefaultLeaseDuration` defaults to one minute and is validated positive at construction. The outbox store substitutes it when a lease request supplies `TimeSpan.Zero`, and throws `ArgumentOutOfRangeException` for a negative duration. The inbox store never applies it, and throws when the duration is zero or negative. `RenewLeaseAsync` on both stores throws when the duration is zero or negative.

The module registers one store instance under every inbox role interface plus the concrete `InMemoryInboxStore`, the `TimeProvider` when `UseTimeProvider` set one, and `IInboxWorkSignal` to `InboxPollingWorkSignal`. Idempotency is indexed by a composite scope key built from the normalized tenant id and the idempotency key. `DurableIdempotencyScope.CreateScopeKey` builds the key with U+001F separators, and `DurableTenantId.Normalize` maps null/blank to the empty string.

`ReleaseLeaseAsync` returns the row to `Pending` (or `Failed` when it carries an error) with the lease cleared, the attempt count decremented, and visibility unchanged. It returns false when the owner or generation no longer matches. `PersistAsync` rejects a non-terminal envelope with `InvalidOperationException`.

Both stores take optional parameters. Null options become `new()`, and a null clock becomes `TimeProvider.System`. Both expose lock-guarded test helpers: `Count`, `Get(Guid messageId)` (dictionary indexer semantics, throwing `KeyNotFoundException` when absent), `GetAll()`, `GetAll(status)`, `GetAll(string contractName)` and `GetAll(status, contractName)` (blank contract names throw), and `Clear()`, which removes every envelope and idempotency-index entry.

Under `IdempotencyConflictMode.Strict`, a resubmission that matches the stored row in id, contract name and version, payload, idempotency key, correlation and causation ids, and normalized tenant (plus topic and handler on the outbox) returns the existing row. Only a differing duplicate throws `IdempotencyConflictException`.

On the inbox store, a message id already stored for another tenant throws regardless of conflict mode. The store stamps completions without a timestamp from the injected clock. The outbox store follows the same rules.

#### PostgreSQL

```csharp
inbox.UsePostgreSqlStorage(pg => pg
    .UseConnectionString(connectionString)      // or UseDataSource(existingNpgsqlDataSource)
    .UseOptions(new PostgreSqlInboxStoreOptions
    {
        SchemaName = "messaging",
        TableName = "inbox_messages",
        UseListenNotify = true,
        TerminalRetention = TimeSpan.FromDays(14),
        ValidateIndexesOnStartup = true
    })
    .EnableAmbientTransactionProvider(TransactionalWriteMode.RequireActiveTransaction));
```

The module registers the store under every role, plus `PostgreSqlInboxStoreRegistration`, the `PostgreSqlInboxSchemaInitializer` startup task (unless disabled), the `inbox.postgresql.schema` diagnostic probe, and the work signal (LISTEN/NOTIFY or polling).

`UseConnectionString` creates an `NpgsqlDataSource` the module owns. The data source is held by a `PostgreSqlDataSourceOwner<PostgreSqlInboxModule>` registered through a singleton factory and resolved by every store and work-signal registration. Disposing the service provider therefore disposes the data source and the LISTEN work signal. `UseDataSource` leaves ownership with the application, which disposes the data source itself. Omitting both options throws `InboxPostgreSqlStorageConfigurationException`.

`AddBatchAsync` inserts in chunks of 1,000 rows inside the caller's transaction. The module quotes the LISTEN channel name.

With `EnableAmbientTransactionProvider`, a scoped `PostgreSqlTransactionalInboxParticipant` resolves the store bound to the ambient `IPostgreSqlTransactionProvider` connection and transaction. `TransactionalWriteMode.RequireActiveTransaction` throws when no ambient transaction is present. `AllowImmediateCommit` falls back to the auto-commit singleton store, and is for development and tests only.

`PostgreSqlInboxStore` constructs as `(NpgsqlDataSource dataSource, PostgreSqlInboxStoreOptions? options = null)`. `UseExistingConnection(NpgsqlConnection connection, NpgsqlTransaction transaction)` returns an `ITransactionalInboxStore` bound to that pair. It throws `ArgumentException` when the transaction does not belong to the connection, and `InvalidOperationException` when the connection is not open. `PostgreSqlInboxStoreRegistration` (`NpgsqlDataSource`, `PostgreSqlInboxStoreOptions`) exposes `DataSource` and `Options`, and creates the same bound writer through `CreateTransactionalStore(connection, transaction)`.

`PostgreSqlInboxSchema` is the static schema API (`CurrentSchemaVersion = 1`, `SqlFiles`, `GetCreateScript`, idempotent `EnsureAsync`, `ValidateAsync` throwing `PostgreSqlSchemaDriftException` on drift). It supports three ownership models (migration-owned, explicit bootstrap, opt-in host bootstrap; migration-owned is recommended), with copy-paste migration paths in `PostgreSqlInboxSchemaSqlPaths` (`V1Create`, `V1EnsureIndexes`, `{{TokenName}}` placeholders).

The store falls back to per-item `AddAsync` for a batch containing any `Strict` item, instead of the bulk path. When the same message id is already stored for another tenant, the store throws `IdempotencyConflictException` regardless of conflict mode. Lease SQL compares visibility and expiry against `CURRENT_TIMESTAMP`, so leasing runs on the database clock. `GetSchemaInfoAsync` reports `CurrentSchemaVersion` as the recorded version when no version row exists yet.

`LiteBus.Outbox.Storage.PostgreSql` mirrors the inbox adapter. `PostgreSqlOutboxStore(NpgsqlDataSource, PostgreSqlOutboxStoreOptions? = null)` follows the same `UseExistingConnection` binding rules. The bound store carries the parent options, so `GetSchemaInfoAsync` reports the configured schema. `PostgreSqlOutboxStoreRegistration` carries `DataSource`, `Options`, and `CreateTransactionalStore`. `PostgreSqlOutboxSchema` (`CurrentSchemaVersion = 2`, `SqlFiles` of `V2Create`/`V1EnsureIndexes`/`V2Upgrade`, `GetCreateScript` rendering version 2, `EnsureAsync` upgrading a version 1 table automatically, drift-throwing `ValidateAsync`) offers the same three ownership models with `PostgreSqlOutboxSchemaSqlPaths` (`V1Create`, `V1EnsureIndexes`, `V2Create`, `V2Upgrade`).

Version 2 adds the nullable `handler` column for per-handler delivery. `EnsureAsync` applies `outbox/v2/upgrade.sql` (`ADD COLUMN IF NOT EXISTS handler`) when the inferred version is below current. It re-infers before the drift check. Migration-owned deployments run the upgrade script manually before a validate-only startup, and new tables are created from `outbox/v2/create.sql`.

`PostgreSqlOutboxNotifyChannel.ChannelName` is `litebus_outbox_work`, the LISTEN/NOTIFY channel the insert trigger signals. `PostgreSqlOutboxWorkSignal` (`IOutboxWorkSignal`, `IDisposable`) is constructed over the data source and listens on it, with polling-delay fallback through `WaitForWorkOrDelayAsync`.

`PostgreSqlTransactionalOutboxParticipant` (`PostgreSqlOutboxStoreRegistration`, `IOutboxStore`, `IPostgreSqlTransactionProvider?`, `TransactionalWriteMode`) resolves the ambient-transaction-bound store in `ResolveStore()`. It falls back to the singleton store only under `AllowImmediateCommit`. Under `RequireActiveTransaction` with no ambient transaction it throws `InvalidOperationException`. Module build throws `OutboxPostgreSqlStorageConfigurationException` when no data source was configured, and when `EnableAmbientTransactionProvider()` is combined with `UseMartenTransactionalOutbox()`.

`CreateInsertStatement(OutboxEnvelope, options?)` returns the schema-owned `PostgreSqlOutboxInsertStatement`. `Sql` is required and uses Npgsql `$1..$n` placeholders, ends with `ON CONFLICT DO NOTHING`, and has no `RETURNING`. `Values` uses `DBNull.Value` for absent values. `FormatSql(char placeholder)` rewrites markers to one placeholder char keeping casts.

Callers must resolve idempotency before queueing. When the same message id is already stored for another tenant, the store throws `IdempotencyConflictException` regardless of conflict mode.

`LiteBus.Storage.PostgreSql` holds the shared helpers. `IPostgreSqlSchemaLogger.Log(PostgreSqlSchemaLogLevel level, string message, Exception? exception = null)` is assigned through `PostgreSqlSchemaStoreOptions.Logger`. Schema operations log nothing when the logger is null, and applications bridge it to `ILogger<T>` without adding logging package references (`NullPostgreSqlSchemaLogger.Instance` is the shared no-op).

`IPostgreSqlTransactionProvider.TryGetCurrent(out NpgsqlConnection? connection, out NpgsqlTransaction? transaction)` supplies the ambient pair. Applications register a scoped implementation and activate it at unit-of-work start.

`PostgreSqlDataSourceFactory.CreateFromConnectionString(string connectionString)` throws `ArgumentException` when the connection string is blank, and is reserved for tests, samples, and small services. Applications that already use Npgsql 7+ pooling pass a shared `NpgsqlDataSource` instead.

`PostgreSqlIdentifier.Qualify(schemaName, tableName)` returns `"schema"."table"`, and `Quote(identifier)` doubles embedded quotes, throwing on blank values or embedded NUL. `IndexName` and `UnquotedIndexName` replace every character that is not a letter, digit, or `_` with `_`, and truncate names over 60 chars to 48 chars plus `_` plus an 8-hex `StableHash` (a process-invariant FNV-1a 32-bit hash, throwing on blank).

`PostgreSqlSchemaSqlFile` is a `(RelativePath, Purpose)` record whose content also ships under `sql/...` in the NuGet package. `PostgreSqlSchemaSqlPaths.Files` holds 6 entries, of which only `MetadataCreate` is for migration pipelines; the rest are runtime-internal. `PostgreSqlTableReference` (`SchemaName`, `TableName`, both validated non-blank) exposes `QualifiedName`, `QuotedQualifiedName`, and the `ForStore` / `ForMetadata` / `Create` factories.

`PostgreSqlWorkSignal` (`NpgsqlDataSource`, channel name; `IAsyncDisposable`, `IDisposable`) LISTENs on a dedicated connection. A `pollInterval <= 0` waits indefinitely. A disposed signal falls back to `Task.Delay`. A broken listener reconnects after 1 s. Disposal is idempotent without disposing the semaphores, so a draining waiter never sees `ObjectDisposedException`. Disposal completes even when the listener loop faulted while racing shutdown.

#### Entity Framework Core

```csharp
public sealed class AppDbContext : DbContext, IInboxDbContext, IOutboxDbContext
{
    public DbSet<InboxMessageEntity> InboxMessages => Set<InboxMessageEntity>();
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.GetModelBuilderConfiguration(new EntityFrameworkCoreInboxStoreOptions(),  EfCoreStorageProvider.PostgreSql);
        modelBuilder.GetModelBuilderConfiguration(new EntityFrameworkCoreOutboxStoreOptions(), EfCoreStorageProvider.PostgreSql);
    }
}

builder.Services.AddDbContextFactory<AppDbContext>((provider, options) =>
{
    options.UseNpgsql(connectionString);
    options.AddLiteBusInboxInterceptor(provider.GetRequiredService<LiteBusInboxSaveChangesInterceptor>());
    options.AddLiteBusOutboxInterceptor(provider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>());
});

liteBus.AddOutbox(outbox =>
{
    outbox.UseEntityFrameworkCoreStorage(ef => ef
        .UseDbContext<AppDbContext>()
        .EnableSaveChangesInterceptor()
        .EnforceTransactionalSetup()
        .UseOptions(new EntityFrameworkCoreOutboxStoreOptions { SchemaName = "messaging" }));
    outbox.UseInProcessDispatch();
    outbox.EnableOutboxProcessor();
});
```

The module registers `EfCoreInboxStore` / `EfCoreOutboxStore` as Singletons over `IDbContextFactory<TContext>`, and projects them onto every role interface plus `ITransactionalInboxStore` / `ITransactionalOutboxStore`. It also registers `IInboxWorkSignal` / `IOutboxWorkSignal` to the polling signal. `EnableSaveChangesInterceptor()` additionally registers the scoped `ITransactionalInbox<TContext>` / `ITransactionalOutbox<TContext>`.

`GetModelBuilderConfiguration` maps snake_case columns (`message_id`, `contract_name`, `contract_version`, `payload`, `created_at`, `visible_after`, `attempt_count`, `status`, `idempotency_key`, `lease_owner`, `lease_generation`, `lease_expires_at`, `last_error`, `correlation_id`, `causation_id`, `tenant_id`, `trace_context`, `completed_at` / `published_at`, `last_attempted_at`, `first_failed_at`, `dead_lettered_at`, `last_lease_owner`, `error_type`, plus `topic` and `handler` on the outbox). It creates a filtered unique index on `(tenant_id, idempotency_key)` where `idempotency_key IS NOT NULL`, a composite index on `(status, visible_after, lease_expires_at, created_at)`, a `created_at` index (`IX_LiteBus_Inbox_CreatedAt` / `IX_LiteBus_Outbox_CreatedAt`), and for the outbox a filtered `topic` index. `lease_generation` is a concurrency token, so a tracked renew or release that lost a lease race maps the conflict to `false` instead of overwriting the new holder. `ToTable` omits the schema for MySQL and SQLite.

Provider support (`EfCoreStorageProvider`, `EfCoreRelationalProviderNames`, `EfCoreRelationalTableQualifier`) covers PostgreSQL (`"schema"."table"`), SQL Server (`[schema].[table]`), MySQL (backtick table only), SQLite (quoted table only), and the EF in-memory provider. Raw lease SQL lives in `EfCorePostgreSqlLeaseSql`, `EfCoreSqlServerLeaseSql`, and `EfCoreMySqlLeaseSql`. An unsupported provider throws `EfCoreStorageNotSupportedException`.

`IInboxDbContext` is exactly `DbSet<InboxMessageEntity> InboxMessages`. `InboxMessageEntity` is a public sealed class with get/set properties mirroring the envelope (`Id`, `ContractName`, `ContractVersion`, `Payload`, `CreatedAt`, `VisibleAfter`, `AttemptCount`, `Status`, `IdempotencyKey`, `LeaseOwner`, `LeaseGeneration`, `LeaseExpiresAt`, `LastError`, `CorrelationId`, `CausationId`, `TenantId`, `TraceContext`, `CompletedAt`, `LastAttemptedAt`, `FirstFailedAt`, `DeadLetteredAt`, `LastLeaseOwner`, `ErrorType`).

`LiteBusInboxSaveChangesInterceptor` exposes `Enqueue(DbContext, InboxEnvelope)` to stage for the next `SaveChanges`, and `TryFindPending(DbContext, InboxEnvelope, out InboxEnvelope)`, which matches queued rows by id or tenant-scoped key. Its synchronous and asynchronous `SavingChanges` overrides flush into `IInboxDbContext.InboxMessages`, throwing `InvalidOperationException` when the saving context does not implement the interface. The flush maps every envelope field including `LeaseGeneration` and `ErrorType`, and collapses `ReturnExisting` duplicates by id and tenant-scoped key. `Strict` duplicates are preserved, so the save fails and aborts the caller unit of work.

The outbox interceptor mirrors the inbox interceptor over `OutboxEnvelope` and `IOutboxDbContext`. It maps `Handler`, `LeaseGeneration`, and `ErrorType` as well, and collapses `ReturnExisting` duplicates before and during flush. `Strict` duplicates are staged for rejection, so the tracker or unique index aborts the unit of work.

`TransactionalInbox<TContext>` resolves duplicates before staging, but only under `ReturnExisting` (tracked, pending, and persisted rows by id or tenant-scoped key). `Strict` always stages, so duplicate scoped keys surface as `DbUpdateException` on `SaveChanges`. The exception is strict batch entries reusing an id already staged in the same batch, which compare before staging. Identical submissions resolve to the staged envelope, while changed submissions throw `IdempotencyConflictException`.

`TransactionalOutbox<TContext>` resolves tracked, pending, and persisted rows under both modes before staging. `Strict` throws `IdempotencyConflictException` for conflicting content known at staging time, and compares `Handler` targeting as well. Only concurrent races surface as `DbUpdateException`.

`EfCoreInboxStore` and `EfCoreOutboxStore` each expose a test constructor over a caller-owned `Func<CancellationToken, Task<I...DbContext>>` context factory plus options (the store never disposes factory-returned contexts). Each also exposes `UseExistingDbContext<TContext>(TContext context)`, returning a transactional store whose `AddAsync` stages inserts and defers commit to the caller `SaveChanges`, and a no-op `DisposeAsync`. `AddAsync` strict-checks rows already staged in the context before staging. Leasing resolves the provider through `EfCoreProviderResolver` and, for relational providers, replaces the request timestamp with the database clock (`EfCoreDatabaseClock.GetUtcNowAsync`) before running provider lease SQL. Lease projections include the idempotency key.

`LiteBus.Storage.EntityFrameworkCore` holds the shared provider helpers. `EfCoreProviderResolver.Resolve(DbContext, EfCoreStorageProvider?)` returns the override when set, and otherwise maps `Database.ProviderName` (throwing `ArgumentNullException` or `NotSupportedException`). `ResolveProviderName(string?)` maps the five `EfCoreRelationalProviderNames` values, and throws `EfCoreStorageNotSupportedException` otherwise. `GetRecommendedDefaultSchema` returns `dbo` for SQL Server, `public` for PostgreSQL, empty for MySQL and SQLite, and `dbo` for anything else including InMemory.

`EfCoreRelationalModelColumnTypes.GetPayloadColumnType` always returns `TEXT`, while `GetTraceContextColumnType` returns `jsonb`, `nvarchar(max)`, `json`, or `TEXT` per provider (unknown providers throw). `ConfigureJsonPayloadColumn<TEntity>` and `ConfigureJsonTraceContextColumn<TEntity>` apply `HasColumnType` unless the provider is null, and return the builder for chaining. `EfCoreRelationalTableQualifier.Qualify(provider, schemaName, tableName)` renders `"schema"."table"` (PostgreSQL, InMemory), `[schema].[table]` (SQL Server), backtick `` `table` `` (MySQL), or quoted `"table"` (SQLite). Schema is required except for MySQL and SQLite, and unknown providers throw.

#### Marten

```csharp
liteBus.AddOutbox(outbox =>
{
    outbox.UsePostgreSqlStorage(pg => pg
        .UseConnectionString(connectionString)
        .UseMartenTransactionalOutbox());
    outbox.UseInProcessDispatch();
    outbox.EnableOutboxProcessor();
});
```

Package `LiteBus.Outbox.Storage.Marten` registers a scoped `ITransactionalOutbox` (`MartenTransactionalOutbox`) backed by the current Marten `IDocumentSession`, so documents and outbox rows commit in the same `SaveChangesAsync`. `UseMartenTransactionalOutbox(this PostgreSqlOutboxModuleBuilder)` sets the factory that resolves the scoped `IDocumentSession` plus `IOutboxEnvelopeFactory` and `PostgreSqlOutboxStoreRegistration`. `MartenTransactionalOutbox` takes `(IDocumentSession session, IOutboxEnvelopeFactory envelopeFactory, PostgreSqlOutboxStoreRegistration registration)` and builds its committed-row lookup store from the registration data source and options. The application registers Marten itself with `AddMarten` against the same database. LiteBus does not register Marten.

Each envelope is queued on the session with `QueueSqlCommand`, using the schema-owned insert from `PostgreSqlOutboxSchema.CreateInsertStatement(envelope, options)`. That insert targets the current schema version and the same columns `PostgreSqlOutboxStore` writes. The row is written only when the session saves, and disposing the session without saving stores nothing. `PostgreSqlOutboxInsertStatement` carries `Sql` (Npgsql `$1` through `$n` placeholders), `Values` (`DBNull.Value` for absent values), and `FormatSql(placeholder)` to rewrite markers for the executing unit of work (`?` for Marten).

The statement ends with `ON CONFLICT DO NOTHING` and has no `RETURNING` clause, so idempotency is resolved before queueing. One committed-row lookup on a separate connection plus in-session tracking by message id and tenant-scoped idempotency key resolve `ReturnExisting` and `Strict` duplicates against earlier items. A differing `Strict` submission throws `IdempotencyConflictException`, and a concurrent insert that wins the race is absorbed by the conflict clause (the receipt still says `Enqueued`). This cannot be combined with `EnableAmbientTransactionProvider`. Module build throws because both register `ITransactionalOutbox`.

#### Commands PostgreSQL idempotency store

```csharp
liteBus.AddCommands(commands => commands
    .EnableIdempotency()
    .UsePostgreSqlIdempotencyStore(pg => pg
        .UseConnectionString(connectionString)
        .UseOptions(new PostgreSqlIdempotencyStoreOptions { ClaimTimeout = TimeSpan.FromMinutes(10) })));
```

Package `LiteBus.Commands.Storage.PostgreSql` registers `PostgreSqlIdempotencyStore` as the Singleton `IIdempotencyStore` through a command child module (`PostgreSqlIdempotencyModule`, `IRequires<CommandModule>`). It also registers `PostgreSqlIdempotencyStoreRegistration` (data source plus options), the `PostgreSqlIdempotencySchemaInitializer` startup task (unless disabled), and the `idempotency.postgresql.schema` probe. The builder offers `UseDataSource` (not owned) / `UseConnectionString` (owned through a container-owned `PostgreSqlDataSourceOwner`), `UseOptions`, and `DisableSchemaInitialization()`. Omitting the data source throws `DurableStorageConfigurationException`.

`PostgreSqlIdempotencySchema` is the static schema API (`CurrentSchemaVersion = 1`, `ComponentName = "idempotency"`, `SqlFiles`, `GetCreateScript`, idempotent `EnsureAsync`, `ValidateAsync`) with canonical paths in `PostgreSqlIdempotencySchemaSqlPaths` (`V1Create`, `V1EnsureIndexes`). Version 1 holds the keys table with the `fingerprint` and `claim_token` columns and a `completed_at` retention index.

#### Commands recurring PostgreSQL lease

```csharp
liteBus.AddCommands(commands => commands
    .AddRecurring(() => new SendDueRemindersCommand(), host => host.RequireLease = true)
    .UsePostgreSqlRecurringLease(pg => pg.UseConnectionString(connectionString)));
```

Package `LiteBus.Commands.Recurring.Storage.PostgreSql` registers `PostgreSqlRecurringCommandLease(NpgsqlDataSource, PostgreSqlRecurringLeaseOptions? = null, ILogger? = null)` as the Singleton `IRecurringCommandLease` through a command child module, with the registration, the schema initializer startup task, and the `recurring.postgresql.schema` probe. A missing data source throws `DurableStorageConfigurationException`.

Acquisition inserts or takes over a lease row keyed by name with `expires_at` on the database clock, returning an `IRecurringCommandLeaseHandle` or null. Only an expired row can be taken, so re-acquiring a live lease, even by the holder, is refused.

The holder renews in the background every `EffectiveRenewInterval`. Renewal is ownership-checked, so a stolen handle releases nothing and a dropped table stops renewal without resurrecting the row. A renewal that finds the lease taken, or that fails, signals `LeaseLost` on the handle. Dispose deletes the row only when the handle still owns it, without signalling loss.

`PostgreSqlRecurringLeaseSchema` (`CurrentSchemaVersion = 1`, `ComponentName = "recurring"`, `SqlFiles`, `GetCreateScript`, `EnsureAsync`, `ValidateAsync`) with `PostgreSqlRecurringLeaseSchemaSqlPaths` (`V1Create`, `V1EnsureIndexes`) owns the `litebus_recurring_leases` table and its `expires_at` index.

#### Commands Marten

```csharp
liteBus.AddCommands(commands => commands
    .EnableIdempotency()
    .UseMartenUnitOfWork()
    .UseMartenIdempotencyStore(options => options.ClaimTimeout = TimeSpan.FromMinutes(10)));
```

Package `LiteBus.Commands.Storage.Marten` brings the command axis to Marten. `MartenUnitOfWork(IDocumentSession)` commits the scoped session. `UseMartenUnitOfWork()` delegates to the command `UseUnitOfWork` hook, and query mediations reuse the same session by calling the query `UseUnitOfWork` hook with `MartenUnitOfWork` directly. The package ships no query extension, so a command storage package does not depend on the query axis.

`MartenIdempotencyStore(IDocumentStore, MartenIdempotencyStoreOptions? = null, TimeProvider? = null)` keeps `MartenIdempotencyRecord` documents in its own serializable sessions, with the same four claim outcomes and takeover semantics as the PostgreSQL store. When the store loses a race, it retries on SQL states 23505, 40001, and 40P01.

`MartenAuditTrail(IDocumentSession, IDocumentStore)` stages each successful audit record in the handler session so it commits with the change, while denial and failure records are written out of band so a rolled-back session cannot lose them. `MartenAuditRecord` is the public flattened audit document (`Id`, `Action`, actor fields, `Outcome`, `OccurredAt`, `Duration`, `Category`, `TargetKind`, `TargetId`, `Reason`, `FailureCode`, `MessageType`, `CorrelationId`, `TenantId`, `Properties`). The application registers Marten itself with `AddMarten`. LiteBus does not register Marten, runs no schema initializer, and reports no probe, because Marten owns its schema.

### 4.20 Modules, ordering and the host manifest

```csharp
public interface IModule
{
    void Build(IModuleConfiguration configuration);
}

public interface ICompositeModule : IModule
{
    CompositeModuleBuildOrder BuildOrder => CompositeModuleBuildOrder.ParentFirst;
    void DeclareChildren(Action<IModule> registerChild);
}

public interface IRequires<TModule> where TModule : IModule { }
```

`ModuleRegistry.Register(IModule)`:

* Throws `ModuleCompositionException` after `BuildOrder()` has been called ("Cannot register modules after BuildOrder() has been called.").
* Module identity is the **concrete type**. Registering two instances of one module type throws.
* A composite's `DeclareChildren` runs during `Register`, so the builder action must run there. Children are staged recursively, and an ordering edge is added in the direction implied by `BuildOrder`. `ParentFirst` makes each child depend on the parent, and `ChildrenFirst` makes the parent depend on each child. An undefined `BuildOrder` value throws.
* `BuildOrder()` performs a DFS topological sort over `IRequires<>` plus the implicit composite edges, ordering each node's dependencies by full type name for determinism. A cycle throws `ModuleCompositionException` naming the cycle path. A missing required module throws an exception naming both modules. The signature is `IReadOnlyList<ModuleDescriptor> BuildOrder()`, returning descriptors sorted so dependencies appear before dependents. A second call returns the cached list, and an empty registry returns an empty list while still freezing the registry.

`ModuleDescriptor` is a `public readonly record struct ModuleDescriptor(IModule Module, IReadOnlySet<Type> Dependencies)` with `Type ModuleType`, `bool HasDependencies`, `static ModuleDescriptor Create(IModule module)`, and `bool DependsOn(Type moduleType)`. `Create` reads the module's `IRequires<>` interfaces, and throws `ArgumentNullException` on null.

`IModuleConfiguration`:

| Member | Behaviour |
| --- | --- |
| `DependencyRegistry` | The container-neutral registry. |
| `StartupTasks`, `BackgroundServices`, `DiagnosticChecks` | Snapshots of the host manifest being built. |
| `RegisterStartupTask(Type)` | Must implement `IStartupTask` and be a concrete class. A type that also implements `IBackgroundService` throws `ArgumentException` that points to `RegisterBackgroundService`. Duplicates are ignored without reordering. |
| `RegisterBackgroundService(Type)` | Must implement `IBackgroundService`. A type that also implements `IStartupTask` throws `ArgumentException` that points to `RegisterStartupTask`. Duplicates are ignored without reordering. |
| `RegisterDiagnosticCheck(Type, string name)` | Must be a concrete class implementing `IDiagnosticCheck`. `name` must be non-whitespace (throws `ArgumentException` otherwise). Re-registering the same type with the same name is ignored. Re-registering it with a different name throws `ModuleCompositionException`, as does registering a name already taken by another type. |
| `CompositionValidations` | Snapshot of validations that run after every module has built. |
| `RegisterCompositionValidation(Action)` | Runs `validate` once the registry is complete. A rule reports a problem by throwing. |
| `GetContext<T>()` | Throws `ModuleCompositionException` when the context is missing (module ordering problem). |
| `SetContext<T>(T)` | Throws when a different instance is already registered for that type. |
| `TryGetContext<T>(out T?)` | Non-throwing probe. |
| `GetOrCreateContext<T>(Func<T>)` | Creates on first use. A factory returning `null` throws. |

The composition summary, `LiteBusCompositionSummary`, is registered as a Singleton by `AddMessaging` and filled in after every module has built, because none of the counts exist before then. Resolve it and log `ToString()` once at startup:

```text
LiteBus composed 166 messages (140 commands, 26 queries); open generic AuthorizationGuard`1 closed over 140 messages;
auditing on, trail MartenAuditTrail (Scoped), actor resolver registered; required declarations RequiredAuthorization of
every IActingAccountCommand; 2 composition checks
```

| Member | Reports |
| --- | --- |
| `MessageCount` | Total registered messages. |
| `MessageCountsByAxis` | Counts per axis name. An axis the host does not compose is absent rather than zero, so the summary says what was wired rather than what was not. |
| `OpenGenericHandlers` | Each `OpenGenericClosure(HandlerName, MessageCount)`. A count of zero means the handler fits nothing and never runs. |
| `DuplicateStageCoverages` | Each `DuplicateStageCoverage(HandlerType, MessageType, DirectContractType, IndirectContractType)`. One handler type covers one message's pre-stage, post, error, or completion entries twice, once directly and once through a base type. Empty when every stage holds at most one entry per handler type. `ToString()` appends one `; duplicate stage coverage {Handler} on {Message} (direct {Direct}, indirect {Indirect})` segment per entry, ordered by handler then message name. |
| `RequiredDeclarations` | Each policy as `"{ValueType} of {scope}"`. |
| `AuditingEnabled`, `AuditTrail`, `AuditActorResolverRegistered` | The audit configuration, with the trail's lifetime. |
| `AuditRecordWriter` | The replacement record writer type and lifetime, or `null` when the built-in writer is in use. It is appended to the startup line when set. |
| `CompositionChecks` | How many application `ValidateComposition` checks run. |

The open generic line is the reason the summary exists. Adding one file to a scanned assembly inserts a pipeline stage into every message it fits, and nothing in the composition code shows it. A count that changes when the set changes is what makes that reviewable. The duplicate-coverage line earns its place the same way. The pipeline still runs both entries, and the summary makes the double run visible instead of silent.

`LiteBusHostManifest` (`StartupTasks`, `BackgroundServices`, `DiagnosticChecks`) is built by `LiteBusHostManifest.FromConfiguration(...)` and registered as a Singleton. `LiteBusHostOrchestrator` runs every `IStartupTask.RunAsync` sequentially during `StartAsync`, then starts all `IBackgroundService.ExecuteAsync` loops in `ExecuteAsync`. The orchestrator swallows an expected shutdown cancellation, while any other exception calls `IHostApplicationLifetime.StopApplication()` and rethrows.

The registration conflict policy (`DependencyRegistrationTracker`) works as follows. `Register` enforces one descriptor per service type. An equal duplicate is ignored, and a different descriptor for the same service type throws `ModuleCompositionException` ("Each LiteBus module may register a given service type only once."). `RegisterCollection` allows multiple implementations resolved through `IEnumerable<T>`, but single and collection bindings for one service type are mutually exclusive in both directions ("either as a single binding or as collection items, not both"). `RegisterCollection` only skips the one-descriptor-per-type check, not this exclusivity check.

A descriptor with `IsCollectionRegistration == true` passed to `Register` throws `ModuleCompositionException` that directs to `RegisterCollection`. A descriptor not created via `DependencyDescriptor.ForCollection` passed to `RegisterCollection` throws the same exception. Both DI adapters enforce the identical rule. Order of first registration is preserved for deterministic container translation.

`IDependencyRegistry` extends `IReadOnlyCollection<DependencyDescriptor>`, so it exposes `Count` and enumerates accepted descriptors in first-registration order. `DependencyRegistry` is a `public sealed class` with an implicit public parameterless constructor and `public int Count` reporting the number of unique tracked descriptors.

`ServiceProviderExtensions` (`LiteBus.Runtime.Abstractions.Extensions`) offers `public static T GetRequiredService<T>(this IServiceProvider serviceProvider)` and `public static object GetRequiredService(this IServiceProvider serviceProvider, Type serviceType)`. Both resolve without a container dependency, and throw `LiteBusDependencyResolutionException` when the service is not registered.

Register storage modules before inbox, outbox, or ingress modules when multiple startup tasks are present, so schema work appears first in the startup task manifest. The orchestrator runs tasks sequentially in registration order.

### 4.21 Diagnostics and health

```csharp
public interface IDiagnosticCheck
{
    string Name { get; }
    Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default);
}
```

`DiagnosticCheckExecution.CheckAsync(descriptor, check, ct)` validates that `check.Name` matches the manifest descriptor and throws `DiagnosticCheckNameMismatchException` (with `ImplementationType`, `DescriptorName`, `CheckName`) otherwise.

`DiagnosticCheckRunner.RunAsync(manifest, services, failHealthWhenNoProbes, [options], ct)` resolves each probe from the provider and runs the probes under a `SemaphoreSlim(MaxParallelism)` (default 4), with a per-probe `Timeout` (default 5 seconds) and an overall `RunTimeout` budget. A null budget (the default) is derived as `waves * Timeout` where `waves = ceil(probeCount / MaxParallelism)`, or `Timeout.InfiniteTimeSpan` on tick overflow. An explicit `RunTimeout` replaces the computed budget. A probe that throws fails only its own outcome.

Null `manifest`, `services`, or `options` throws `ArgumentNullException`. Non-positive `MaxParallelism` or `Timeout` ticks throws `ArgumentOutOfRangeException`, as does a negative `RunTimeout`. On explicit-budget expiry the run token is cancelled immediately. On computed-budget expiry the runner first waits a 1-second grace period to let in-flight per-probe timeouts finish, then cancels. Every unfinished probe becomes `Unhealthy` with description "The diagnostic run exceeded its budget.":

| Situation | Probe outcome |
| --- | --- |
| Probe type not resolvable | `Unhealthy`, "The diagnostic check is not registered." |
| Probe exceeded `Timeout` | `Unhealthy`, "The diagnostic check timed out." (the linked token is cancelled) |
| Probe `Name` differs from its manifest descriptor | `Unhealthy` with the mismatch message |
| Run budget expired with probes still running | `Unhealthy`, "The diagnostic run exceeded its budget." (the probes are cancelled) |
| Probe threw | `Unhealthy`, "The diagnostic check failed." |
| Caller cancelled | `OperationCanceledException` propagates |
| Manifest has no probes and `failHealthWhenNoProbes` is true | one synthetic `litebus.probes` outcome, `Degraded` |
| Manifest has no probes and the flag is false | `Healthy` with an empty probe list |

The run aggregates probe outcomes. When all probes are `Healthy` the run is `Healthy`. When any probe is `Unhealthy` the run is `Unhealthy`. Otherwise the run is `Degraded`.

LiteBus ships these probes:

| Name | Probe | Reports |
| --- | --- | --- |
| `litebus.audit.trail` | `AuditTrailDiagnosticCheck` | `Unhealthy` when auditing is enabled but no `IAuditTrail` is registered. `Healthy` with `component`, `trailRegistered`, `trailType`, `trailIsSingleton`, `actorResolverRegistered` when both trail and actor resolver are registered. `Degraded` when the trail is registered but no `IAuditActorResolver` is. `Healthy` with `recordWriter` when a replacement record writer is registered. |
| `litebus.idempotency.store` | `IdempotencyStoreDiagnosticCheck` | `Unhealthy` when idempotency is enabled but no `IIdempotencyStore` is registered. Otherwise `Healthy` with `component`, `storeRegistered`, `storeType`. |
| `inbox.postgresql.schema` | `PostgreSqlInboxSchemaDiagnosticCheck` | Expected vs recorded inbox schema version. `Healthy` data keys: `component`, `expectedVersion`, `schemaName`, `tableName`; drift adds `actualVersion`. |
| `outbox.postgresql.schema` | `PostgreSqlOutboxSchemaDiagnosticCheck` | Expected vs recorded outbox schema version. Same data keys as the inbox schema probe. |
| `idempotency.postgresql.schema` | `PostgreSqlIdempotencySchemaDiagnosticCheck` | Expected vs recorded idempotency keys schema version. Same data keys as the inbox schema probe. |
| `recurring.postgresql.schema` | `PostgreSqlRecurringLeaseSchemaDiagnosticCheck` | Expected vs recorded recurring leases schema version. Same data keys as the inbox schema probe. |
| `transport.amqp.connectivity` | `AmqpConnectivityDiagnosticCheck` | Broker connection reachability. |
| `transport.kafka.connectivity` | `KafkaConnectivityDiagnosticCheck` | Cluster describe within `ConnectivityCheckTimeout`. |
| `transport.sqs.connectivity` | `AwsSqsConnectivityDiagnosticCheck` | `Degraded` ("SQS connectivity is not configured; set ConnectivityCheckQueueUrl.") when `ConnectivityCheckQueueUrl` is blank. Otherwise the probe reads the `QueueArn` attribute (`Healthy` "SQS queue attributes are available." / `Unhealthy` "SQS queue attributes are unavailable."). SDK exception text never surfaces. |
| `transport.azure_service_bus.connectivity` | `AzureServiceBusConnectivityDiagnosticCheck` | `Degraded` ("Azure Service Bus connectivity is not configured; set ConnectivityCheckTarget.") when `ConnectivityCheckTarget` is null, because opening a client alone establishes no broker connection. Otherwise the probe non-destructively peeks 1 message (peeking locks and settles nothing). |

`W3CTraceContextParser.TryParse(string?, out ActivityContext)` accepts either a bare `traceparent` string or a JSON object with `traceparent` and optional `tracestate`, and is used by `MessageProcessorDiagnostics.TryGetParentActivityContext` so a durable dispatch continues the originating trace.

The ASP.NET Core health check (`LiteBus.Extensions.Diagnostics.HealthChecks`) runs the manifest probes through `DiagnosticCheckRunner` in `LiteBusHealthCheck`, and maps the aggregate status. `Healthy` with zero probes maps to `Healthy("No LiteBus diagnostic probes are registered.")` with no data. `Healthy` with probes maps to `Healthy("All LiteBus diagnostic probes succeeded.", data)`. `Unhealthy` maps to `Unhealthy("One or more LiteBus diagnostic probes reported unhealthy status.", data)`. Anything else maps to `Degraded`, with "No LiteBus diagnostic probes are registered." when the only probe is the synthetic `litebus.probes` outcome, and "One or more LiteBus diagnostic probes reported degraded status." otherwise.

Report data is one entry `data["probes"]`, an array of `{ name, status (DiagnosticStatus.ToString()), description, data }` dictionaries per probe.

### 4.22 Operator HTTP endpoints (`LiteBus.Extensions.AspNetCore`)

```csharp
builder.Services.AddLiteBusManagement(options => options.AuthorizationPolicy = "ops");
var app = builder.Build();
app.AddLiteBusManagementEndpoints();   // or AddLiteBusManagementEndpoints(explicitOptions)
```

`AddLiteBusManagement(this IServiceCollection services, Action<LiteBusManagementOptions>? configure = null)` registers one `LiteBusManagementOptions` as Singleton, using a default instance when `configure` is null and the configured instance otherwise, and returns the collection for chaining. `AddLiteBusManagementEndpoints(this IEndpointRouteBuilder endpoints, LiteBusManagementOptions? options = null)` uses the explicit options when given, the DI-registered `LiteBusManagementOptions` when those exist, and a new default instance otherwise. It returns the route builder for chaining.

Routes are grouped under `/{RoutePrefix}/inbox` and `/{RoutePrefix}/outbox`. An axis with no registered manager gets a fallback returning `404 Not Found` with the text `"Inbox is not configured."` / `"Outbox is not configured."`. The prefix is trimmed of `/`, and a prefix that trims to empty roots routes at the application base (`/{segment}` instead of `/{prefix}/{segment}`).

| Method and route (per axis) | Handler |
| --- | --- |
| `GET /messages` | `QueryAsync` with `[AsParameters]` filter binding. `ToPageRequest` throws `ArgumentOutOfRangeException` (a 400) when `PageSize <= 0` or `PageSize > MaxPageSize`. It is not clamped. Bound `MessageIds` are validated against `MaxBulkMessageIds` before reaching the manager. |
| `GET /messages/{messageId:guid}` | `GetMessageAsync`; `404` when absent |
| `POST /messages/requeue` | `RequeueAsync` with `{ "messageIds": [...] }`; validated against `MaxBulkMessageIds` |
| `POST /messages/requeue-dead-letters` | `RequeueDeadLettersAsync` |
| `DELETE /messages` | `PurgeAsync` with query-string filter plus optional `{ "confirm": true }` body |
| `GET /status-counts` | `GetStatusCountsAsync` |
| `GET /schema` | `GetSchemaInfoAsync` |
| `GET /retention/status` | `GetRetentionStatusAsync` |
| `POST /retention/purge` | `RunRetentionPurgeAsync` |
| `GET /processor/state` | `IInboxProcessorControl.State` / `IOutboxProcessorControl.State` |
| `POST /processor/pause` | `PauseAsync` |
| `POST /processor/resume` | `ResumeAsync` |
| `POST /processor/drain` | `DrainAsync`. The timeout is `timeoutSeconds is > 0 ? TimeSpan.FromSeconds(timeoutSeconds.Value) : DefaultDrainTimeout` (a missing or non-positive value takes the default). A timeout above `MaxDrainTimeout` throws `ArgumentOutOfRangeException` (a 400). It is not clamped. |

One shared route, `GET /{RoutePrefix}/health`, runs the manifest probes through `DiagnosticCheckRunner` with `FailHealthWhenNoProbes` and `DiagnosticChecks`. It returns 200 with a JSON array of `{ name, status, description, data }` per probe when the aggregate status is `Healthy`, and 503 with the same array shape otherwise.

Processor routes return `404` with `"Inbox processor is not enabled."` / `"Outbox processor is not enabled."` when the processor control is not registered. An outbox retention purge with no positive `Retention` policy returns the operator-safety rejection rather than `500`. The inbox purge returns zero in the same case.

Query-string binding types are all `public sealed class` with `[FromQuery]` init properties. `InboxMessageQueryBinding` binds `Guid? MessageId`, `Guid[]? MessageIds`, `InboxStatus[]? Statuses`, `string? ContractName`, `string? CorrelationId`, `string? CausationId`, `string? TenantId`, `DateTimeOffset? CreatedAfter`, `DateTimeOffset? CreatedBefore`, `int PageSize = 50`, `string? Cursor`, with `InboxMessageFilter ToFilter()` and `InboxMessagePageRequest ToPageRequest(int maxPageSize)`. `InboxMessagePurgeBinding` binds the same set without `PageSize` and `Cursor`, with `InboxMessageFilter ToFilter()`. `OutboxMessageQueryBinding` binds the inbox query set plus `string? Topic`, with `OutboxMessageFilter ToFilter()` and `OutboxMessagePageRequest ToPageRequest(int maxPageSize)`. `OutboxMessagePurgeBinding` binds the inbox purge set plus `string? Topic`, with `OutboxMessageFilter ToFilter()`.

Query and purge endpoints validate the bound `MessageIds` against `MaxBulkMessageIds` before reaching the manager, throwing `ArgumentOutOfRangeException` (a 400) when the count exceeds the limit, on both axes.

Failures map to RFC 7807 problems via `Results.Problem`, with `instance` set to the request path and `extensions["traceId"]` set to the trace identifier. `ArgumentException` maps to 400 `urn:litebus:management:invalid-request` ("Invalid management request"), `InboxManagementException`/`OutboxManagementException` to 400 `urn:litebus:management:safety-rejection` ("Management request rejected"), and anything else to 500 `urn:litebus:management:operation-failed` ("Management operation failed"). A request-aborted `OperationCanceledException` is rethrown, never mapped.

Boundary failures are logged through `ILoggerFactory.CreateLogger("LiteBus.Management")` with the trace identifier, request method, path, and exception. Invalid input logs `EventId 4001` (Warning), operator-safety rejection logs `EventId 4002` (Warning), and unexpected failure logs `EventId 5001` (Error). Every route gets `RequireAuthorization()` (or `RequireAuthorization(policy)`) unless `AllowAnonymousManagement` is set.

Option validation at map time throws `ArgumentException` / `ArgumentOutOfRangeException` for a blank `RoutePrefix`, non-positive `MaxPageSize`, `MaxBulkMessageIds`, `DefaultDrainTimeout`, `MaxDrainTimeout`, or diagnostic limits, a negative `RunTimeout`, and when `DefaultDrainTimeout > MaxDrainTimeout`.

Idempotency-key endpoints use `WithIdempotencyKey<TBuilder>(this TBuilder builder, Action<IdempotencyKeyFilterOptions>? configure = null)` (`TBuilder : IEndpointConventionBuilder`, so route groups work). It registers an endpoint filter that reads the idempotency key request header into `HttpContext.Items`. `IdempotencyKeyFilterOptions` carries `HeaderName` (default `Idempotency-Key`), `RequireHeader` (default true, where a missing or blank header short-circuits with a 400 `urn:litebus:idempotency:missing-key` problem), `MaxKeyLength` (default 255, where a longer key short-circuits with a 400 `urn:litebus:idempotency:key-too-long` problem), and `TenantResolver` (`Func<HttpContext, string?>?`, default null, where a multi-tenant endpoint sets it so two tenants sharing one key string do not share a store row). The filter uses the first value when several arrive, and trims values.

`CommandMediationSettings.WithIdempotencyKey(settings, context)` carries the validated key and the resolved tenant from the filter into the mediation settings items. `IdempotencyKeyGuard : ICommandGuard<ICommand>` (constructed with the `IExecutionContext`) passes the key to the idempotency shortcut as a `SuppliedIdempotencyKey`, setting it only when no stage supplied one. Indirect guards run before command-specific direct guards, so a command-specific guard that derives its own key keeps precedence only by overwriting unconditionally. One that sets only when absent silently loses to the header.

`MediationResult.ToHttpResult(result, httpContext, onSuccess)` and `MediationResult<T>.ToHttpResult(result, httpContext, onSuccess)` answer a success through `onSuccess` (the recorded result for an idempotent replay) and map refusals to problems. An in-flight key maps to 409 `urn:litebus:idempotency:in-flight`, a fingerprint mismatch to 422 `urn:litebus:idempotency:fingerprint-mismatch`, any other denial to 403 with the pipeline reason, and an invalid command to 400 with the collected reason.

### 4.23 OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddLiteBusInboxInstrumentation()
        .AddLiteBusOutboxInstrumentation()
        .AddLiteBusTransportInstrumentation())
    .WithMetrics(metrics => metrics
        .AddLiteBusInboxMetrics()
        .AddLiteBusOutboxMetrics()
        .AddLiteBusTransportMetrics()
        .AddLiteBusAmqpMetrics());
```

Activity sources are `LiteBus.Inbox`, `LiteBus.Outbox`, and `LiteBus.Transport`. Spans are `inbox.processor.pass`, `inbox.processor.message`, `outbox.processor.pass`, and `outbox.processor.message` (each message span is tagged `litebus.message_id` and parented on the stored trace context), plus transport `send` and `process` spans.

Meters are `LiteBus.Inbox`, `LiteBus.Outbox`, and `LiteBus.Transport`.

| Instrument | Meter | Kind | Notes |
| --- | --- | --- | --- |
| `litebus.inbox.queue.depth` | Inbox | observable gauge | Tagged `litebus.inbox.status`. |
| `litebus.inbox.processor.state` | Inbox | observable gauge | Loop state. |
| `litebus.inbox.processor.lease_lost` | Inbox | counter | Lease renewal failed during dispatch. |
| `litebus.inbox.processor.persist_skipped` | Inbox | counter | Terminal persist skipped an envelope. |
| `litebus.inbox.processor.persist_rejected` | Inbox | counter | Terminal persist rejected an update. |
| `litebus.inbox.processor.persist_failed` | Inbox | counter | Terminal persist threw and the pass continued. |
| `litebus.inbox.processor.leases_acquired` | Inbox | counter | Envelopes leased. |
| `litebus.inbox.processor.dispatch_duration` | Inbox | histogram | Milliseconds. |
| `litebus.inbox.cleanup.errors` | Inbox | counter | Retention cleanup failures. |
| `litebus.inbox.diagnostics.unavailable` | Inbox | counter | Queue-depth probe could not read the store. |
| `ingress.ack_failed_after_accept` | Inbox | counter | Broker ack failed after a successful inbox accept. |
| `litebus.outbox.*` (same suffixes as inbox, with `litebus.outbox.status` for queue depth) | Outbox | as above | Same semantics for publication. |
| `litebus.transport.circuit_breaker.open` | Transport | observable gauge | Tagged `litebus.transport.broker`. |
| `litebus.transport.circuit_breaker.failure_count` | Transport | observable gauge | Tagged `litebus.transport.broker`. |
| `litebus.transport.circuit_breaker.failure_recorded` | Transport | counter | One per failure observed while the circuit stays closed; untagged. |
| `litebus.transport.circuit_breaker.opened` | Transport | counter | One per circuit opening; untagged. |

Mediation telemetry (`LiteBusMediationTelemetry`, `LiteBus.Messaging`) uses `LiteBus.Mediation` for both the activity source and the meter. Register it with an exporter through `LiteBus.Messaging.Extensions.OpenTelemetry`. Call `AddLiteBusMediationInstrumentation()` on a `TracerProviderBuilder` and `AddLiteBusMediationMetrics()` on a `MeterProviderBuilder`.

| Instrument | Kind | Tags | Purpose |
| --- | --- | --- | --- |
| `litebus.mediation.duration` | histogram (ms) | `litebus.message`, `litebus.outcome` | How long a mediation took, per message and ending. |
| `litebus.mediation.count` | counter | `litebus.message`, `litebus.outcome`, `litebus.code` | One per completed mediation. It answers what is being denied and why without a log line. |
| `litebus.mediation.stage.duration` | histogram (ms) | `litebus.message`, `litebus.stage` | The duration of one pre stage. Recorded only when opted in. |
| `litebus.mediation.decisions` | counter | `litebus.message`, `litebus.stage`, `litebus.outcome`, `litebus.decided_by` | One per mediation a stage stopped. It answers which stage stopped the mediation as a metric filter instead of a stack trace. |

Each mediation records one span, named `mediate {MessageType}` and tagged with `litebus.message`, `litebus.outcome` and, when a decision supplied one, `litebus.code`. Only a `Failed` outcome sets `ActivityStatusCode.Error`, because a denial is a decision, and colouring every refused request red makes a trace view useless for finding the requests that broke.

`MediationTelemetryOptions` decides what is recorded. Set it through `MessageModuleBuilder.UseTelemetry`:

| Property | Default | Note |
| --- | --- | --- |
| `Spans` | `true` | An `ActivitySource` with no listener does almost no work. |
| `Metrics` | `true` | The duration histogram, the outcome counter, and the decision counter. |
| `StageSpans` | `false` | A child span per pre stage on every message is not free, and mediation volume is orders of magnitude above inbox volume. |
| `StageMetrics` | `false` | One measurement per stage per message is four to five times the traffic of one per message. |

`MediationTelemetryOptions.Disabled` turns everything off, for a benchmark or a host exporting the same measurements through its own instrumentation. The options are process-wide, because an `ActivitySource` and a `Meter` are. `MediationTelemetry.Options` is always assigned at build (the builder value or a new `MediationTelemetryOptions`), so a composition without `UseTelemetry` resets the process-wide options to defaults instead of inheriting a previous composition's settings.

Transport span tags (`LiteBusTransportTelemetry`) are `messaging.system`, `messaging.operation.name` (`send` / `process`), `messaging.operation.type`, `messaging.destination.name`, `messaging.message.id`, `messaging.message.conversation_id`, `messaging.kafka.message.key`, `messaging.rabbitmq.destination.routing_key`, `litebus.transport.route`, and `litebus.transport.redelivered`. `TransportMessagingSystems` values are `aws_sqs`, `kafka`, `litebus_in_memory`, `litebus` (fallback for custom transports), `rabbitmq`, and `servicebus`. There is no `azure_service_bus` messaging-system value. ASB adapters set `MessagingSystem = ServiceBus` (`"servicebus"`) on both consume and send paths, while `azure_service_bus` is only the `TransportBrokerIdentity` broker tag.

`TransportActivityMetadata` is a `public readonly record struct` carrying what one transport activity records: required `MessagingSystem` (`string`), `Destination`/`Route`/`MessageId`/`CorrelationId` (`string?`, null), and `Redelivered` (`bool`, false). `TransportTracing` is a `public static class` with a `static readonly ActivitySource ActivitySource` (name `LiteBus.Transport`). `StartPublishActivity(TransportActivityMetadata)` starts a Producer `send` span, and returns `null` with no listener.

`StartConsumeActivity(TransportMessage)` starts a Consumer `process` span, where a blank `MessagingSystem` falls back to `"litebus"`. A valid W3C parent from the `litebus-trace-context` header becomes the parent, or a link when an ambient parent exists. `RecordException(activity, exception)` sets the `error.type` tag and `Error` status. The span name is `operation` or `operation destination`. A route is also recorded in the Kafka key and RabbitMQ routing-key tags.

`TransportBrokerIdentity` is a `public sealed class TransportBrokerIdentity(string broker)` that rejects a blank value. It exposes `Broker`, the dimension on circuit-breaker metrics. Registered values are `"amqp"`, `"kafka"`, `"sqs"`, `"azure_service_bus"`, and `"inmemory"`, one per adapter module.

`TransportObservableMetrics` (constructor `(IServiceProvider serviceProvider)`) registers the two gauges. The open gauge reports `int` 1/0 ("Whether any publisher circuit is open where 1 is open and 0 is closed."), and the failure gauge reports the `long` sum of consecutive failures. Both carry the `litebus.transport.broker` tag when an identity is registered, and yield nothing when no registry exists. `Dispose` unregisters the meter.

`AddLiteBusAmqpMetrics(this MeterProviderBuilder builder)` registers the shared `LiteBusTransportTelemetry.MeterName` (`"LiteBus.Transport"`). There is no AMQP-specific meter, and AMQP breaker metrics are distinguished only by the `litebus.transport.broker` = `"amqp"` dimension.

### 4.24 Testing support

| Package | Type | Purpose |
| --- | --- | --- |
| `LiteBus.Testing` | `LiteBusTestBase` | Abstract `IAsyncDisposable` base for tests sharing infrastructure. |
| `LiteBus.Testing` | `ManualTimeProvider` | `TimeProvider` with `Advance(TimeSpan)` and `SetUtcNow(DateTimeOffset)`. All members are thread-safe. |
| `LiteBus.Testing.Mediation` | `TestCommandMediator` | Records `Commands` and `Evaluated`, returns `default` results and succeeded `MediationResult`s, permits every evaluation, and offers `Clear()`. All members are thread-safe and recordings return snapshots. |
| `LiteBus.Testing.Mediation` | `TestQueryMediator` | Records `Queries` and `Evaluated`. `NextResult` is single-use (each `QueryAsync`, `StreamAsync` and `TryQueryAsync` call consumes it and resets it to null). `QueryAsync` records the query and returns `(TQueryResult)(stub ?? default(TQueryResult)!)`, so a missing stub yields the type default and a wrongly typed stub throws `InvalidCastException`. `StreamAsync` records the stream query then casts and yields the consumed stub exactly once, throwing `InvalidCastException` on a mismatch and yielding nothing when no stub was set. It permits every evaluation. `Clear()` clears `Queries` and `Evaluated` and resets `NextResult` to null. All members are thread-safe and recordings return snapshots. |
| `LiteBus.Testing.Mediation` | `TestEventMediator` | Records `Events` and offers `Clear()`. All members are thread-safe and `Events` returns a snapshot. |
| `LiteBus.Testing.Mediation` | `MediationHarness` / `MediationHarness<TMessage>` | Runs the shipped pipeline over hand-supplied handler instances, with no host and no container. Members are `For<TMessage>()`, `With(handler)`, `With(params object[])`, `WithTags(params string[])`, `RunAsync(message)`, `RunAsync<TResult>(message)`, and `EvaluateAsync(message)` (returns `Task<MediationDecision>` via `DecisionEvaluationMediationStrategy`; guards and validators only, not a `MediationHarnessResult`). Handler instances are keyed by concrete type in a dictionary, so a second instance of the same type replaces the first. The registry discovers every contract the concrete type implements exactly as in a host. `RunAsync`/`RunAsync<TResult>` always run the single-handler strategies (`SingleAsyncHandlerMediationStrategy`, never the event broadcast). |
| `LiteBus.Testing.Mediation` | `MediationHarnessResult` | `Outcome` (`MediationOutcome`), `StagesRun` (`IReadOnlyList<PreStage>`), and `MainHandlerRan` (`bool`) are `required`. `Reason`/`Code` default to null, `Failures` (`IReadOnlyList<ValidationFailure>`) defaults to `[]`, and `Value` is `object?` defaulting to null. It also offers `IsSuccess` / `IsDenied` / `IsInvalid`. |
| `LiteBus.Testing.Mediation` | `InMemoryIdempotencyStore` | `IIdempotencyStore` double with `AppliedKeys` (`IReadOnlyCollection<string>`, a snapshot via `.ToList()`; ordinal comparison, lock-guarded) and `ClaimTimeout` (default five minutes, `TimeProvider`-driven, must be positive; constructor `(TimeProvider? timeProvider = null, TimeSpan? claimTimeout = null)`). `TryClaimAsync` returns `Granted(token)` for a new key or a takeover past `ClaimTimeout`, `AlreadyCompleted(payload)` for an applied key with a matching or absent fingerprint, `InFlight` for a held key, and `FingerprintMismatch` when both fingerprints are set and differ. `CompleteAsync(key, claimToken, payload)` applies the key only while the stored claim still carries the token, and inserts nothing otherwise. `ReleaseAsync(key, claimToken)` only drops the unsettled claim when the token matches, and never un-applies. All three reject a blank key via `ArgumentException.ThrowIfNullOrWhiteSpace`. It ships from the testing package on purpose. It forgets on restart and is per-process, so it cannot make a claim about the system. |
| `LiteBus.Testing.Transport` | `TestMessageTransport` | `ITransportPublisher` double with `Published`, `NextPublishException` (single-shot, consumed atomically so exactly one concurrent `PublishAsync` observes it), `IsDisconnected`. All members are thread-safe. `PublishAsync` throws `InvalidOperationException` ("Test transport is disconnected.") when `IsDisconnected` is true, checked before the single-shot exception. `Clear()` drains recorded publications and resets `NextPublishException` to null and `IsDisconnected` to false. |
| `LiteBus.Testing.Hosting` | `LiteBusHostedServiceExtensions` | Offers `StartLiteBusHostedServicesAsync`, `StopLiteBusHostedServicesAsync` (reverse order), `GetInboxProcessorHostedService`, and `AssertBackgroundServices(provider, params Type[])`. `AssertBackgroundServices` compares the manifest list against the expected types order-insensitively by full name, returns the resolved `LiteBusHostManifest` on match, and throws `InvalidOperationException` on mismatch. |
| `LiteBus.Testing.DurableMessaging` | `InboxOutboxStoreServiceCollectionExtensions` | `AddInboxStoreRoles<TStore>(store)` / `AddOutboxStoreRoles<TStore>(store)` register one instance under all ten role interfaces. `AddTestTransactionalOutbox(store)` / `AddTestTransactionalOutbox()` bind one shared `TestOutboxStore` to every outbox role and register the production `StoreBoundTransactionalOutbox` as scoped `ITransactionalOutbox` (immediate-commit semantics). `AddTestTransactionalInbox(store)` / `AddTestTransactionalInbox()` do the same for one shared `TestInboxStore` with the production `StoreBoundTransactionalInbox` as scoped `ITransactionalInbox`. |
| `LiteBus.Testing.DurableMessaging` | `ProcessorDrainResult` | Sealed record aggregating a drain. `Passes` includes the final idle pass. `LeasedCount`, `SucceededCount`, `FailedCount`, `DeadLetteredCount`, and `ElapsedTime` are all `get; init;`. |
| `LiteBus.Testing.DurableMessaging` | `ProcessorPassHelpers.DrainInboxAsync` / `DrainOutboxAsync` | `(IServiceProvider provider, int maxPasses = 100, CancellationToken = default)` loop passes until one pass leases nothing and return a `ProcessorDrainResult`. They throw `InvalidOperationException` when `maxPasses` is exhausted with work still leased. Rows hidden behind retry backoff need a manual clock advance plus another drain. |
| `LiteBus.Testing.DurableMessaging` | `ScriptedOutboxDispatcher` | `IOutboxDispatcher` decorator `(IOutboxDispatcher? inner = null)` with per-contract failure scripts. Members are `FailNext(contractName, error, times = 1)`, `FailAlways(contractName, error)`, `GetAttemptCount(contractName)`, `ClearContract(contractName)`, and `Clear()`. `AttemptedEnvelopes` and `DispatchedEnvelopes` hold the recordings. A cancelled token throws without consuming the script, recording, or delegating. |
| `LiteBus.Testing.DurableMessaging` | `StoreSchemaContract` / `StoreSchemaExpectation` | `StoreSchemaExpectation` pins `Component`, `ExpectedVersion`, `RecordedVersion` (all required) with optional `SchemaName`/`TableName` pins (null skips the name check). `StoreSchemaContract.GetDifferences(actual, expected)` returns one entry per difference. `AssertCompatible` throws `InvalidOperationException` listing every difference. `AssertCompatibleAsync` overloads read the schema from an `IOutboxDiagnosticsStore` or `IInboxDiagnosticsStore`. |
| `LiteBus.Testing.DurableMessaging` | `InboxOutboxTestHost` | `public sealed class InboxOutboxTestHost : IAsyncDisposable` (namespace `LiteBus.Testing`). `Create(Action<ILiteBusBuilder>? = null, Action<IServiceCollection>? = null, TimeProvider? = null)` builds in-memory inbox+outbox storage (the clock is applied to messaging and both stores). `Services` is the root provider. `GetInbox()`/`GetOutbox()` are the writers. |
| `LiteBus.Testing.DurableMessaging` | `ProcessorPassHelpers` | `RunInboxPassAsync(IServiceProvider provider, CancellationToken = default)` resolves `IInboxProcessor` and runs one processing pass. `RunOutboxPassAsync` resolves `IOutboxProcessor` and runs one publishing pass. Both return `Task<ProcessorPassResult>`. |
| `LiteBus.Testing.DurableMessaging` | `TestOutboxStore` | `public sealed class TestOutboxStore : IOutboxStore, IOutboxProcessingStore, IOutboxOperationsStore, ITransactionalOutboxStore` (namespace `LiteBus.Testing`) wraps a backing `InMemoryOutboxStore`, constructs with `(InMemoryOutboxStoreOptions? options = null, TimeProvider? timeProvider = null)`, and adds `Get(Guid)` and `Clear()`. |
| `LiteBus.Testing.DurableMessaging` | `TestInboxStore` | `public sealed class TestInboxStore : IInboxStore, IInboxProcessingStore, IInboxOperationsStore, ITransactionalInboxStore` (namespace `LiteBus.Testing`) wraps a backing `InMemoryInboxStore`, constructs with `(InMemoryInboxStoreOptions? options = null, TimeProvider? timeProvider = null)`, and adds `Get(Guid)` and `Clear()`. The transactional writer marker lets tests resolve an in-memory `ITransactionalInbox` with immediate-commit semantics. |
| `LiteBus.Testing.DurableMessaging` | `ChaosLeaseExpiryFixture` | `CreateLeaseStore()` returns an `IInboxLeaseStore` whose `RenewLeaseAsync` fails for one target message id, simulating mid-dispatch lease loss. |

```csharp
var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
var store = new InMemoryInboxStore(new InMemoryInboxStoreOptions(), clock);

var services = new ServiceCollection();
services.AddInboxStoreRoles(store);
services.AddSingleton<TimeProvider>(clock);

clock.Advance(TimeSpan.FromMinutes(2));   // expire a lease deterministically
```

`AmbientExecutionContext.ResetForTesting()` clears leaked ambient state between tests.

To test one message's pipeline, `MediationHarness` runs the real strategies through the real stage runner over instances the test builds, so a guard can be asserted without a host:

```csharp
var result = await MediationHarness.For<CloseOrganizationCommand>()
    .With(new AuthorizationGuard<CloseOrganizationCommand>(metadata, authorizer))
    .RunAsync(command);

result.Outcome.Should().Be(MediationOutcome.Denied);
result.StagesRun.Should().Equal(PreStage.Guard);
result.MainHandlerRan.Should().BeFalse();
```

`StagesRun` is the part a consumer cannot build, because only the stage runner knows which stages ran. When the point of the library is that behavior moved into named stages, that is the assertion a test of the behavior wants. A stage with no registered handler is skipped by the pipeline and absent from the list, so the sequence reads as what happened rather than what could have.

A refusal is reported in the result rather than raised, because a test asserting a denial should not have to catch. A genuine fault still propagates, so a broken handler fails the test as a fault. Each harness gets one `MessageRegistry`, so nothing leaks between cases.

What it deliberately leaves out is composition. It registers handler instances directly, so it proves nothing about a registration a host would reject, or about a module graph, or about container lifetimes. Assert those against a host. `MessageRegistryFactory.Create()` and `MessageMediatorFactory.Create(registry, dispatchScopeFactory)` are the public seams it is built on, both `EditorBrowsable(Never)`, for a manual host that composes LiteBus without a container.

### 4.25 Roslyn analyzers

Ship `LiteBus.Analyzers` as an analyzer package reference. Rules with the `CompilationEnd` tag gather facts as each file is analyzed and report once after the whole compilation is analyzed, so they do not appear as live editor squiggles. CompilationEnd candidates are scoped to the analyzed assembly. Only types declared there are reported, and only registrations invoked in the analyzed compilation satisfy the rules, while handlers from referenced assemblies still satisfy handler coverage. `LB1002` and `LB1006` are reserved identifiers and are never reported.

| Id | Title | Category | Severity | Enabled | Message shape |
| --- | --- | --- | --- | --- | --- |
| `LB1001` | Duplicate command handler | `LiteBus.Handlers` | Error | Yes (CompilationEnd) | Command type has more than one command handler. |
| `LB1003` | Query handler impurity | `LiteBus.Handlers` | Warning | Yes | Query handler depends on a command, event, query, inbox, outbox, or transport publisher API. |
| `LB1004` | Command with result scheduled to inbox | `LiteBus.Inbox` | Error | Yes | `ICommand<TResult>` cannot be stored through `IInbox` or `ITransactionalInbox` `AcceptAsync` / `AcceptBatchAsync`, including untyped `InboxAcceptItem` initializers. Untyped items resolve through the `MessageType = typeof(X)` assignment, then the `Message` assignment. Arguments match by name when named. A batch that arrives as a method result, parameter, or field is statically opaque and stays silent. |
| `LB1005` | Unsupported open generic handler | `LiteBus.Handlers` | Error | Yes | Open generic handler must expose one type parameter, or two bound by a handler contract taking both in order. |
| `LB1007` | Missing message contract registration | `LiteBus.Contracts` | Warning | Yes (CompilationEnd) | Handled message type has no durable contract registration. Only `Register` on a contract-writer surface counts, including its extension methods. `Register` on any other type never satisfies the rule. |
| `LB1008` | Missing command handler | `LiteBus.Handlers` | Error | Yes (CompilationEnd) | Command type has no command handler. |
| `LB1009` | Missing query handler | `LiteBus.Handlers` | Error | Yes (CompilationEnd) | Query type has no query handler. |
| `LB1010` | Duplicate query handler | `LiteBus.Handlers` | Error | Yes (CompilationEnd) | Query type has more than one query handler. |
| `LB1011` | Orphan handler tag | `LiteBus.Handlers` | Warning | Yes (CompilationEnd) | A handler tag that no mediation filter references. |
| `LB1012` | Duplicate handler across assemblies | `LiteBus.Handlers` | Warning | Yes (CompilationEnd) | Same handler name in two assemblies, so `RegisterFromAssembly` may register both. |
| `LB1013` | Transactional outbox without DbContext | `LiteBus.Outbox` | Warning | Yes | Injects `ITransactionalOutboxStore` without an Entity Framework Core `DbContext` in the same constructor. |
| `LB1014` | Processor enabled without dispatcher | `LiteBus.Configuration` | Error | Yes | A configuration calls `Enable{Inbox,Outbox}Processor` with no dispatcher registration in the same configuration scope. |
| `LB1015` | Transactional storage without interceptor | `LiteBus.Configuration` | Warning | Yes | A configuration calls `EnforceTransactionalSetup()` without `EnableSaveChangesInterceptor()` on the same axis (inbox or outbox). |
| `LB1016` | Transactional inbox without DbContext | `LiteBus.Inbox` | Warning | Yes | Injects `ITransactionalInboxStore` without an Entity Framework Core `DbContext`. |
| `LB1017` | Explicit message contract registration recommended | `LiteBus.Contracts` | Warning | Yes (CompilationEnd) | `[MessageContract]` present but no explicit registration. Only `Register` on a contract-writer surface counts. Partial types report once, at the first location. |
| `LB1018` | Message states no audit position | `LiteBus.Auditing` | Warning | **No** (opt in with `dotnet_diagnostic.LB1018.severity = warning`; CompilationEnd) | The message has none of `[Audited]`, `[AuditExempt]`, an `IAuditDefinition`, or an `IMessageDefinition<TMessage>.Describe` body calling `Audited` or `NotAudited`. |
| `LB1019` | Untyped shortcut on a message that produces a result | `LiteBus.Handlers` | Warning | Yes | An untyped shortcut contract is used for a result-producing message. Answering would throw `PipelineContractException`. |
| `LB1020` | Message states no position on a required declaration | `LiteBus.Declarations` | Warning | **No** (opt in with `dotnet_diagnostic.LB1020.severity = warning`; CompilationEnd) | The message declares none of the value types named in `litebus_required_declarations` through any of the four shapes, and records no exemption, generalizing `LB1018`. The `Describe` body reading recognizes `Declare<TValue>`/`Exempt<TValue>` on the type argument, `Audited`/`NotAudited` as declaring `AuditDeclaration`, and `Authorized`/`AuthorizationExempt` as declaring `AuthorizationDeclaration`. |
| `LB1021` | Required declaration type not found | `LiteBus.Declarations` | Warning | Yes (CompilationEnd) | A name in `litebus_required_declarations` does not resolve. It is reported rather than skipped, because skipping would silently disable the requirement. |
| `LB1022` | Handler priority is inside the range reserved by LiteBus | `LiteBus.Handlers` | Warning | Yes | `[HandlerPriority]` falls inside the range LiteBus reserves for its own handlers (`ReservedFloor` to `ReservedCeiling`), so the handler orders against LiteBus stages rather than against sibling application handlers. |
| `LB1023` | Nested mediation in command handler | `LiteBus.Handlers` | Warning | **No** (opt in with `dotnet_diagnostic.LB1023.severity = warning`) | A command handler sends or publishes through a mediator inside its handle method, nesting a second unit of work. Do follow-up work through the domain event collector or the transactional outbox instead. `IQueryMediator` reads and `EvaluateAsync` guard checks are never reported, and `IMessageMediator.Mediate` of a query counts as a read. |
| `LB1024` | External side effect in command handler | `LiteBus.Handlers` | Warning | **No** (opt in with `dotnet_diagnostic.LB1024.severity = warning`) | A command handler injects an external side-effect type through its own constructor. The default forbidden types are `HttpClient` and `IHttpClientFactory` (subclasses and interface matches count). Set `litebus.LB1024.forbidden_types` in `.editorconfig` to a comma-separated list of metadata names to replace them. |

`LB1003` analyzes classes implementing `IQueryHandler<,>` or `IStreamQueryHandler<,>`. It inspects constructor parameters, fields, and properties on all hierarchy levels (ordinary method parameters are not dependencies and are not scanned), reporting each impure dependency once. `LB1011` collects handler occurrences by symbol, so a partial handler with two declarations reports once, at its first location.

`LB1022` inspects handler types only and skips other attributed types. `LB1024` inspects public constructors only. `LB1018` and `LB1020` share `DeclarableKinds = [Command, Query, StreamQuery]`, so only commands and (stream) queries are reported and events are never flagged for a missing audit position or required declaration.

The analyzer package stays on Roslyn 4.x (`Microsoft.CodeAnalysis.CSharp` 4.8.0) so it loads on consumer SDKs.

### 4.26 Exception catalog

| Exception | Namespace | Thrown when |
| --- | --- | --- |
| `NoHandlerFoundException` | `LiteBus.Messaging.Abstractions` | No main handler for a message (or an unresolvable descriptor with `RegisterPlainMessagesOnSpot` off, or an event with `ThrowIfNoHandlerFound`). Carries `MessageType`. It is never routed to error handlers. |
| `MultipleHandlerFoundException` | same | Single-handler mediation found more than one. Carries `MessageType` and `HandlerTypes`. |
| `AmbiguousMessageResolveException` | same | Two equally specific assignable descriptors. Carries `MessageType`, `ResolveStrategyType`, `Candidates`. |
| `MessageDescriptorNotFoundException` | same | Descriptor still missing after on-the-spot registration. Carries `MessageType`, `ResolveStrategyType`, `RegisterPlainMessagesOnSpot`, `RegisteredMessageCount`. |
| `UnsupportedOpenGenericHandlerException` | same | Open generic handler whose arity is neither one, nor two bound by a typed handler contract. Carries `HandlerType`, `GenericParameterCount`. |
| `LiteBusMessageDeniedException` | same | A guard denied, a shortcut denied, or a classifier recognized a fault, and no refusal mapper applied. Carries `MessageType?`, `Reason?`, `Code?`, `DeniedBy` (the guard or shortcut type, named in the message). |
| `LiteBusMessageInvalidException` | same | Validators reported failures and no refusal mapper applied. Carries `MessageType?`, `Failures`, `RejectedBy` (the validator type, named in the message). |
| `NoExecutionContextException` | same | Ambient execution context accessed outside a mediation. |
| `HandleContextDataNotFoundException` | same | `IExecutionContext.Data.Get<T>()` asked for a slot no stage stored. Carries `DataType` and `Key` (null for an unkeyed read). Use `TryGet<T>` where the value is optional. |
| `AuditReasonMissingException` | same | An audited action declares `ReasonRequired` and the handler supplied none. Carries `Action`, `MessageType`. Deliberately not a `LiteBusConfigurationException`. It is a data problem in one mediation. |
| `MessageMetadataNotFoundException` | same | `IMessageMetadataAccessor` asked about a type the registry does not hold. Carries `MessageType`. |
| `MessageContractNotRegisteredException` | same | Contract lookup failed by type or by `(name, version)`. Carries `Type? MessageType`, `string? ContractName`, and `int? ContractVersion`. |
| `MessageContractAlreadyRegisteredException` | same | Conflicting contract registration. |
| `MessageContractMismatchException` | same | Explicit registration disagrees with `[MessageContract]`. Carries `MessageType`. |
| `MessageSerializationException` | same | Payload could not be serialized or deserialized. Carries `MessageType?`, `ContractName?`, `ContractVersion?`, `Operation`. |
| `NotResolvedException` | `LiteBus.Messaging.Exceptions` | Legacy service-resolution failure. |
| `LiteBusConfigurationException` | `LiteBus.Runtime.Abstractions.Exceptions` | The composition-error category, still catchable as one. New code throws a derived type. The base remains for a failure that fits no category. |
| `ModuleCompositionException` | same | The module graph itself is invalid. A module registered twice, a dependency cycle, a missing required module, or a descriptor the container adapter cannot translate. |
| `MessageDeclarationException` | same | Message metadata is contradictory. Two definitions declare the same value, a definition declares nothing, an attribute disagrees with its annotation value, or a required declaration went unsatisfied. |
| `PipelineContractException` | same | A handler registration cannot be dispatched. A pipeline marker has no contract, the open generic shape is unsupported, two refusal mappers share the same level, an untyped shortcut is used for a result-producing message, or a scanned open generic appears while `RequireExplicitOpenGenerics` is set. |
| `DurableStorageConfigurationException` | same | Durable messaging is enabled without the storage, dispatch or processor registration it needs, or against a schema it cannot use. |
| `AuditConfigurationException` | same | Auditing cannot write. No `IAuditTrail` is registered, `AddAuditing` selected no axis, or an audit convention check failed. |
| `IdempotencyConfigurationException` | `LiteBus.Messaging.Abstractions` | Idempotency declared but the key cannot be resolved. The projected key is blank or the context-supplied key is missing. It propagates out of `SendAsync` instead of reaching error handlers. |
| `LiteBusDependencyResolutionException` | `LiteBus.Runtime.Abstractions.Exceptions` | Required service missing from the container. Carries `ServiceType`. |
| `LiteBusNotSupportedException` | same | A module registers a type that is not a construct of the axis being configured. |
| `LiteBusTimeoutException` | same | A LiteBus operation exceeded its configured time limit. |
| `DiagnosticCheckNameMismatchException` | `LiteBus.Runtime.Abstractions.Diagnostics` | `IDiagnosticCheck.Name` differs from the manifest descriptor. |
| `IdempotencyConflictException` | `LiteBus.Messaging.Abstractions.DurableMessaging` | Thrown under `IdempotencyConflictMode.Strict` for a duplicate key or id. |
| `InboxStorageException` / `OutboxStorageException` | `LiteBus.Inbox.Abstractions.Exceptions` / `LiteBus.Outbox.Storage.InMemory.Exceptions` | The store rejected a write (including in-memory capacity). |
| `InboxDispatchException` | `LiteBus.Inbox.Abstractions.Exceptions` | Inbox dispatch or ingress could not accept or route a message. |
| `InboxIngressException` | same | Ingress could not map, authorize or accept a broker delivery. |
| `AmqpInboxIngressConfigurationException` | `LiteBus.Inbox.Ingress.Amqp.Exceptions` | AMQP inbox ingress misconfigured or required services missing. |
| `InboxManagementException` / `OutboxManagementException` | `LiteBus.Inbox.Abstractions` / `LiteBus.Outbox.Abstractions` | Operator safety rule violated. Either an unconfirmed unrestricted purge, or an outbox retention purge with no positive `Retention` policy. |
| `LiteBusDispatchException` | `LiteBus.Outbox.Abstractions.Exceptions` | Outbox dispatch could not publish or replay a leased envelope. |
| `InboxPostgreSqlStorageConfigurationException` | `LiteBus.Inbox.Storage.PostgreSql.Exceptions` | No PostgreSQL data source configured. |
| `OutboxPostgreSqlStorageConfigurationException` | `LiteBus.Outbox.Storage.PostgreSql.Exceptions` | No PostgreSQL data source configured, or both the ambient transaction provider and the Marten queued writer registered. |
| `PostgreSqlStorageConfigurationException` | `LiteBus.Storage.PostgreSql.Exceptions` | PostgreSQL storage is misconfigured, such as with a missing embedded SQL resource. |
| `PostgreSqlStorageTimeoutException` | same | A PostgreSQL storage wait exceeded its budget (advisory lock acquisition, schema operation). |
| `PostgreSqlSchemaDriftException` | `LiteBus.Storage.PostgreSql` | A store table does not match the expected schema version. Carries `Component`, `SchemaName`, `TableName`, `ExpectedVersion`, `ActualVersion?`, `Details`. |
| `EfCoreStorageNotSupportedException` | `LiteBus.Storage.EntityFrameworkCore.Exceptions` | Helper does not support the current EF Core provider. |
| `SagaConcurrencyException` | `LiteBus.Saga.Abstractions` | Optimistic saga save detected a concurrent update. Carries `Correlation`. |
| `TransportHeaderMappingException` | `LiteBus.Transport.Abstractions` | Required LiteBus transport header missing or invalid. Carries `HeaderName?`. |
| `TransportCircuitBreakerOpenException` | `LiteBus.Transport` | A broker operation is rejected while the circuit is open. |
| `AmqpTransportConfigurationException` | `LiteBus.Transport.Amqp.Exceptions` | AMQP transport misconfigured or in an invalid lifecycle state. |
| `AmqpMessageReturnedException` | same | A mandatory AMQP publication was returned as unroutable. Carries `Exchange`, `RoutingKey`, `ReplyCode`, `ReplyText`. It releases its circuit-breaker permit instead of counting as a broker failure. |
| `AwsSqsTransportConfigurationException` | `LiteBus.Transport.AwsSqs.Exceptions` | SQS transport misconfigured or client state invalid. |
| `InvalidOperationException` | - | Re-enumerating a mediated stream, storing an `ICommand<TResult>` in the inbox, an envelope transition from the wrong status, starting an already-started consumer on any transport, a batch append result count mismatch, or a `TCommand` registered as recurring that implements neither `ICommand` nor `ICommand<TCommandResult>` (thrown on its first pass). |

---

## 5. Enum & Constants Catalog

### 5.1 Public enums

#### `MediationOutcome` (`LiteBus.Messaging.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Succeeded` | `0` | The main handler and all post-handlers ran without raising an exception. |
| `Answered` | `1` | A shortcut answered the message, so the main handler never ran. |
| `Denied` | `2` | A guard or a shortcut refused the message. The main handler never ran. |
| `Failed` | `3` | The pipeline raised an exception other than cancellation or a refusal. |
| `Canceled` | `4` | The mediation cancellation token fired. |
| `Invalid` | `5` | A validator reported the message malformed. The main handler never ran. |

#### `PreStage` (`LiteBus.Messaging.Abstractions`) - declaration order **is** execution order

| Member | Value | Meaning |
| --- | --- | --- |
| `Guard` | `0` | Decides whether the message may proceed. Stops at the first denial. |
| `Validator` | `1` | Decides whether the message is well-formed. Every validator runs, and failures are collected. |
| `Shortcut` | `2` | Decides whether the answer is already known. Stops at the first answer or denial. |
| `PreHandler` | `3` | Prepares the message. It cannot stop the pipeline by returning. |

#### `MessageErrorOutcome` (`LiteBus.Messaging.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Unhandled` | `0` | Default. The pipeline rethrows the original exception after error handlers run. |
| `Handled` | `1` | The pipeline suppresses the exception and may return `HandledResult`. |

#### `HandlerFaultMode` (`LiteBus.Messaging.Abstractions`)

It is a policy about fault isolation rather than about concurrency, so it applies to sequential execution as well.

| Member | Value | Meaning |
| --- | --- | --- |
| `PropagateFirst` | `0` | One failure stops the group. Already-started siblings settle first. Handlers not yet started do not run. |
| `ContinueAndAggregate` | `1` | Every handler in the group runs regardless of its siblings. A single failure is rethrown as-is, and several failures are wrapped in `AggregateException`. |

The mode governs handlers inside one priority group and never carries across groups.

#### `AuditOutcome` (`LiteBus.Messaging.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Succeeded` | `0` | The action completed. |
| `Denied` | `1` | The action was refused before it took effect. |
| `Failed` | `2` | The action was permitted but did not complete. |
| `Canceled` | `3` | The action was cancelled before it completed. |
| `Invalid` | `4` | The action was rejected because its input failed validation. |

#### `IdempotencyClaimOutcome` (`LiteBus.Messaging.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Granted` | `0` | The key was not held, and the caller now holds it. The message runs. |
| `AlreadyCompleted` | `1` | The key was applied by an earlier delivery. The message is answered without running. |
| `InFlight` | `2` | The key is held by a delivery that has not settled yet. The message is denied with code `idempotency.in-flight`. |
| `FingerprintMismatch` | `3` | The key was applied carrying different content. The message is denied with code `idempotency.fingerprint-mismatch`. |

An unsettled claim older than the store claim timeout is taken over and granted. Fingerprints compare only on applied keys. When both are non-null and different, the claim mismatches, while a null on either side replays.

#### `ConcurrencyMode` (`LiteBus.Events.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Sequential` | `0` | Handlers execute one after another in deterministic order. This is the default. |
| `Parallel` | `1` | Handlers execute concurrently in non-deterministic order. |

#### `RetryBackoff` (`LiteBus.Messaging.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Fixed` | `0` | Every retry uses `InitialDelay` before jitter. |
| `Exponential` | `1` | Delay grows as `InitialDelay * 2^(attemptCount-1)` before the `MaxDelay` cap and jitter. |
| `Schedule` | `2` | Delay is read from the explicit `RetryOptions.Delays` ladder. An attempt past the end repeats the last entry, and `MaxDelay` does not clamp the entries. |

#### `IdempotencyConflictMode` (`LiteBus.Messaging.Abstractions.DurableMessaging`)

| Member | Value | Meaning |
| --- | --- | --- |
| `ReturnExisting` | `0` | Return the stored row for a duplicate key or id. |
| `Strict` | `1` | Fail the operation with `IdempotencyConflictException`. |

#### `ProcessorHookFailurePolicy` (`LiteBus.Messaging.Abstractions.Processing`)

| Member | Value | Meaning |
| --- | --- | --- |
| `DeadLetter` | `0` | Move the message to dead letter when an after-dispatch hook throws. |
| `CompleteDespiteHookFailure` | `1` | Log the hook failure and persist the successful dispatch anyway. |

#### `InboxStatus` (`LiteBus.Inbox.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Pending` | `0` | Waiting to be processed. |
| `Processing` | `1` | Leased by a processor. |
| `Completed` | `2` | Dispatched successfully. |
| `Failed` | `3` | Failed and may be retried after `VisibleAfter`. |
| `DeadLettered` | `4` | Retries exhausted, non-retryable failure, or manually set aside. |

#### `OutboxStatus` (`LiteBus.Outbox.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Pending` | `0` | Waiting to be published. |
| `Publishing` | `1` | Leased by a publisher. |
| `Published` | `2` | Published successfully. |
| `Failed` | `3` | Failed and may be retried. |
| `DeadLettered` | `4` | Retries exhausted, non-retryable failure, or manually set aside. |

#### `InboxAcceptOutcome` (`LiteBus.Inbox.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Accepted` | `0` | A new envelope was stored. |
| `AlreadyAccepted` | `1` | An existing envelope was returned for the supplied idempotency key or identifier. |

#### `OutboxEnqueueOutcome` (`LiteBus.Outbox.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Enqueued` | `0` | A new envelope was stored. |
| `AlreadyEnqueued` | `1` | An existing envelope was returned for the supplied idempotency metadata. |

#### `ProcessorState` - declared twice, once per axis: `LiteBus.Inbox.Abstractions.ProcessorState` and `LiteBus.Outbox.Abstractions.ProcessorState`

| Member | Value | Meaning |
| --- | --- | --- |
| `Running` | `0` | The processor is actively leasing and dispatching (or publishing). |
| `Paused` | `1` | The processor is suspended, and no new passes start. |
| `Draining` | `2` | The processor is finishing one final pass before stopping. |

#### `InstanceLifetime` (`LiteBus.Runtime.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Transient` | `0` | A new instance per request. |
| `Singleton` | `1` | One instance for the application lifetime. |
| `Scoped` | `2` | One instance per dependency-injection scope. |

#### `CompositeModuleBuildOrder` (`LiteBus.Runtime.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `ParentFirst` | `0` | Build the composite parent before its declared children (used by `InboxModule` and `OutboxModule`). |
| `ChildrenFirst` | `1` | Build every declared child before the parent (used by `SagaModule`). |

#### `DiagnosticStatus` (`LiteBus.Runtime.Abstractions.Diagnostics`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Healthy` | `0` | The probe succeeded and reported nothing actionable. |
| `Degraded` | `1` | The probe succeeded but reported a condition that may need attention. |
| `Unhealthy` | `2` | The probe failed or reported a condition needing intervention. |

#### `DiagnosticAggregateStatus` (`LiteBus.Runtime.Abstractions.Diagnostics`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Healthy` | `0` | All probes healthy. |
| `Degraded` | `1` | At least one degraded and none unhealthy. |
| `Unhealthy` | `2` | At least one unhealthy. |

#### `TransactionalWriteMode` (`LiteBus.Storage.PostgreSql`)

| Member | Value | Meaning |
| --- | --- | --- |
| `RequireActiveTransaction` | `0` | The participant throws when `IPostgreSqlTransactionProvider.TryGetCurrent` returns false. This is the default for ambient registration. |
| `AllowImmediateCommit` | `1` | The participant falls back to the auto-commit singleton store when no ambient transaction exists. It is for development and tests only, because it breaks atomicity between domain and messaging writes. |

#### `PostgreSqlSchemaLogLevel` (`LiteBus.Storage.PostgreSql`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Debug` | `0` | Verbose schema operation detail. |
| `Information` | `1` | Normal schema lifecycle messages. |
| `Warning` | `2` | Recoverable schema concern. |
| `Error` | `3` | Schema operation failed. |

#### `EfCoreStorageProvider` (`LiteBus.Storage.EntityFrameworkCore`)

| Member | Value | Provider name (`EfCoreRelationalProviderNames`) | Table qualification |
| --- | --- | --- | --- |
| `InMemory` | `0` | `Microsoft.EntityFrameworkCore.InMemory` | quoted schema and table |
| `PostgreSql` | `1` | `Npgsql.EntityFrameworkCore.PostgreSQL` | quoted schema and table |
| `SqlServer` | `2` | `Microsoft.EntityFrameworkCore.SqlServer` | bracketed schema and table |
| `MySql` | `3` | `Pomelo.EntityFrameworkCore.MySql` | backtick table only (no schema) |
| `Sqlite` | `4` | `Microsoft.EntityFrameworkCore.Sqlite` | quoted table only (no schema) |

Trace-context column types differ by provider. PostgreSQL uses `jsonb`, SQL Server `nvarchar(max)`, MySQL `json`, SQLite `TEXT`, and EF in-memory `TEXT`. The payload column is always `TEXT`. `ExecuteUpdate` bulk paths are skipped for the in-memory and SQLite providers.

#### `TransportConsumerInvocationOutcome` (`LiteBus.Transport.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Handled` | `0` | The handler completed without the invoker settling the delivery. |
| `Requeued` | `1` | The invoker returned the delivery to the broker after a handler failure. |

### 5.2 Internal enums (not part of the public API; documented because they govern behaviour)

| Enum | Members | Meaning |
| --- | --- | --- |
| `PipelineFamily` (`LiteBus.Messaging.Abstractions`) | `PreStage = 0`, `PostHandler = 1`, `CompletionHandler = 2`, `RefusalMapper = 3` | Groups dispatchable contracts by call shape. `RefusalMapper` is the one family that is not a pipeline stage. It runs on the refusal path in place of raising. |
| `StageAggregation` (`LiteBus.Messaging.Abstractions`) | `StopAtFirst = 0`, `CollectFailures = 1` | `CollectFailures` is used only by `PreStage.Validator`. |
| `MetadataSourceKind` (`LiteBus.Messaging.Registry`) | `Attribute = 0`, `Definition = 1` | The higher value wins for the same declaring message type, so a definition overrides an attribute. |
| `EfCoreLeaseComponent` (`LiteBus.Storage.EntityFrameworkCore.Leasing`) | `Inbox = 0`, `Outbox = 1` | Selects the table shape targeted by raw lease SQL. |

### 5.3 Constants

#### `HandlerPriorities` (`LiteBus.Messaging.Abstractions`)

| Constant | Type | Value |
| --- | --- | --- |
| `Default` | `int` | `0` |
| `ReservedFloor` | `int` | `1000000` |
| `Persistence` | `int` | `1000100` (`ReservedFloor + 100`) |
| `Observability` | `int` | `1000200` (`ReservedFloor + 200`) |
| `ReservedCeiling` | `int` | `2000000` |
| `UnitOfWork` | `int` | `2000100` (`ReservedCeiling + 100`) |

#### `MediationExceptionData` (`LiteBus.Messaging.Abstractions`)

| Constant | Type | Value |
| --- | --- | --- |
| `SuppressedCompletionFaults` | `string` | `LiteBus.SuppressedCompletionFaults` |

#### `MessageTraceContextKeys` (`LiteBus.Messaging.Abstractions`)

| Constant | Type | Value |
| --- | --- | --- |
| `CorrelationId` | `string` | `__LiteBus.Trace.CorrelationId` |
| `CausationId` | `string` | `__LiteBus.Trace.CausationId` |
| `TenantId` | `string` | `__LiteBus.Trace.TenantId` |
| `TraceContext` | `string` | `__LiteBus.Trace.TraceContext` |

#### `InboxExecutionContextKeys` (`LiteBus.Inbox.Abstractions`)

| Constant | Type | Value |
| --- | --- | --- |
| `IsInboxExecution` | `string` | `__LiteBus.Inbox.IsInboxExecution` |
| `MessageId` | `string` | `__LiteBus.Inbox.MessageId` |
| `ContractName` | `string` | `__LiteBus.Inbox.ContractName` |

#### `MessageProcessorDiagnostics` (`LiteBus.Messaging.Abstractions`)

| Constant | Type | Value |
| --- | --- | --- |
| `LeaseLostDuringProcessingError` | `string` | `Lease lost during processing; scheduling retry.` |

This is internal. Persisted error text is truncated to 1024 characters.

#### `AuditTrailDiagnosticCheck` (`LiteBus.Messaging.Audit`)

| Constant | Type | Value |
| --- | --- | --- |
| `CheckName` | `string` | `litebus.audit.trail` |

#### `IdempotencyStoreDiagnosticCheck` (`LiteBus.Messaging.Idempotency`)

| Constant | Type | Value |
| --- | --- | --- |
| `CheckName` | `string` | `litebus.idempotency.store` |

The `Name` property returns `CheckName`.

#### `IdempotencyRefusalCodes` (`LiteBus.Messaging.Abstractions`)

| Constant | Type | Value |
| --- | --- | --- |
| `InFlight` | `string` | `idempotency.in-flight` |
| `FingerprintMismatch` | `string` | `idempotency.fingerprint-mismatch` |

#### `DeclarationGuard<TMessage, TValue>.UndeclaredCode` (`LiteBus.Messaging.Abstractions`)

| Constant | Type | Value |
| --- | --- | --- |
| `UndeclaredCode` | `string` | `declaration.missing` |

#### `PostgreSqlIdempotencySchemaDiagnosticCheck` (`LiteBus.Commands.Storage.PostgreSql`)

| Constant | Type | Value |
| --- | --- | --- |
| `CheckName` | `string` | `idempotency.postgresql.schema` |

#### `PostgreSqlRecurringLeaseSchemaDiagnosticCheck` (`LiteBus.Commands.Recurring.Storage.PostgreSql`)

| Constant | Type | Value |
| --- | --- | --- |
| `CheckName` | `string` | `recurring.postgresql.schema` |

#### `TransportHeaders` (`LiteBus.Transport.Abstractions`)

| Constant | Type | Value |
| --- | --- | --- |
| `MessageId` | `string` | `litebus-message-id` |
| `ContractName` | `string` | `litebus-contract-name` |
| `ContractVersion` | `string` | `litebus-contract-version` |
| `CorrelationId` | `string` | `correlation-id` |
| `CausationId` | `string` | `causation-id` |
| `TenantId` | `string` | `tenant-id` |
| `TraceContext` | `string` | `litebus-trace-context` |
| `IdempotencyKey` | `string` | `litebus-idempotency-key` |
| `VisibleAfter` | `string` | `litebus-visible-after` |
| `VisibleAfterDelay` | `string` | `litebus-visible-after-delay` |
| `ContentEncoding` | `string` | `litebus-content-encoding` |
| `Route` | `string` | `litebus-route` |
| `ContentType` | `string` | `content-type` |

#### `LiteBusMediationTelemetry` (`LiteBus.Messaging`)

| Constant | Type | Value |
| --- | --- | --- |
| `ActivitySourceName` | `string` | `LiteBus.Mediation` |
| `MeterName` | `string` | `LiteBus.Mediation` |
| `DurationInstrumentName` | `string` | `litebus.mediation.duration` |
| `CountInstrumentName` | `string` | `litebus.mediation.count` |
| `StageDurationInstrumentName` | `string` | `litebus.mediation.stage.duration` |
| `DecisionsInstrumentName` | `string` | `litebus.mediation.decisions` |
| `MessageAttributeName` | `string` | `litebus.message` |
| `OutcomeAttributeName` | `string` | `litebus.outcome` |
| `CodeAttributeName` | `string` | `litebus.code` |
| `StageAttributeName` | `string` | `litebus.stage` |
| `DecidedByAttributeName` | `string` | `litebus.decided_by` |

#### `TransportMessagingSystems` (`LiteBus.Transport`)

| Constant | Type | Value |
| --- | --- | --- |
| `AmazonSqs` | `string` | `aws_sqs` |
| `Kafka` | `string` | `kafka` |
| `LiteBusInMemory` | `string` | `litebus_in_memory` |
| `Other` | `string` | `litebus` |
| `RabbitMq` | `string` | `rabbitmq` |
| `ServiceBus` | `string` | `servicebus` |

#### `LiteBusTransportTelemetry` (`LiteBus.Transport`)

| Constant | Type | Value |
| --- | --- | --- |
| `ActivitySourceName` | `string` | `LiteBus.Transport` |
| `PublishOperationName` | `string` | `send` |
| `ConsumeOperationName` | `string` | `process` |
| `MessagingSystemTagName` | `string` | `messaging.system` |
| `MessagingOperationNameTagName` | `string` | `messaging.operation.name` |
| `MessagingOperationTypeTagName` | `string` | `messaging.operation.type` |
| `DestinationTagName` | `string` | `messaging.destination.name` |
| `MessageIdTagName` | `string` | `messaging.message.id` |
| `ConversationIdTagName` | `string` | `messaging.message.conversation_id` |
| `KafkaMessageKeyTagName` | `string` | `messaging.kafka.message.key` |
| `RabbitMqRoutingKeyTagName` | `string` | `messaging.rabbitmq.destination.routing_key` |
| `RouteTagName` | `string` | `litebus.transport.route` |
| `RedeliveredTagName` | `string` | `litebus.transport.redelivered` |
| `MeterName` | `string` | `LiteBus.Transport` |
| `CircuitBreakerOpenInstrumentName` | `string` | `litebus.transport.circuit_breaker.open` |
| `CircuitBreakerFailureCountInstrumentName` | `string` | `litebus.transport.circuit_breaker.failure_count` |
| `CircuitBreakerFailureRecordedInstrumentName` | `string` | `litebus.transport.circuit_breaker.failure_recorded` |
| `CircuitBreakerOpenedInstrumentName` | `string` | `litebus.transport.circuit_breaker.opened` |
| `BrokerTagName` | `string` | `litebus.transport.broker` |

#### Reserved transport adapter meters

Each adapter reserves one meter for future adapter-specific instruments. No counters, histograms, or gauges are registered on these meters today. Circuit breaker metrics live on the shared `LiteBus.Transport` meter. Call `AddMeter` with one of these names only when adding custom adapter instrumentation.

| Constant | Declaring type | Value |
| --- | --- | --- |
| `MeterName` | `LiteBusTransportKafkaTelemetry` (`LiteBus.Transport.Kafka`) | `LiteBus.Transport.Kafka` |
| `MeterName` | `LiteBusTransportAwsTelemetry` (`LiteBus.Transport.AwsSqs`) | `LiteBus.Transport.AwsSqs` |
| `MeterName` | `LiteBusTransportAzureServiceBusTelemetry` (`LiteBus.Transport.AzureServiceBus`) | `LiteBus.Transport.AzureServiceBus` |
| `MeterName` | `LiteBusInMemoryTelemetry` (`LiteBus.Transport.InMemory`) | `LiteBus.Transport.InMemory` |

#### `LiteBusInboxTelemetry` (`LiteBus.Inbox`)

| Constant | Type | Value |
| --- | --- | --- |
| `ActivitySourceName` | `string` | `LiteBus.Inbox` |
| `MeterName` | `string` | `LiteBus.Inbox` |
| `QueueDepthInstrumentName` | `string` | `litebus.inbox.queue.depth` |
| `ProcessorStateInstrumentName` | `string` | `litebus.inbox.processor.state` |
| `QueueStatusAttributeName` | `string` | `litebus.inbox.status` |
| `ProcessorLeaseLostInstrumentName` | `string` | `litebus.inbox.processor.lease_lost` |
| `ProcessorPersistSkippedInstrumentName` | `string` | `litebus.inbox.processor.persist_skipped` |
| `CleanupErrorInstrumentName` | `string` | `litebus.inbox.cleanup.errors` |
| `ProcessorDispatchDurationInstrumentName` | `string` | `litebus.inbox.processor.dispatch_duration` |
| `ProcessorLeasesAcquiredInstrumentName` | `string` | `litebus.inbox.processor.leases_acquired` |
| `ProcessorPersistRejectedInstrumentName` | `string` | `litebus.inbox.processor.persist_rejected` |
| `DiagnosticsUnavailableInstrumentName` | `string` | `litebus.inbox.diagnostics.unavailable` |
| `ProcessorPersistFailedInstrumentName` | `string` | `litebus.inbox.processor.persist_failed` |

#### `LiteBusOutboxTelemetry` (`LiteBus.Outbox`)

It uses the same constant names, with `LiteBus.Outbox` for the source and meter names, `litebus.outbox.*` instrument names (`queue.depth`, `processor.state`, `processor.lease_lost`, `processor.persist_skipped`, `cleanup.errors`, `processor.dispatch_duration`, `processor.leases_acquired`, `processor.persist_rejected`, `diagnostics.unavailable`, `processor.persist_failed`), `QueueStatusAttributeName` is `litebus.outbox.status`.

#### `LiteBusInboxIngressTelemetry` (`LiteBus.Inbox.Ingress`)

| Constant | Type | Value |
| --- | --- | --- |
| `MeterName` | `string` | `LiteBus.Inbox` |
| `AckFailedAfterAcceptInstrumentName` | `string` | `ingress.ack_failed_after_accept` |

#### `EfCoreRelationalProviderNames` (`LiteBus.Storage.EntityFrameworkCore`)

| Constant | Type | Value |
| --- | --- | --- |
| `InMemory` | `string` | `Microsoft.EntityFrameworkCore.InMemory` |
| `PostgreSql` | `string` | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| `SqlServer` | `string` | `Microsoft.EntityFrameworkCore.SqlServer` |
| `MySql` | `string` | `Pomelo.EntityFrameworkCore.MySql` |
| `Sqlite` | `string` | `Microsoft.EntityFrameworkCore.Sqlite` |

#### `PostgreSqlSchemaComponents` (`LiteBus.Storage.PostgreSql`)

These are the component names tracked in the schema-version metadata table.

| Constant | Type | Value |
| --- | --- | --- |
| `Inbox` | `string` | `inbox` |
| `Outbox` | `string` | `outbox` |
| `Saga` | `string` | `saga` |

#### `PostgreSqlSchemaSqlPaths` (`LiteBus.Storage.PostgreSql`)

These are the repository-relative paths to the canonical SQL files shipped in `LiteBus.Storage.PostgreSql`, rooted at `src/LiteBus.Storage.PostgreSql/Sql/`. Copy these files into Flyway, Liquibase, or DBA-owned migration folders.

| Constant | Type | Value |
| --- | --- | --- |
| `MetadataCreate` | `string` | `src/LiteBus.Storage.PostgreSql/Sql/metadata/create.sql` |
| `MetadataSelectVersion` | `string` | `src/LiteBus.Storage.PostgreSql/Sql/metadata/select_version.sql` |
| `MetadataUpsertVersion` | `string` | `src/LiteBus.Storage.PostgreSql/Sql/metadata/upsert_version.sql` |
| `InspectorTableExists` | `string` | `src/LiteBus.Storage.PostgreSql/Sql/inspector/table_exists.sql` |
| `InspectorListColumns` | `string` | `src/LiteBus.Storage.PostgreSql/Sql/inspector/list_columns.sql` |
| `InspectorIndexExists` | `string` | `src/LiteBus.Storage.PostgreSql/Sql/inspector/index_exists.sql` |

#### `PostgreSqlInboxNotifyChannel` (`LiteBus.Inbox.Storage.PostgreSql`)

| Constant | Type | Value |
| --- | --- | --- |
| `ChannelName` | `string` | `litebus_inbox_work` |

This is the `LISTEN` / `NOTIFY` channel that inbox insert triggers use to wake processors.

#### `PostgreSqlOutboxNotifyChannel` (`LiteBus.Outbox.Storage.PostgreSql`)

| Constant | Type | Value |
| --- | --- | --- |
| `ChannelName` | `string` | `litebus_outbox_work` |

This is the `LISTEN` / `NOTIFY` channel that outbox insert triggers use to wake processors.

#### `PostgreSqlInboxSchemaSqlPaths` (`LiteBus.Inbox.Storage.PostgreSql`)

These are the repository-relative paths to the canonical SQL files shipped in `LiteBus.Inbox.Storage.PostgreSql`, rooted at `src/LiteBus.Inbox.Storage.PostgreSql/Sql/`.

| Constant | Type | Value |
| --- | --- | --- |
| `V1Create` | `string` | `src/LiteBus.Inbox.Storage.PostgreSql/Sql/inbox/v1/create.sql` |
| `V1EnsureIndexes` | `string` | `src/LiteBus.Inbox.Storage.PostgreSql/Sql/inbox/v1/ensure_indexes.sql` |

#### `PostgreSqlOutboxSchemaSqlPaths` (`LiteBus.Outbox.Storage.PostgreSql`)

These are the repository-relative paths to the canonical SQL files shipped in `LiteBus.Outbox.Storage.PostgreSql`, rooted at `src/LiteBus.Outbox.Storage.PostgreSql/Sql/`.

| Constant | Type | Value |
| --- | --- | --- |
| `V1Create` | `string` | `src/LiteBus.Outbox.Storage.PostgreSql/Sql/outbox/v1/create.sql` |
| `V1EnsureIndexes` | `string` | `src/LiteBus.Outbox.Storage.PostgreSql/Sql/outbox/v1/ensure_indexes.sql` |
| `V2Create` | `string` | `src/LiteBus.Outbox.Storage.PostgreSql/Sql/outbox/v2/create.sql` |
| `V2Upgrade` | `string` | `src/LiteBus.Outbox.Storage.PostgreSql/Sql/outbox/v2/upgrade.sql` |

#### `PostgreSqlSagaSchemaSqlPaths` (`LiteBus.Saga.Storage.PostgreSql`)

These are the repository-relative paths to the canonical SQL files shipped in `LiteBus.Saga.Storage.PostgreSql`, rooted at `src/LiteBus.Saga.Storage.PostgreSql/Sql/`.

| Constant | Type | Value |
| --- | --- | --- |
| `V1Create` | `string` | `src/LiteBus.Saga.Storage.PostgreSql/Sql/saga/v1/create.sql` |
| `V1EnsureIndexes` | `string` | `src/LiteBus.Saga.Storage.PostgreSql/Sql/saga/v1/ensure_indexes.sql` |

#### `PostgreSqlIdempotencySchemaSqlPaths` (`LiteBus.Commands.Storage.PostgreSql`)

These are the repository-relative paths to the canonical SQL files shipped in `LiteBus.Commands.Storage.PostgreSql`, rooted at `src/LiteBus.Commands.Storage.PostgreSql/Sql/`.

| Constant | Type | Value |
| --- | --- | --- |
| `V1Create` | `string` | `src/LiteBus.Commands.Storage.PostgreSql/Sql/idempotency/v1/create.sql` |
| `V1EnsureIndexes` | `string` | `src/LiteBus.Commands.Storage.PostgreSql/Sql/idempotency/v1/ensure_indexes.sql` |

#### `PostgreSqlRecurringLeaseSchemaSqlPaths` (`LiteBus.Commands.Recurring.Storage.PostgreSql`)

These are the repository-relative paths to the canonical SQL files shipped in `LiteBus.Commands.Recurring.Storage.PostgreSql`, rooted at `src/LiteBus.Commands.Recurring.Storage.PostgreSql/Sql/`.

| Constant | Type | Value |
| --- | --- | --- |
| `V1Create` | `string` | `src/LiteBus.Commands.Recurring.Storage.PostgreSql/Sql/recurring/v1/create.sql` |
| `V1EnsureIndexes` | `string` | `src/LiteBus.Commands.Recurring.Storage.PostgreSql/Sql/recurring/v1/ensure_indexes.sql` |

#### Default-value constants

| Constant | Declaring type | Value |
| --- | --- | --- |
| `DefaultMaxInFlightMessages` | `TransportConsumerOptions` | `32` |
| `DefaultMaxMessageBytes` | `TransportInboxIngressSafetyOptions` | `4194304` (4 MiB) |
| `DefaultMaxInFlightMessages` | `TransportInboxIngressSafetyOptions` | `32` (aliases the transport constant) |
| `DefaultBatchSize` | `TransportInboxIngressSafetyOptions` | `10` |
| `DefaultMaxMessageBytes` | `TransportInboxIngressOptions` | `4194304` (aliases the safety constant) |
| `DefaultDestinationCapacity` | `InMemoryTransportOptions` | `1024` |

#### Analyzer diagnostic ids (`LiteBus.Analyzers.DiagnosticIds`, internal)

`LB1001` DuplicateCommandHandler, `LB1003` QueryHandlerImpurity, `LB1004` CommandWithResultScheduledToInbox, `LB1005` UnsupportedOpenGenericHandler, `LB1007` MissingMessageContractRegistration, `LB1008` MissingCommandHandler, `LB1009` MissingQueryHandler, `LB1010` DuplicateQueryHandler, `LB1011` OrphanHandlerTag, `LB1012` DuplicateHandlerAcrossAssemblies, `LB1013` TransactionalOutboxWithoutDbContext, `LB1014` ProcessorEnabledWithoutDispatcher, `LB1015` TransactionalStorageWithoutInterceptor, `LB1016` TransactionalInboxWithoutDbContext, `LB1017` ExplicitMessageContractRegistration, `LB1018` MissingAuditDeclaration, `LB1019` UntypedShortcutOnResultMessage, `LB1020` MissingDeclaration, `LB1021` UnresolvedRequiredDeclaration, `LB1022` ReservedHandlerPriority, `LB1023` NestedMediationInCommandHandler, `LB1024` ExternalSideEffectInCommandHandler. (`LB1002` and `LB1006` are reserved and are never shipped.)

#### Log event ids

| EventId | Name | Level | Source |
| --- | --- | --- | --- |
| `1001` | `LoopFailed` | Error | inbox processor: the loop failed before the next pass |
| `1002` | `DispatchFailed` | Warning | inbox processor: dispatch failed for one message on one attempt |
| `1003` | `LeaseLossPersistSkipped` | Warning | inbox processor: lease-loss persistence skipped because the active lease was lost |
| `1004` | `AfterDispatchCompleted` | Warning | inbox processor: the AfterDispatch hook failed and dispatch completes despite the hook failure |
| `1005` | `AfterDispatchDeadLettered` | Warning | inbox processor: the AfterDispatch hook failed and the message moves to dead letter |
| `1006` | `TerminalPersistSkipped` | Warning | inbox processor: terminal persistence skipped because the active lease was lost |
| `1007` | `PassCompleted` | Information | inbox processor: one pass finished with leased, succeeded, failed and dead-lettered counts |
| `1008` | `LeaseLossPersistFailed` | Error | inbox processor: lease-loss persistence failed and the pass continues with remaining messages |
| `1009` | `PermanentFailureClassifierFailed` | Warning | inbox processor: the `IsPermanentFailure` predicate threw, so the failure follows the retry schedule |
| `1101` | `CleanupFailed` | Error | inbox retention cleanup |
| `2001` | `LoopFailed` | Error | outbox processor: the loop failed before the next pass |
| `2002` | `DispatchFailed` | Warning | outbox processor: dispatch failed for one message on one attempt |
| `2003` | `LeaseLossPersistSkipped` | Warning | outbox processor: lease-loss persistence skipped because the active lease was lost |
| `2004` | `AfterDispatchCompleted` | Warning | outbox processor: the AfterDispatch hook failed and dispatch completes despite the hook failure |
| `2005` | `AfterDispatchDeadLettered` | Warning | outbox processor: the AfterDispatch hook failed and the message moves to dead letter |
| `2006` | `TerminalPersistSkipped` | Warning | outbox processor: terminal persistence skipped because the active lease was lost |
| `2007` | `PassCompleted` | Information | outbox processor: one pass finished with leased, published, failed and dead-lettered counts |
| `2008` | `LeaseLossPersistFailed` | Error | outbox processor: lease-loss persistence failed and the pass continues with remaining messages |
| `2009` | `PermanentFailureClassifierFailed` | Warning | outbox processor: the `IsPermanentFailure` predicate threw, so the failure follows the retry schedule |
| `2101` | `CleanupFailed` | Error | outbox retention cleanup |
| `2101` | `LoopStarted` | Information | recurring command loop: the loop started for one command type with its interval |
| `2102` | `LoopDisabled` | Information | recurring command loop: the loop is disabled and will not run |
| `2103` | `PassFailed` | Error | recurring command loop: one pass failed and the loop continues after the error backoff |
| `2104` | `PassConflicted` | Information | recurring command loop: one pass lost a concurrency race and the loop continues at its normal interval |
| `2105` | `LeaseSkipped` | Debug | recurring command loop: one pass was skipped because another instance holds the lease, and the loop continues at its normal interval |
| `2106` | `LeaseLost` | Warning | recurring command loop: the lease was lost mid-pass, so the busy period ends and the loop backs off |
| `2110` | `LeaseRenewalStolen` | Warning | PostgreSQL recurring lease: renewal found the lease taken by another holder, logged once. Renewal stopped and the row left alone |
| `2111` | `LeaseRenewalFailed` | Warning | PostgreSQL recurring lease: renewal threw. Renewal stopped and the lease expires on the database clock |
| `3001` | `LeasedBatch` | Debug | shared processor |
| `3002` | `LeaseRenewalFailed` | Warning | shared processor |
| `3002` | `IngressRestarting` | Warning | transport inbox ingress |
| `3003` | `TerminalPersistenceFailed` | Error | shared processor |
| `3003` | `BatchFlushFailed` | Error | transport inbox ingress |
| `3004` | `AckFailedAfterAccept` | Error | transport inbox ingress |
| `4001` | `InvalidManagementRequest` | Warning | ASP.NET Core management endpoints |
| `4001` | `ProcessorSessionRestarting` | Warning | Azure Service Bus consumer: the processor session failed and will restart with backoff |
| `4002` | `ManagementSafetyRejection` | Warning | ASP.NET Core management endpoints |
| `5001` | `ManagementOperationFailed` | Error | ASP.NET Core management endpoints |

Event ids are unique within a component, not across components. `2101`, `3002`, `3003`, and `4001` each name two different events from two different sources.

#### Other well-known string values

| Value | Where |
| --- | --- |
| `"inbox"` / `"outbox"` | `PayloadProtectionContext.Axis` |
| `"application/json"` | default `ContentType` on `TransportPublishRequest`, `TransportInboxDispatcherOptions`, `TransportOutboxDispatcherOptions` |
| `"litebus"` | default `LiteBusManagementOptions.RoutePrefix`, default health-check name |
| `"litebus.probes"` | synthetic probe name emitted when the manifest has no probes and `FailHealthWhenNoProbes` is true |
| `"litebus_inbox_messages"`, `"litebus_outbox_messages"`, `"litebus_saga_instances"`, `"litebus_schema_versions"`, `"litebus_idempotency_keys"`, `"litebus_recurring_leases"` | default table names |
| `"public"` | default schema and metadata-schema name |
| `"litebus-transport"` | default `KafkaTransportOptions.ConsumerGroupId` |
| `"localhost"`, `"/"`, `"guest"` | AMQP default host, virtual host, user and password |
| `U+001F` | separator inside `DurableIdempotencyScope.CreateScopeKey` |
| `IX_LiteBus_Inbox_CreatedAt`, `IX_LiteBus_Outbox_CreatedAt` | EF Core `created_at` index names |
| `"litebus-headers"` | SQS message attribute holding non-reserved headers packed as JSON when attributes would exceed ten; expanded on receive |
| `"urn:litebus:idempotency:missing-key"`, `"urn:litebus:idempotency:key-too-long"`, `"urn:litebus:idempotency:in-flight"`, `"urn:litebus:idempotency:fingerprint-mismatch"` | problem types returned by the ASP.NET Core idempotency-key filter and `MediationResult.ToHttpResult` |
| `"Idempotency-Key"` | default `IdempotencyKeyFilterOptions.HeaderName` |

---

## 6. Common Recipes & Code Snippets

### 6.1 Standard initialization and usage (in-process mediation only)

```csharp
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLiteBus(liteBus =>
{
    var applicationAssembly = typeof(PlaceOrderCommand).Assembly;

    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(applicationAssembly));
    liteBus.AddQueries(queries => queries.RegisterFromAssembly(applicationAssembly));
    liteBus.AddEvents(events => events.RegisterFromAssembly(applicationAssembly));
});

var app = builder.Build();

app.MapPost("/orders", async (
    PlaceOrderRequest request,
    ICommandMediator commands,
    CancellationToken cancellationToken) =>
{
    var orderId = await commands.SendAsync(new PlaceOrderCommand(request.CartId), cancellationToken);
    return Results.Created($"/orders/{orderId}", new { orderId });
});

app.MapGet("/orders/{orderId:guid}", async (
    Guid orderId,
    IQueryMediator queries,
    CancellationToken cancellationToken) =>
{
    var view = await queries.QueryAsync(new GetOrderQuery(orderId), cancellationToken);
    return view is null ? Results.NotFound() : Results.Ok(view);
});

app.Run();
```

```csharp
// Message and handler
public sealed record PlaceOrderCommand(Guid CartId) : ICommand<Guid>;

public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>
{
    private readonly IOrderRepository _orders;
    private readonly IEventMediator _events;

    public PlaceOrderCommandHandler(IOrderRepository orders, IEventMediator events)
    {
        _orders = orders;
        _events = events;
    }

    public async Task<Guid> HandleAsync(PlaceOrderCommand message, CancellationToken cancellationToken = default)
    {
        var orderId = await _orders.PlaceAsync(message.CartId, cancellationToken);
        await _events.PublishAsync(new OrderPlaced(orderId), cancellationToken);
        return orderId;
    }
}
```

### 6.2 Durable command intake with a PostgreSQL inbox and an outbox

```csharp
using LiteBus.Commands;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Queries;

builder.Services.AddLiteBus(liteBus =>
{
    var assembly = typeof(ProcessPaymentCommand).Assembly;
    var connectionString = builder.Configuration.GetConnectionString("Messaging")!;

    liteBus.AddMessaging(messaging => messaging.UseTimeProvider(TimeProvider.System));
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(assembly));
    liteBus.AddQueries(queries => queries.RegisterFromAssembly(assembly));
    liteBus.AddEvents(events => events.RegisterFromAssembly(assembly));

    liteBus.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process");

        inbox.UsePostgreSqlStorage(pg => pg
            .UseConnectionString(connectionString)
            .UseOptions(new PostgreSqlInboxStoreOptions
            {
                SchemaName = "messaging",
                UseListenNotify = true,
                EnsureSchemaCreationOnStartup = true,
                ValidateSchemaCreationOnStartup = true
            }));

        inbox.UseInProcessDispatch();

        inbox.UseProcessorOptions(new InboxProcessorOptions
        {
            BatchSize = 50,
            DispatcherConcurrency = 8,
            LeaseDuration = TimeSpan.FromMinutes(2),
            LeaseHeartbeatInterval = TimeSpan.FromSeconds(30),
            HonorShutdownTokenOnPersist = true,
            Retry = new RetryOptions
            {
                MaxAttempts = 8,
                InitialDelay = TimeSpan.FromSeconds(2),
                MaxDelay = TimeSpan.FromMinutes(10),
                Backoff = RetryBackoff.Exponential,
                UseJitter = true
            }
        });

        inbox.EnableInboxProcessor(host =>
        {
            host.PollInterval = TimeSpan.FromMilliseconds(500);
            host.StartupDelay = TimeSpan.FromSeconds(2);
            host.UseAdaptivePolling = true;
        });

        inbox.EnableCleanup(cleanup =>
        {
            cleanup.Retention = TimeSpan.FromDays(14);
            cleanup.Interval = TimeSpan.FromHours(6);
        });
    });

    liteBus.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<PaymentProcessed>("payments.processed");
        outbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(500));
        outbox.EnableCleanup(cleanup => cleanup.Retention = TimeSpan.FromDays(3));
    });
});
```

```csharp
// HTTP intake: accept durably and return 202 with the durable id
app.MapPost("/payments", async (
    AcceptPaymentRequest request,
    IInbox inbox,
    CancellationToken cancellationToken) =>
{
    var item = InboxAcceptItem<ProcessPaymentCommand>.WithIdempotency(
        new ProcessPaymentCommand(request.PaymentId, request.Amount),
        $"payment:{request.PaymentId}");

    var receipt = await inbox.AcceptAsync(item, cancellationToken);

    return Results.Accepted($"/payments/{request.PaymentId}", new
    {
        messageId = receipt.Id,
        outcome = receipt.Outcome            // Accepted or AlreadyAccepted
    });
});
```

```csharp
// Handler: work plus a durable event in one place
public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
{
    private readonly PaymentLedger _ledger;
    private readonly IOutbox _outbox;

    public ProcessPaymentCommandHandler(PaymentLedger ledger, IOutbox outbox)
    {
        _ledger = ledger;
        _outbox = outbox;
    }

    public async Task HandleAsync(ProcessPaymentCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(message.Amount);

        _ledger.MarkProcessed(message.PaymentId, message.Amount);

        await _outbox.EnqueueAsync(new PaymentProcessed(message.PaymentId, message.Amount), cancellationToken);
    }
}
```

The inbox accepts only commands declared as a **void** `ICommand`. `InboxEnvelopeFactory` throws `InvalidOperationException` for an `ICommand<TResult>`, and `LB1004` reports the same misuse at compile time.

### 6.3 Broker fan-out: transactional EF Core outbox publishing to AMQP, with AMQP ingress into the inbox

```csharp
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Outbox.Dispatch;
using LiteBus.Outbox.Dispatch.Amqp;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Transport.Amqp;

builder.Services.AddDbContextFactory<AppDbContext>((provider, options) =>
{
    options.UseNpgsql(connectionString);
    options.AddLiteBusOutboxInterceptor(provider.GetRequiredService<LiteBusOutboxSaveChangesInterceptor>());
    options.AddLiteBusInboxInterceptor(provider.GetRequiredService<LiteBusInboxSaveChangesInterceptor>());
});

builder.Services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(assembly));
    liteBus.AddEvents(events => events.RegisterFromAssembly(assembly));

    // One shared transport at the composition root; dispatch and ingress both require it.
    liteBus.AddAmqpTransport(new AmqpConnectionOptions
    {
        HostName = "rabbit",
        VirtualHost = "/payments",
        UserName = "app",
        Password = secret,
        ClientProvidedName = "payments-api",
        CircuitBreaker = new AmqpCircuitBreakerOptions
        {
            FailureThreshold = 3,
            BreakDuration = TimeSpan.FromSeconds(15)
        }
    });

    liteBus.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<PaymentProcessed>("payments.processed");

        outbox.UseEntityFrameworkCoreStorage(ef => ef
            .UseDbContext<AppDbContext>()
            .EnableSaveChangesInterceptor()
            .EnforceTransactionalSetup()
            .UseOptions(new EntityFrameworkCoreOutboxStoreOptions { SchemaName = "messaging" }));

        outbox.UseAmqpDispatch(dispatch =>
        {
            dispatch.DefaultDestination = "payments";                  // AMQP exchange
            dispatch.ContentType = "application/json";
            dispatch.Persistent = true;
            dispatch.Mandatory = true;
            dispatch.ValidatePayloadBeforeDispatch = true;
            dispatch.ResolveRoute = envelope => $"payments.{envelope.ContractName}";
        });

        outbox.EnableOutboxProcessor();
        outbox.EnableCleanup(cleanup => cleanup.Retention = TimeSpan.FromDays(7));
    });

    liteBus.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process");

        inbox.UseEntityFrameworkCoreStorage(ef => ef
            .UseDbContext<AppDbContext>()
            .EnableSaveChangesInterceptor());

        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor();

        inbox.UseAmqpIngress(ingress => ingress.UseOptions(new AmqpInboxIngressOptions
        {
            QueueName = "payments-inbound",
            PrefetchCount = 32,
            DeclareQueue = true,
            DurableQueue = true,
            RequeueOnFailure = true,
            Safety = new TransportInboxIngressSafetyOptions
            {
                MaxMessageBytes = 512 * 1024,
                RequireStableIdentity = true,
                TrustApplicationHeaders = false,
                MaxInFlightMessages = 16,
                EnableBatchAccept = true,
                BatchSize = 25,
                BatchMaxWait = TimeSpan.FromMilliseconds(150)
            }
        }));
    });
});
```

```csharp
// Transactional write: the event row and the domain change commit together.
public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
{
    private readonly AppDbContext _db;
    private readonly ITransactionalOutbox<AppDbContext> _outbox;

    public ProcessPaymentCommandHandler(AppDbContext db, ITransactionalOutbox<AppDbContext> outbox)
    {
        _db = db;
        _outbox = outbox;
    }

    public async Task HandleAsync(ProcessPaymentCommand message, CancellationToken cancellationToken = default)
    {
        _db.Payments.Add(new PaymentRow(message.PaymentId, message.Amount));

        await _outbox.EnqueueAsync(new PaymentProcessed(message.PaymentId, message.Amount), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);   // interceptor writes the outbox row here
    }
}
```

### 6.4 Advanced configuration: overriding the pipeline defaults

```csharp
// A guard, a validator, a shortcut and a refusal mapper for one command,
// plus a completion handler that records every outcome.

public sealed class TenantIsActiveGuard : ICommandGuard<ProcessPaymentCommand>
{
    private readonly ITenantDirectory _tenants;

    public TenantIsActiveGuard(ITenantDirectory tenants) => _tenants = tenants;

    public async Task<Verdict> DecideAsync(ProcessPaymentCommand message, CancellationToken ct = default)
        => await _tenants.IsActiveAsync(message.TenantId, ct)
            ? Verdict.Allow
            : Verdict.Deny("the tenant is suspended", "TENANT_SUSPENDED");
}

public sealed class ProcessPaymentValidator : ICommandValidator<ProcessPaymentCommand>
{
    public Task<Validity> ValidateAsync(ProcessPaymentCommand command, CancellationToken ct = default)
        => Task.FromResult(command.Amount > 0
            ? Validity.Valid
            : Validity.Invalid("amount must be greater than zero", nameof(command.Amount), "AMOUNT_NOT_POSITIVE"));
}

// Refuse without throwing: map denials and validation failures onto a result value.
public sealed class PaymentRefusalMapper : ICommandRefusalMapper<ProcessPaymentCommand, PaymentOutcome>
{
    public PaymentOutcome Map(ProcessPaymentCommand message, Refusal refusal)
        => new(Accepted: false, refusal.Outcome.ToString(), refusal.Reason, refusal.Code);
}

// A cross-cutting completion handler for every command, ordered after application handlers.
[HandlerPriority(1000)]
public sealed class MediationOutcomeRecorder : ICommandCompletionHandler
{
    private readonly IMetrics _metrics;

    public MediationOutcomeRecorder(IMetrics metrics) => _metrics = metrics;

    public Task HandleCompletionAsync(MessageCompletionContext<ICommand> context, CancellationToken ct)
    {
        _metrics.Record(
            name: context.Message.GetType().Name,
            outcome: context.Outcome.ToString(),
            durationMs: context.Duration.TotalMilliseconds,
            failureType: context.Exception?.GetType().Name);

        return Task.CompletedTask;
    }
}

// Parallel event fan-out that reports every failure, plus a tag filter.
await eventMediator.PublishAsync(new PaymentProcessed(id, amount), new EventMediationSettings
{
    ThrowIfNoHandlerFound = true,
    AutoRegisterUnregisteredMessageTypes = false,
    Routing = new EventRoutingSettings
    {
        Tags = ["realtime"],
        HandlerPredicate = descriptor => descriptor.Priority < HandlerPriorities.ReservedFloor
    },
    Execution = new EventExecutionSettings
    {
        PriorityGroupsConcurrencyMode = ConcurrencyMode.Sequential,
        HandlersWithinSamePriorityConcurrencyMode = ConcurrencyMode.Parallel,
        FaultMode = HandlerFaultMode.ContinueAndAggregate
    }
}, cancellationToken);
```

### 6.5 Error handling and extension points

```csharp
// 1. Recover from a specific fault and return a fallback value.
public sealed class ConcurrencyFallback : ICommandErrorHandler<PlaceOrderCommand, Guid>
{
    public Task HandleErrorAsync(MessageErrorContext<PlaceOrderCommand, Guid> context, CancellationToken ct = default)
    {
        if (context.Exception is DbUpdateConcurrencyException)
        {
            context.HandledResult = Guid.Empty;
            context.Outcome = MessageErrorOutcome.Handled;   // suppress the original exception
        }

        return Task.CompletedTask;
    }
}

// 2. Distinguish the refusal shapes, and read suppressed completion-handler faults off the propagated exception.
try
{
    await commandMediator.SendAsync(command, cancellationToken);
}
catch (LiteBusMessageInvalidException invalid)
{
    logger.LogWarning("rejected by {RejectedBy}", invalid.RejectedBy?.Name);

    foreach (var failure in invalid.Failures)
    {
        logger.LogWarning("invalid {Member}: {Message} ({Code})", failure.Member, failure.Message, failure.Code);
    }
}
catch (LiteBusMessageDeniedException denied)
{
    logger.LogWarning("denied {MessageType}: {Reason} ({Code}) by {DeniedBy}", denied.MessageType?.Name, denied.Reason, denied.Code, denied.DeniedBy?.Name);
}
catch (NoHandlerFoundException missing)
{
    logger.LogError("no handler registered for {MessageType}", missing.MessageType.FullName);
}
catch (Exception exception)
{
    if (exception.Data[MediationExceptionData.SuppressedCompletionFaults] is List<Exception> suppressed)
    {
        foreach (var fault in suppressed)
        {
            logger.LogError(fault, "a completion handler failed while the mediation was already failing");
        }
    }

    throw;
}

// 3. Custom audit trail: buffer inside the unit of work, or write out of band for failures.
public sealed class SqlAuditTrail : IAuditTrail
{
    private readonly AppDbContext _db;

    public SqlAuditTrail(AppDbContext db) => _db = db;

    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        _db.AuditRecords.Add(AuditRow.From(record));
        await _db.SaveChangesAsync(cancellationToken);   // token is CancellationToken.None here
    }
}

// 4. Custom durable processor hook (the seam the saga integration uses).
public sealed class TenantContextHook : IProcessorEnvelopeHook
{
    private static readonly AsyncLocal<string?> Tenant = new();

    public Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
    {
        Tenant.Value = envelope.TenantId;
        return Task.CompletedTask;
    }

    public void PrepareDispatchScope(IProcessorEnvelope envelope) => Tenant.Value = envelope.TenantId;

    public bool ShouldDispatch(IProcessorEnvelope envelope) => envelope.TenantId is not null;

    public void AbandonDispatchScope(IProcessorEnvelope envelope) => Tenant.Value = null;

    public Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
    {
        Tenant.Value = null;
        return Task.CompletedTask;
    }
}

// Register it as a collection entry so several hooks can coexist:
services.AddSingleton<IProcessorEnvelopeHook, TenantContextHook>();
```

### 6.6 Operations: pause, drain, replay and purge

```csharp
public sealed class MessagingOperations
{
    private readonly IInboxManager _inbox;
    private readonly IInboxProcessorControl _control;

    public MessagingOperations(IInboxManager inbox, IInboxProcessorControl control)
    {
        _inbox = inbox;
        _control = control;
    }

    public async Task<int> ReplayDeadLettersAsync(CancellationToken ct)
    {
        // Stop leasing, finish the current pass, then replay.
        await _control.PauseAsync(ct);

        try
        {
            return await _inbox.RequeueDeadLettersAsync(ct);
        }
        finally
        {
            await _control.ResumeAsync(ct);
        }
    }

    public async Task<RequeueResult> ReplaySelectedAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
        => await _inbox.RequeueAsync(ids, ct);

    public async Task DrainBeforeShutdownAsync(CancellationToken ct)
        => await _control.DrainAsync(TimeSpan.FromMinutes(2), ct);

    public async Task<int> PurgeOldFailuresAsync(CancellationToken ct)
        => await _inbox.PurgeAsync(new InboxMessageFilter
        {
            Statuses = [InboxStatus.DeadLettered],
            CreatedBefore = DateTimeOffset.UtcNow.AddDays(-30)
        }, confirm: false, ct);   // narrowed filter, so confirm is not needed

    public async Task<IReadOnlyList<InboxEnvelope>> BrowseAsync(CancellationToken ct)
    {
        var items = new List<InboxEnvelope>();
        string? cursor = null;

        do
        {
            var page = await _inbox.QueryAsync(
                new InboxMessageFilter { Statuses = [InboxStatus.Failed, InboxStatus.DeadLettered] },
                new InboxMessagePageRequest { PageSize = 100, Cursor = cursor },
                ct);

            items.AddRange(page.Items);
            cursor = page.HasMore ? page.NextCursor : null;
        }
        while (cursor is not null);

        return items;
    }

    public async Task<(StoreSchemaInfo Schema, RetentionRunStatus Retention)> InspectAsync(CancellationToken ct)
        => (await _inbox.GetSchemaInfoAsync(ct), await _inbox.GetRetentionStatusAsync(ct));
}
```

Once `AddLiteBusManagementEndpoints()` is mapped, the operations above are available as these HTTP calls:

```text
GET    /litebus/inbox/messages?statuses=DeadLettered&pageSize=100
GET    /litebus/inbox/messages/{messageId}
POST   /litebus/inbox/messages/requeue            { "messageIds": ["..."] }
POST   /litebus/inbox/messages/requeue-dead-letters
DELETE /litebus/inbox/messages?statuses=DeadLettered   { "confirm": true }
GET    /litebus/inbox/status-counts
GET    /litebus/inbox/schema
GET    /litebus/inbox/retention/status
POST   /litebus/inbox/retention/purge
GET    /litebus/inbox/processor/state
POST   /litebus/inbox/processor/pause
POST   /litebus/inbox/processor/resume
POST   /litebus/inbox/processor/drain
GET    /litebus/health
```

The same routes exist under `/litebus/outbox`.

### 6.7 Deterministic tests over the durable pipeline

```csharp
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Testing;

var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<PaymentLedger>();

services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(messaging => messaging.UseTimeProvider(clock));
    liteBus.AddCommands(commands => commands.RegisterFromAssembly(typeof(ProcessPaymentCommand).Assembly));

    liteBus.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process");
        inbox.UseInMemoryStorage(store => store.UseTimeProvider(clock));
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor(host =>
        {
            host.PollInterval = TimeSpan.Zero;      // no polling delay in tests
            host.UseAdaptivePolling = true;
        });
        inbox.UseProcessorOptions(new InboxProcessorOptions
        {
            BatchSize = 10,
            LeaseHeartbeatInterval = TimeSpan.Zero, // disable heartbeats for determinism
            Retry = new RetryOptions { MaxAttempts = 2, UseJitter = false, Backoff = RetryBackoff.Fixed }
        });
    });
});

await using var provider = services.BuildServiceProvider();

// Accept one command, then run exactly one processor pass by hand.
var inbox = provider.GetRequiredService<IInbox>();
var receipt = await inbox.AcceptAsync(new ProcessPaymentCommand(paymentId, 42m), CancellationToken.None);

var processor = provider.GetRequiredService<IInboxProcessor>();
var pass = await processor.ProcessPendingAsync(CancellationToken.None);

Assert.Equal(1, pass.LeasedCount);
Assert.Equal(1, pass.SucceededCount);

// Or drive the hosted loop, then stop it.
await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, CancellationToken.None);
clock.Advance(TimeSpan.FromSeconds(5));
await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);

AmbientExecutionContext.ResetForTesting();
```

The following code substitutes the mediators in a unit test:

```csharp
var commands = new TestCommandMediator();
var queries = new TestQueryMediator { NextResult = new OrderView(orderId) };
var events = new TestEventMediator();

var sut = new CheckoutService(commands, queries, events);
await sut.CheckoutAsync(cartId, CancellationToken.None);

Assert.Single(commands.Commands);
Assert.IsType<PlaceOrderCommand>(commands.Commands[0]);
Assert.Single(events.Events);
```

The following code simulates lease loss mid-dispatch:

```csharp
var store = new InMemoryInboxStore(new InMemoryInboxStoreOptions(), clock);
var chaos = new ChaosLeaseExpiryFixture(store, targetMessageId);
IInboxLeaseStore leaseStore = chaos.CreateLeaseStore();   // RenewLeaseAsync returns false for that id
// The processor cancels the dispatch and persists the envelope as Failed with
// MessageProcessorDiagnostics.LeaseLostDuringProcessingError.
```

### 6.8 Full observability wiring

```csharp
using LiteBus.Extensions.Diagnostics.HealthChecks;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Inbox.Extensions.OpenTelemetry;
using LiteBus.Outbox.Extensions.OpenTelemetry;
using LiteBus.Transport.Amqp.Extensions.OpenTelemetry;
using LiteBus.Transport.Extensions.OpenTelemetry;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddLiteBusInboxInstrumentation()      // source LiteBus.Inbox
        .AddLiteBusOutboxInstrumentation()     // source LiteBus.Outbox
        .AddLiteBusTransportInstrumentation()  // source LiteBus.Transport
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddLiteBusInboxMetrics()              // meter LiteBus.Inbox
        .AddLiteBusOutboxMetrics()             // meter LiteBus.Outbox
        .AddLiteBusTransportMetrics()          // meter LiteBus.Transport
        .AddLiteBusAmqpMetrics()               // AMQP-specific meter registration
        .AddOtlpExporter());

builder.Services.AddHealthChecks()
    .AddLiteBus(options =>
    {
        options.FailHealthWhenNoProbes = true;
        options.DiagnosticChecks = new DiagnosticCheckRunOptions
        {
            MaxParallelism = 8,
            Timeout = TimeSpan.FromSeconds(3)
        };
    });

builder.Services.AddLiteBusManagement(options =>
{
    options.RoutePrefix = "internal/litebus";
    options.AuthorizationPolicy = "operators";
    options.MaxPageSize = 200;
    options.MaxBulkMessageIds = 500;
    options.DefaultDrainTimeout = TimeSpan.FromSeconds(45);
    options.MaxDrainTimeout = TimeSpan.FromMinutes(3);
});

var app = builder.Build();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("ready") });
app.AddLiteBusManagementEndpoints();
app.Run();
```

The following code registers a custom diagnostic probe against an axis:

```csharp
public sealed class BacklogDepthCheck : IDiagnosticCheck
{
    private readonly IInboxDiagnosticsStore _store;

    public BacklogDepthCheck(IInboxDiagnosticsStore store) => _store = store;

    public string Name => "payments.inbox.backlog";   // must match the registered name exactly

    public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var counts = await _store.GetStatusCountsAsync(cancellationToken);
        var pending = counts.TryGetValue(InboxStatus.Pending, out var value) ? value : 0;

        return pending switch
        {
            > 10_000 => new DiagnosticResult(DiagnosticStatus.Unhealthy, "inbox backlog is critical",
                new Dictionary<string, object> { ["pending"] = pending }),
            > 1_000 => new DiagnosticResult(DiagnosticStatus.Degraded, "inbox backlog is growing",
                new Dictionary<string, object> { ["pending"] = pending }),
            _ => new DiagnosticResult(DiagnosticStatus.Healthy, "inbox backlog is normal",
                new Dictionary<string, object> { ["pending"] = pending })
        };
    }
}

liteBus.AddInbox(inbox =>
{
    inbox.UseInMemoryStorage();
    inbox.UseInProcessDispatch();
    inbox.EnableInboxProcessor();
    inbox.AddDiagnosticCheck<BacklogDepthCheck>("payments.inbox.backlog");
});
```

When the probe runs, a `Name` that differs from the registered descriptor name throws `DiagnosticCheckNameMismatchException`.

### 6.9 Marten transactional outbox

```csharp
using LiteBus.Outbox.Storage.Marten;
using LiteBus.Outbox.Storage.PostgreSql;
using Marten;

services.AddMarten(options => options.Connection(connectionString));

services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(_ => { });
    liteBus.AddEvents(events => events.RegisterFromAssembly(assembly));

    liteBus.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.submitted");
        outbox.UsePostgreSqlStorage(pg =>
        {
            pg.UseConnectionString(connectionString);
            pg.UseMartenTransactionalOutbox();
        });
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor();
    });
});
```

```csharp
// Handler: the document and the outbox row commit in one SaveChangesAsync.
public sealed class SubmitOrderCommandHandler : ICommandHandler<SubmitOrderCommand>
{
    private readonly IDocumentSession _session;
    private readonly ITransactionalOutbox _outbox;

    public SubmitOrderCommandHandler(IDocumentSession session, ITransactionalOutbox outbox)
    {
        _session = session;
        _outbox = outbox;
    }

    public async Task HandleAsync(SubmitOrderCommand message, CancellationToken cancellationToken = default)
    {
        _session.Store(new Order(message.OrderId));

        await _outbox.EnqueueAsync(new OrderSubmitted(message.OrderId), cancellationToken);
        await _session.SaveChangesAsync(cancellationToken);
    }
}
```

The application registers Marten itself against the same database as the outbox. You cannot combine `UseMartenTransactionalOutbox()` with `EnableAmbientTransactionProvider()` because both register `ITransactionalOutbox`. If you dispose the session without saving, it stores neither the document nor the outbox row.
