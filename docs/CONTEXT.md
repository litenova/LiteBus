# LiteBus - Complete AI Context & Reference Guide

> LiteBus is a .NET 10 library that provides explicit, composable in-process mediation (commands, queries, events) plus durable messaging (inbox, outbox, saga) with pluggable relational/in-memory storage and broker transports. Its target domain is CQRS-style application architecture where handler discovery, pipeline stages, message contracts, auditing, and at-least-once delivery must all be declared rather than inferred. The architecture is a module graph: each feature is an `IModule` that writes container-neutral `DependencyDescriptor` entries into an `IDependencyRegistry`, so the same composition works on Microsoft DI and Autofac.

**Assembly version:** `7.0.0` (`VersionPrefix` in `src/Directory.Build.props`). **Target framework:** `net10.0`. **Nullable:** enabled. **License:** MIT. All assemblies are strong-named with a shared public key.

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
| **Module / Composition Root** | `IModule`, `ICompositeModule`, `IModuleRegistry`, `IModuleConfiguration`. Every feature (`MessageModule`, `CommandModule`, `QueryModule`, `EventModule`, `InboxModule`, `OutboxModule`, `SagaModule`, transport modules, storage/dispatch/ingress sub-modules) is a module. Modules are topologically sorted by `IRequires<TModule>` edges before `Build` is called. |
| **Builder** | `MessageModuleBuilder`, `CommandModuleBuilder`, `QueryModuleBuilder`, `EventModuleBuilder`, `InboxModuleBuilder`, `OutboxModuleBuilder`, `SagaModuleBuilder`, plus per-adapter builders (`PostgreSqlInboxModuleBuilder`, `EfCoreOutboxStorageModuleBuilder`, `AmqpInboxIngressModuleBuilder`, ...). Builders are pure configuration objects; they never touch the container. |
| **Container abstraction / Adapter** | `IDependencyRegistry` + `DependencyDescriptor` + `InstanceLifetime`. `MicrosoftDependencyRegistryAdapter` and `AutofacDependencyRegistryAdapter` translate descriptors. `IMessageDispatchScopeFactory` / `IMessageDispatchScope` abstract per-message scopes. |
| **Mediator** | `IMessageMediator` is the single core mediator. `ICommandMediator`, `IQueryMediator`, `IEventMediator` are thin semantic facades over it. |
| **Strategy** | `IMessageResolveStrategy` (descriptor lookup) and `IMessageMediationStrategy<TMessage, TMessageResult>` (pipeline shape). Implementations: `ActualTypeOrFirstAssignableTypeMessageResolveStrategy`, `SingleAsyncHandlerMediationStrategy<TMessage>`, `SingleAsyncHandlerMediationStrategy<TMessage, TMessageResult>`, `SingleStreamHandlerMediationStrategy<TMessage, TMessageResult>`, `AsyncBroadcastMediationStrategy<TMessage>`. |
| **Pipeline / staged middleware** | Fixed stage order enforced by `PreStage` enum ordering: Guard -> Validator -> Shortcut -> PreHandler -> main handler -> post-handlers -> (error handlers on fault) -> completion handlers. |
| **Registry** | `IMessageRegistry` (handler descriptors + message metadata), `IMessageContractRegistry` (durable contract name/version <-> CLR type), `ISagaStateTypeRegistry`, `ITransportCircuitBreakerRegistry`. |
| **Factory** | `IInboxEnvelopeFactory`, `IOutboxEnvelopeFactory`, `InboxProcessorFactory`, `OutboxProcessorFactory`, `PipelineDispatch.For(Type)`. |
| **Lazy resolution** | `LazyHandler<THandler, TDescriptor>` + `ILazyHandlerCollection<,>`: handler instances are only resolved from the container when the pipeline actually reaches them. |
| **Ambient context (AsyncLocal)** | `AmbientExecutionContext` with `AmbientExecutionContext.ExecutionContextScope`, and `AmbientAuditScope` layered on top of `IExecutionContext.Items`. |
| **Envelope / Unit of work** | `InboxEnvelope`, `OutboxEnvelope` are immutable records with transition methods (`AsLeased`, `AsCompleted`/`AsPublished`, `AsFailed`, `AsDeadLettered`, `AsRequeued`). |
| **Lease / fencing token** | `LeaseOwner` + monotonic `LeaseGeneration` + `LeaseExpiresAt` on envelopes; `ILeaseRenewable.RenewLeaseAsync` heartbeats. |
| **Discriminated union via sealed record hierarchies** | `MessageIdentity`, `Idempotency`, `MessageVisibility`, `MessageTrace`, `TenantScope`, `PublicationTarget`, `AuditDeclaration`, `AzureServiceBusDiagnosticTarget`. |
| **Result objects instead of exceptions** | `Verdict`, `Validity`, `Shortcut`, `Shortcut<T>`, `Refusal`, `PipelineDecision`, `MediationOutcome`, `PersistResult`, `ProcessorPassResult`. |
| **Roslyn analyzers** | 20 diagnostic rules (`LB1001`-`LB1021`) that enforce composition invariants at compile time. |

### 1.2 Key interfaces and their primary implementations

#### Mediation core (`LiteBus.Messaging.Abstractions` -> `LiteBus.Messaging`)

| Interface | Primary implementation | Notes |
| --- | --- | --- |
| `IMessageMediator` | `MessageMediator` (internal) | Creates `ExecutionContext`, an ambient scope and a dispatch scope, resolves the descriptor, builds `MessageDependencies`, delegates to the mediation strategy, then retains the scope until the returned `Task`/`IAsyncEnumerable` completes via `MediationScopeRetention`. |
| `IMessageRegistry` (= `IMessageWriter` + `IMessageReader`) | `MessageRegistry` (internal) | Thread-safe (lock-protected). Discovers handler descriptors from CLR types, links handlers to messages, binds `IMessageDefinition` metadata, closes open generic handlers. |
| `IMessageContractRegistry` (= `IContractWriter` + `IContractReader`) | `MessageContractRegistry` (internal) | Lock-protected bidirectional map: CLR type <-> (`Name`, `Version`). Also honours `[MessageContract]` on-demand. |
| `IMessageSerializer` | `SystemTextJsonMessageSerializer` | `JsonSerializerDefaults.Web` by default; wraps `JsonException`/`NotSupportedException` in `MessageSerializationException`. |
| `IExecutionContext` | `ExecutionContext` (internal) | Carries `CancellationToken`, `Items`, `Data`, `Tags`, `MessageResult`, `PostHandlersSuppressed`. |
| `IMessageDependencies` | `MessageDependencies` (internal) | Filters descriptors by handler predicate and tag intersection, orders by `Priority` then `RegistrationSequence`, wraps each in a `LazyHandler`. Precomputes a bitmask of occupied pre-stages. |
| `IAuditScope` | `AmbientAuditScope` (internal, singleton instance) | Stores `AuditScopeState` under execution-context item key `__LiteBus.Audit.Scope`. |
| `IAuditRecordWriter` | `AuditRecordWriter` (internal, scoped) | Builds `AuditRecord` from the message's `AuditedDeclaration` + `IAuditScope` + `IAuditOutcomeMapper` + `TimeProvider`. |
| `IAuditOutcomeMapper` | `DefaultAuditOutcomeMapper` | Maps `MediationOutcome` to `AuditOutcome` with no application knowledge. |
| `IAuditTrail` | none shipped (application supplied) | Registered through `MessageModuleBuilder.UseAuditTrail`. |
| `IMessageContractResolver` | `DeclaredTypeMessageContractResolver` (optional) | Default (unregistered) behaviour uses the runtime instance type. |
| `IHandleContextData` | `HandleContextData` | Type-keyed store reached through `IExecutionContext.Data`; one instance per mediation, lock-guarded. |

#### Semantic facades

| Interface | Implementation | Strategy used |
| --- | --- | --- |
| `ICommandMediator` | `CommandMediator` | `SingleAsyncHandlerMediationStrategy<ICommand>` / `<ICommand<TResult>, TResult>` + `ActualTypeOrFirstAssignableTypeMessageResolveStrategy`. |
| `IQueryMediator` | `QueryMediator` | `SingleAsyncHandlerMediationStrategy<IQuery<TResult>, TResult>` for `QueryAsync`; `SingleStreamHandlerMediationStrategy<IStreamQuery<TResult>, TResult>` for `StreamAsync`. |
| `IEventMediator` | `EventMediator` | `AsyncBroadcastMediationStrategy<IEvent>` / `<TEvent>`. |

#### Runtime / composition (`LiteBus.Runtime.Abstractions` -> `LiteBus.Runtime`)

| Interface | Implementation |
| --- | --- |
| `ILiteBusBuilder` | `LiteBusBuilder` |
| `IModuleRegistry` | `ModuleRegistry` (internal; DFS topological sort, cycle detection, composite child expansion) |
| `IModuleConfiguration` | `ModuleConfiguration` (internal) |
| `IDependencyRegistry` | `DependencyRegistry`, `MicrosoftDependencyRegistryAdapter`, `AutofacDependencyRegistryAdapter` (all share `DependencyRegistrationTracker`) |
| `IMessageDispatchScopeFactory` | `MicrosoftMessageDispatchScopeFactory`, `AutofacMessageDispatchScopeFactory`, `RootMessageDispatchScopeFactory` |
| `IStartupTask` | `PostgreSqlInboxSchemaInitializer`, `PostgreSqlOutboxSchemaInitializer`, `InboxObservableMetricsInitializer`, `OutboxObservableMetricsInitializer`, `TransportObservableMetricsInitializer` |
| `IBackgroundService` | `InboxProcessorBackgroundService`, `OutboxProcessorBackgroundService`, `InboxCleanupBackgroundService`, `OutboxCleanupBackgroundService`, `TransportInboxIngressConsumer`; driven by `LiteBusHostOrchestrator` (a `BackgroundService`) |
| `IDiagnosticCheck` | `AuditTrailDiagnosticCheck`, `PostgreSqlInboxSchemaDiagnosticCheck`, `PostgreSqlOutboxSchemaDiagnosticCheck`, `AmqpConnectivityDiagnosticCheck`, `KafkaConnectivityDiagnosticCheck`, `AwsSqsConnectivityDiagnosticCheck`, `AzureServiceBusConnectivityDiagnosticCheck` |

#### Durable messaging

| Interface | Implementation(s) |
| --- | --- |
| `IInbox` | `Inbox` |
| `ITransactionalInbox` | `StoreBoundTransactionalInbox` (PostgreSQL ambient transaction) |
| `ITransactionalInbox<TContext>` | `TransactionalInbox<TContext>` (EF Core) |
| `IInboxStore` / `IInboxLeaseStore` / `IInboxStateWriter` / `IInboxDeadLetterStore` / `IInboxRetentionStore` / `IInboxDiagnosticsStore` / `IInboxMessageQuery` / `IInboxPurgeStore` / `IInboxProcessingStore` / `IInboxOperationsStore` | `InMemoryInboxStore`, `PostgreSqlInboxStore`, `EfCoreInboxStore` |
| `IInboxDispatcher` | `CommandInboxDispatcher` (in-process), `TransportInboxDispatcher` (broker) |
| `IInboxProcessor` | `PipelinedInboxProcessor` |
| `IInboxProcessorControl` | `InboxProcessorControl` |
| `IInboxWorkSignal` | `InboxPollingWorkSignal`, `PostgreSqlInboxWorkSignal` (LISTEN/NOTIFY) |
| `IInboxManager` | `InboxManager` (internal) |
| `IOutbox` | `Outbox` |
| `ITransactionalOutbox` / `ITransactionalOutbox<TContext>` | `StoreBoundTransactionalOutbox`, `TransactionalOutbox<TContext>` |
| `IOutboxStore` + role interfaces | `InMemoryOutboxStore`, `PostgreSqlOutboxStore`, `EfCoreOutboxStore` |
| `IOutboxDispatcher` | `EventOutboxDispatcher` (in-process), `TransportOutboxDispatcher` (broker) |
| `IOutboxProcessor` | `PipelinedOutboxProcessor` |
| `IProcessorEnvelopeHook` | `SagaProcessorHook` |
| `ISagaStore` | `InMemorySagaStore`, `PostgreSqlSagaStore` |
| `ISagaContext` | `SagaExecutionContext` |

#### Transport

| Interface | Implementation(s) |
| --- | --- |
| `ITransportPublisher` | `InMemoryPublisher`, `AmqpPublisher`, `KafkaPublisher`, `AwsSqsPublisher`, `AzureServiceBusPublisher`, plus the `TestMessageTransport` double |
| `IMessageConsumer` | `InMemoryConsumer`, `AmqpConsumer`, `KafkaConsumer`, `AwsSqsConsumer`, `AzureServiceBusConsumer` |
| `ITenantRoutingStrategy` | none shipped (application supplied, optional) |
| `ITransportCircuitBreaker` / `ITransportCircuitBreakerRegistry` | `TransportCircuitBreaker` / `TransportCircuitBreakerRegistry` |
| `IPayloadEncryptor` / `IContextualPayloadEncryptor` | none shipped (application supplied); `IInboxPayloadProtector` / `IOutboxPayloadProtector` wrap it per axis |

### 1.3 The mediation pipeline in exact order

```text
IMessageMediator.Mediate
  |- new ExecutionContext(tags, items, cancellationToken)
  |- AmbientExecutionContext.CreateScope(executionContext)
  |- IMessageDispatchScopeFactory.CreateScope()          (per-message DI scope)
  |- IMessageResolveStrategy.Find(runtimeType, reader)
  |     (if null and RegisterPlainMessagesOnSpot -> IMessageWriter.Register then retry)
  |     (if still null -> NoHandlerFoundException or MessageDescriptorNotFoundException)
  |- new MessageDependencies(...)  filter by predicate + tags, order by Priority then RegistrationSequence
  |- IMessageMediationStrategy.Mediate(message, dependencies, executionContext)
  |     |- RunAsyncPreStages
  |     |     1. PreStage.Guard      -> IMessageGuard<T>.DecideAsync      -> Verdict   (StopAtFirst)
  |     |     2. PreStage.Validator  -> IMessageValidator<T>.ValidateAsync -> Validity  (CollectFailures: all run)
  |     |     3. PreStage.Shortcut   -> IMessageShortcut<T>[,R].TryAnswerAsync -> Shortcut (StopAtFirst)
  |     |     4. PreStage.PreHandler -> IMessagePreHandler<T>.PreHandleAsync  (cannot stop)
  |     |     Within each stage: indirect (base-type) handlers run BEFORE direct handlers.
  |     |- if decision.StopsPipeline
  |     |     - refusal (Denied/Invalid): try IMessageRefusalMapper<T,R> -> value; else throw
  |     |       LiteBusMessageDeniedException / LiteBusMessageInvalidException
  |     |     - answered: PipelineDecision.ResolveResult<R>()
  |     |- main handler (exactly one for command/query; all for events grouped by priority)
  |     |- RunAsyncPostHandlers (direct then indirect; stops if IExecutionContext.SuppressPostHandlers was called)
  |     |- on catch: RunAsyncErrorHandlers (indirect then direct), rethrow unless MessageErrorOutcome.Handled
  |     |- finally: RunAsyncCompletionHandlers (one priority-ordered pass) with CancellationToken.None
  |- MediationScopeRetention.RetainUntilPipelineCompletes(result, resourceScope)
```

Rules baked into the code:

* Stage order is fixed by the declaration order of the `PreStage` enum and cannot be changed by priorities.
* `HandlerPriorityAttribute` orders handlers **within** a stage/role only, ascending; ties break on `RegistrationSequence` (module registration order).
* Every role except completion runs its direct (message-type) handlers and its indirect (base-type) handlers as two separate passes. The completion stage merges both descriptor sets into one `IMessageDependencies.CompletionHandlers` collection sorted by `Priority` then `RegistrationSequence`, so priority is the only ordering rule there. `IMessageDependencies` has no `IndirectCompletionHandlers`; `IMessageDescriptor` still separates them at the registry level.
* A pre-stage is skipped entirely when no handler occupies it (`IMessageDependencies.HasPreStageHandlers` bitmask).
* `MediationExceptionFilters.IsRecoverableMediationException` excludes `NoHandlerFoundException`, `MultipleHandlerFoundException`, `OperationCanceledException` and refusals from error handlers.
* Completion handlers always run, on every path, and are never cancelled. A completion handler that throws while the mediation had already failed has its exception collected into `exception.Data["LiteBus.SuppressedCompletionFaults"]` instead of replacing the original fault.

### 1.4 Lifecycle and thread safety

| Component | Lifetime | Thread safety |
| --- | --- | --- |
| `IMessageRegistry`, `IMessageReader`, `IMessageWriter` | **Singleton instance** created at composition time | Yes - all mutations and reads take a private lock. |
| `IMessageContractRegistry`, `IContractReader`, `IContractWriter` | **Singleton instance** | Yes - private lock over both lookup dictionaries. |
| `TimeProvider` | **Singleton instance** (`TimeProvider.System` unless overridden) | Yes. |
| `IAuditScope` (`AmbientAuditScope`) | **Singleton instance**; state is per-mediation via `AsyncLocal` items | Yes for the accessor; state is logically per-flow. |
| `IAuditOutcomeMapper` | **Singleton instance** | Application responsibility. |
| `IAuditTrail` | **Scoped** when registered by type, **Singleton** when registered as an instance | Application responsibility. |
| `IAuditRecordWriter` | **Scoped** (factory) | - |
| `IMessageMediator`, `ICommandMediator`, `IQueryMediator`, `IEventMediator` | **Transient** | Stateless; safe to resolve anywhere. |
| Every discovered handler type | **Scoped** (registered as itself) | One instance per dispatch scope. |
| `IMessageDispatchScopeFactory` | **Singleton** | Yes. |
| In-memory / PostgreSQL stores | **Singleton instance** | Yes (in-memory uses locks/concurrent collections; PostgreSQL is stateless over `NpgsqlDataSource`). |
| EF Core stores | **Singleton** over `IDbContextFactory<TContext>` | Each operation creates its own `DbContext`. |
| `ITransactionalInbox` / `ITransactionalOutbox` (+ generic) | **Scoped** | Bound to the caller's unit of work. |
| Processor options records (`InboxProcessorOptions`, `OutboxProcessorOptions`, host/cleanup options) | **Singleton instances** captured from the builder | Treat as immutable after composition. |
| `InboxProcessorControl` / `OutboxProcessorControl` | **Singleton instance** (also as `IInboxProcessorControl` / `IOutboxProcessorControl`) | Yes - lock + `TaskCompletionSource`. |
| `IInboxProcessor` / `IOutboxProcessor` | **Transient** (factory) | A new processor per resolution; each generates its own lease-owner suffix. |
| Background services and startup tasks | **Singleton** | Registered once each, deduplicated by type. |
| Transport clients (`IConnection`, `IProducer`, `IConsumer`, `IAmazonSQS`, `ServiceBusClient`, `InMemoryTransportBroker`) | **Singleton** | Provided by the broker SDK. |
| `ITransportPublisher` / `IMessageConsumer` | **Transient** for AMQP/Kafka/SQS/in-memory publisher; ASB consumer is **Singleton** | See adapter. |
| `ISagaStateTypeRegistry`, `SagaExecutionContext`, `ISagaContext`, `IProcessorEnvelopeHook` | **Singleton** | `SagaExecutionContext` keeps per-dispatch state in async-local storage. |

**Async execution model.** Everything hot is `Task`-based with `ConfigureAwait(false)`. Streaming queries return `IAsyncEnumerable<T>`; the dispatch scope is kept alive by `MediationScopeRetention.ScopeRetainedAsyncEnumerable<T>`, which **allows only one enumeration** (a second `GetAsyncEnumerator` throws `InvalidOperationException`). The mediator hands back the raw strategy result wrapped so that scope disposal happens after the task completes or the stream is fully enumerated/disposed.

### 1.5 Package map

| Package | Contains |
| --- | --- |
| `LiteBus` | Meta-package: Messaging, Commands, Queries, Events, Inbox, Outbox + their abstractions + `LiteBus.DurableMessaging.Abstractions`. |
| `LiteBus.Messaging.Abstractions` | Handler contracts, pipeline value types, contracts, metadata, audit contracts, exceptions. |
| `LiteBus.Messaging` | Registry, mediator, mediation strategies, pipeline runner, audit writer, serializer, shared durable processor machinery. |
| `LiteBus.Commands[.Abstractions]`, `LiteBus.Queries[.Abstractions]`, `LiteBus.Events[.Abstractions]` | Semantic axes. |
| `LiteBus.Runtime.Abstractions`, `LiteBus.Runtime` | Modules, dependency descriptors, diagnostics, hosting manifest. |
| `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | `AddLiteBus(this IServiceCollection, ...)`. |
| `LiteBus.Runtime.Extensions.Autofac` | `AddLiteBus(this ContainerBuilder, ...)`. |
| `LiteBus.Runtime.Extensions.Hosting`, `...Microsoft.Hosting`, `...Autofac.Hosting` | `IHostedService` orchestration for manifest startup tasks and background services. |
| `LiteBus.DurableMessaging.Abstractions` | Retry, idempotency, visibility, trace, tenancy, processor options/results, payload encryption, processor hooks. |
| `LiteBus.Inbox[.Abstractions]`, `LiteBus.Outbox[.Abstractions]` | Durable axes. |
| `LiteBus.Inbox.Storage.{InMemory,PostgreSql,EntityFrameworkCore}` and outbox equivalents | Storage adapters. |
| `LiteBus.Inbox.Dispatch[.{InProcess,InMemory,Amqp,Kafka,AwsSqs,AzureServiceBus}]` and outbox equivalents | Dispatch adapters. |
| `LiteBus.Inbox.Ingress[.{InMemory,Amqp,Kafka,AwsSqs,AzureServiceBus}]` | Broker -> inbox ingress. |
| `LiteBus.Transport.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.{InMemory,Amqp,Kafka,AwsSqs,AzureServiceBus}` | Broker-neutral transport plus adapters. |
| `LiteBus.Storage.PostgreSql`, `LiteBus.Storage.EntityFrameworkCore` | Shared relational helpers (schema bootstrap, lease SQL, provider detection). |
| `LiteBus.Saga[.Abstractions]`, `LiteBus.Saga.InboxIntegration`, `LiteBus.Saga.Storage.PostgreSql` | Saga orchestration. |
| `LiteBus.Extensions.AspNetCore` | Operator management HTTP endpoints. |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | `IHealthChecksBuilder.AddLiteBus(...)`. |
| `LiteBus.Inbox.Extensions.OpenTelemetry`, `LiteBus.Outbox.Extensions.OpenTelemetry`, `LiteBus.Transport.Extensions.OpenTelemetry`, `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` | OTel registration helpers. |
| `LiteBus.Testing`, `LiteBus.Testing.Mediation`, `LiteBus.Testing.Hosting`, `LiteBus.Testing.Transport`, `LiteBus.Testing.DurableMessaging` | Test doubles and fixtures. |
| `LiteBus.Analyzers` | Roslyn analyzers `LB1001`-`LB1021`. |

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

1. Wraps `services` in a `MicrosoftDependencyRegistryAdapter`.
2. Registers `IMessageDispatchScopeFactory` -> `MicrosoftMessageDispatchScopeFactory` as **Singleton** (this is why `MessageModule.Build` can assert its presence).
3. Creates a `ModuleRegistry`, wraps it in `LiteBusBuilder`, and invokes `configure`.
4. Calls `moduleRegistry.BuildOrder()` (freezes registration, topologically sorts) and calls `Build(moduleConfiguration)` on each module in order.
5. Registers `LiteBusHostManifest` (Singleton) built from the module configuration.
6. Registers every diagnostic check implementation type as Singleton.
7. Registers startup tasks + background services as Singletons and one `IHostedService` (`LiteBusHostOrchestrator`).

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

Extra behaviour versus Microsoft DI: it first registers `IServiceProvider` -> `AutofacServiceProviderAdapter` as `InstancePerLifetimeScope`, so factory descriptors can resolve services; then `IMessageDispatchScopeFactory` -> `AutofacMessageDispatchScopeFactory` (Singleton). Lifetime translation: `Transient` -> `InstancePerDependency`, `Singleton` -> `SingleInstance`, `Scoped` -> `InstancePerLifetimeScope`. Open generic implementation types are registered with `RegisterGeneric`.

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

`ILiteBusBuilder` extension methods (each also has an `IModuleRegistry` form):

| Method | Package | Registers |
| --- | --- | --- |
| `AddMessaging(Action<MessageModuleBuilder>)` | `LiteBus.Messaging` | `MessageModule` |
| `AddCommands(Action<CommandModuleBuilder>)` | `LiteBus.Commands` | `CommandModule` (requires `MessageModule`) |
| `AddQueries(Action<QueryModuleBuilder>)` | `LiteBus.Queries` | `QueryModule` (requires `MessageModule`) |
| `AddEvents(Action<EventModuleBuilder>)` | `LiteBus.Events` | `EventModule` (requires `MessageModule`) |
| `AddInbox(Action<InboxModuleBuilder>)` | `LiteBus.Inbox` | `InboxModule` (composite; requires `MessageModule`) |
| `AddOutbox(Action<OutboxModuleBuilder>)` | `LiteBus.Outbox` | `OutboxModule` (composite; requires `MessageModule`) |
| `AddInMemoryTransport()` | `LiteBus.Transport.InMemory` | `InMemoryTransportModule` |
| `AddAmqpTransport(AmqpConnectionOptions)` | `LiteBus.Transport.Amqp` | `AmqpTransportModule` |
| `AddKafkaTransport(KafkaTransportOptions)` | `LiteBus.Transport.Kafka` | `KafkaTransportModule` |
| `AddAwsSqsTransport(AwsSqsTransportOptions)` | `LiteBus.Transport.AwsSqs` | `AwsSqsTransportModule` |
| `AddAzureServiceBusTransport(AzureServiceBusTransportOptions)` | `LiteBus.Transport.AzureServiceBus` | `AzureServiceBusTransportModule` |

`IModuleRegistry` extensions: `AddMessageModule`, `AddCommandModule`, `AddQueryModule`, `AddEventModule` (with and without a builder action), `AddInboxModule` (with and without), `AddOutboxModule` (with and without). `IModuleRegistry.Register(IModule)` accepts any module directly; `IsModuleRegistered<T>()` reports whether a module type is present.

### 2.4 Exactly what each module registers

**`MessageModule`** (throws `LiteBusConfigurationException` when no `IMessageDispatchScopeFactory` descriptor exists yet):

| Service | Implementation | Lifetime |
| --- | --- | --- |
| `IMessageRegistry`, `IMessageReader`, `IMessageWriter` | the same `MessageRegistry` instance | Singleton (instance) |
| `IMessageContractRegistry`, `IContractReader`, `IContractWriter` | the same `MessageContractRegistry` instance | Singleton (instance) |
| `IMessageSerializer` | `SystemTextJsonMessageSerializer` | Transient |
| `TimeProvider` | builder value or `TimeProvider.System` | Singleton (instance) |
| `IAuditScope` | `AmbientAuditScope` | Singleton (instance) |
| `IAuditOutcomeMapper` | builder value or `DefaultAuditOutcomeMapper` | Singleton (instance) |
| `IAuditTrail` | only if `UseAuditTrail`/`UseAuditTrailInstance` was called | Lifetime passed to `UseAuditTrail<T>` (Scoped by default) / Singleton for an instance |
| `IAuditRecordWriter` | `AuditRecordWriter` factory | Scoped |
| `IMessageMediator` | `MessageMediator` | Transient |
| every newly discovered handler type | itself | Scoped |

**`CommandModule`**: `ICommandMediator` -> `CommandMediator` (Transient); every handler type discovered in this module as Scoped; registers `AuditTrailDiagnosticCheck` (`litebus.audit.trail`) when `EnableAuditing()` was called.

**`QueryModule`**: same shape with `IQueryMediator` -> `QueryMediator`.

**`EventModule`**: `IEventMediator` -> `EventMediator` (Transient) + handlers.

**`InboxModule`** (composite, `ParentFirst`; children are storage, then dispatcher, then ingress, then feature bridges such as saga):

| Service | Notes |
| --- | --- |
| `IMessageContractRegistry`, `IContractReader` | the shared contract registry (idempotent re-registration) |
| `InboxProcessorOptions` | instance from the builder |
| `IInboxPayloadProtector` | only when `UsePayloadEncryption` was called |
| `IInboxEnvelopeFactory` -> `InboxEnvelopeFactory` | Transient |
| `IInbox` -> `Inbox` | Transient |
| `InboxCleanupHostOptions`, `InboxRetentionCoordinator` | instances |
| `IInboxManager` -> `InboxManager` | Transient |
| `IInboxProcessor` | factory (`InboxProcessorFactory.Create`), Transient |
| `InboxProcessorControl`, `IInboxProcessorControl`, `InboxProcessorHostOptions`, `InboxProcessorBackgroundService` | only when `EnableInboxProcessor()` was called; the background service is added to the host manifest |
| `InboxCleanupBackgroundService` | only when `EnableCleanup()` was called |
| `InboxObservableMetrics` | Singleton factory + `InboxObservableMetricsInitializer` startup task |
| consumer diagnostic probes | one per `AddDiagnosticCheck<TCheck>(name)` call, Singleton |

`Build` throws `LiteBusConfigurationException` when storage is missing, or when the processor is enabled without a dispatcher.

**`OutboxModule`**: mirrors the inbox with `OutboxProcessorOptions`, `IOutboxPayloadProtector`, `IOutboxEnvelopeFactory`, `IOutbox`, `OutboxCleanupHostOptions`, `OutboxRetentionCoordinator`, `IOutboxManager`, `IOutboxProcessor`, `OutboxProcessorControl`, `OutboxProcessorHostOptions`, `OutboxProcessorBackgroundService`, `OutboxCleanupBackgroundService`, `OutboxObservableMetrics` + initializer.

### 2.5 Direct instantiation without a DI container

`MessageModule` requires an `IMessageDispatchScopeFactory`; for a container-less host, use `RootMessageDispatchScopeFactory`, which exposes a supplied root provider and never disposes it.

`MessageRegistry`, `MessageContractRegistry`, `MessageMediator`, `ModuleRegistry` and `ModuleConfiguration` are all `internal`, so there is no fully hand-rolled composition path. The container-less pattern is to run the same `AddLiteBus` composition over a plain `ServiceCollection` (no host, no ASP.NET Core):

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
// Generic host: AddLiteBus already registers LiteBusHostOrchestrator as IHostedService.
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

Manual host control for tests (`LiteBus.Testing.Hosting`):

```csharp
await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, ct);
var inboxLoop = LiteBusHostedServiceExtensions.GetInboxProcessorHostedService(provider);
LiteBusHostedServiceExtensions.AssertBackgroundServices(provider, typeof(InboxProcessorBackgroundService));
await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, ct);
```

---

## 3. Configuration & All Options (Exhaustive)

Conventions used below: "Required?" means the compiler or a runtime guard forces a value. `required` marks C# `required` members. Properties declared `{ get; init; }` with no initializer default to `null` (reference/nullable) or the type default (`0`, `false`, `TimeSpan.Zero`, `default(T)`).

### 3.1 Mediation settings

#### `CommandMediationSettings` (`LiteBus.Commands.Abstractions`, sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Routing` | `CommandRoutingSettings` | `new CommandRoutingSettings()` | No | Tag and predicate filters applied to every handler role for this send. |
| `Items` | `IDictionary<string, object>` | `new Dictionary<string, object>()` | No | Get-only collection copied into `IExecutionContext.Items`. Used to pass out-of-band data to handlers; this is also how inbox dispatch injects trace metadata. |

#### `CommandRoutingSettings` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Tags` | `IEnumerable<string>` | `[]` (empty) | No | A handler participates when it carries no tags, or when its tags intersect this set. An empty set therefore selects untagged handlers only. |
| `HandlerPredicate` | `Func<IHandlerDescriptor, bool>` | `_ => true` | No | Second filter applied after tag filtering, evaluated per descriptor before the handler instance is resolved. |

#### `QueryMediationSettings` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Routing` | `QueryRoutingSettings` | `new QueryRoutingSettings()` | No | Tag/predicate filter for query handlers. |
| `Items` | `IDictionary<string, object>` | `new Dictionary<string, object>()` | No | Copied into `IExecutionContext.Items`. Unlike the command variant this property has an `init` accessor. |

#### `QueryRoutingSettings` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Tags` | `IEnumerable<string>` | `[]` | No | Same semantics as `CommandRoutingSettings.Tags`. |
| `HandlerPredicate` | `Func<IHandlerDescriptor, bool>` | `_ => true` | No | Same semantics as `CommandRoutingSettings.HandlerPredicate`. |

#### `EventMediationSettings` (`LiteBus.Events.Abstractions`, sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `ThrowIfNoHandlerFound` | `bool` | `false` | No | When `true`, publishing an event with zero applicable main handlers throws `NoHandlerFoundException`. When `false` the publish silently no-ops after the pre-stages. |
| `AutoRegisterUnregisteredMessageTypes` | `bool` | `false` | No | Sets `MessageMediationRequest.RegisterPlainMessagesOnSpot`. When `true`, an unknown event type is registered into the `IMessageRegistry` at publish time instead of throwing `NoHandlerFoundException`. |
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
| `PriorityGroupsConcurrencyMode` | `ConcurrencyMode` | `Sequential` | No | `Sequential` runs each priority group to completion before the next; `Parallel` starts every priority group at once, discarding priority as an ordering guarantee. |
| `HandlersWithinSamePriorityConcurrencyMode` | `ConcurrencyMode` | `Sequential` | No | Controls whether handlers sharing one priority run one at a time (ordered by `RegistrationSequence`) or concurrently. |
| `ParallelFaultMode` | `ParallelFaultMode` | `PropagateFirst` | No | `PropagateFirst` uses `Task.WhenAll`, surfacing the first fault after siblings settle. `AggregateAll` awaits every task individually and throws the single exception, or an `AggregateException` when more than one failed. |

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
| `MaxAttempts` | `int` | `5` | No | When `envelope.AttemptCount >= MaxAttempts` after a dispatch failure the envelope is dead-lettered instead of retried. Validated `> 0`. |
| `InitialDelay` | `TimeSpan` | `5 seconds` | No | Base delay for attempt 1. Validated `>= TimeSpan.Zero`. |
| `MaxDelay` | `TimeSpan` | `5 minutes` | No | Hard cap applied before and after jitter. Validated `>= TimeSpan.Zero`. |
| `Backoff` | `RetryBackoff` | `Exponential` | No | `Fixed` always uses `InitialDelay`; `Exponential` uses `InitialDelay * 2^(attemptCount-1)`. Validated with `Enum.IsDefined`. |
| `UseJitter` | `bool` | `true` | No | Multiplies the computed delay by a random factor in `[0.8, 1.2)`, then re-caps at `MaxDelay`. Disable for deterministic tests. |

Method: `TimeSpan CalculateDelay(int attemptCount)`.

#### `ProcessorOptions` (`LiteBus.Messaging.Abstractions.Processing`, public record; base of `InboxProcessorOptions` and `OutboxProcessorOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `BatchSize` | `int` | `20` | No | Maximum envelopes leased per pass. Validated `> 0`. Also drives adaptive polling: a full batch skips the poll delay. |
| `LeaseDuration` | `TimeSpan` | `1 minute` | No | Lease expiry written when the processor claims a message. Validated `> TimeSpan.Zero`. |
| `Retry` | `RetryOptions` | `new RetryOptions()` | No | Retry/dead-letter policy for dispatch failures. |
| `LeaseOwner` | `string?` | `null` | No | Base lease-owner name. When null or whitespace the processor uses `MachineName:ProcessId`. The pipelined processor always appends a `Guid` suffix so each processor instance is uniquely fenced. |
| `DispatcherConcurrency` | `int` | `1` | No | Number of parallel dispatch workers within one pass (a `SemaphoreSlim` gate). Validated `> 0`. |
| `LeaseHeartbeatInterval` | `TimeSpan` | `15 seconds` | No | Lease renewal cadence while dispatch runs. `TimeSpan.Zero` disables heartbeating. Validated `>= 0` and, when positive, `<= LeaseDuration / 2`. |
| `TenantId` | `string?` | `null` | No | Restricts leasing to one tenant partition (passed into the lease request). |
| `HonorShutdownTokenOnPersist` | `bool` | `false` | No | When `false` the terminal persist uses the heartbeat token; when `true` it uses the original shutdown token, so persistence can still be cancelled by host shutdown. |
| `HookFailurePolicy` | `ProcessorHookFailurePolicy` | `DeadLetter` | No | Applied when an `IProcessorEnvelopeHook.AfterDispatchAsync` throws after a successful dispatch. `OutboxModuleBuilder` overrides the default to `CompleteDespiteHookFailure` when a transport dispatcher is registered and `UseProcessorOptions` was not called. |

`InboxProcessorOptions` and `OutboxProcessorOptions` are `sealed record X : ProcessorOptions` with no added members.

#### `ProcessorHostOptions` (public class; base of `InboxProcessorHostOptions` and `OutboxProcessorHostOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | No | When `false` the background loop returns immediately at startup. |
| `PollInterval` | `TimeSpan` | `1 second` | No | Delay after an empty or partial pass. `TimeSpan.Zero` means no delay (hot loop). Validated `>= TimeSpan.Zero`. |
| `StartupDelay` | `TimeSpan` | `TimeSpan.Zero` | No | One-off delay before the first pass; interruptible by a drain request. Validated `>= TimeSpan.Zero`. |
| `UseAdaptivePolling` | `bool` | `true` | No | When `true` and the last pass leased a full `BatchSize`, the poll delay is skipped so a backlog drains at full speed. |

Method: `void Validate()`. `InboxProcessorHostOptions` and `OutboxProcessorHostOptions` are empty sealed subclasses.

#### `InboxCleanupHostOptions` / `OutboxCleanupHostOptions` (sealed classes)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | No | When `false` the retention loop exits at startup. |
| `Interval` | `TimeSpan` | `1 hour` | No | Delay between cleanup passes. Validated `> TimeSpan.Zero`. On failure the loop backs off exponentially, doubling up to a 5-minute cap. |
| `Retention` | `TimeSpan?` | `null` | No | How long terminal rows (Completed / Published) are kept. **When `null` the cleanup loop returns immediately and `RunRetentionPurgeAsync` returns 0.** Validated `> TimeSpan.Zero` when set. |

Method: `void Validate()`.

#### `LeaseRenewalRequest` (sealed record, positional)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MessageId` | `Guid` | none | **Yes** | Envelope being renewed. |
| `LeaseOwner` | `string` | none | **Yes** | Must match the stored owner or renewal fails. |
| `LeaseGeneration` | `long` | none | **Yes** | Fencing token; a mismatch means another worker re-leased the row. |
| `LeaseDuration` | `TimeSpan` | none | **Yes** | Requested extension length. |
| `RequestedExpiresAt` | `DateTimeOffset` | none | **Yes** | Absolute expiry computed from the caller's clock. |

#### `InboxLeaseRequest` / `OutboxLeaseRequest` (sealed records)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `BatchSize` | `int` | none | **Yes** (`required`) | Maximum rows to claim. |
| `LeaseOwner` | `string` | none | **Yes** (`required`) | Owner written on the claimed rows. |
| `Now` | `DateTimeOffset` | none | **Yes** (`required`) | Timestamp used for `VisibleAfter` and lease-expiry comparisons. |
| `LeaseDuration` | `TimeSpan` | none | **Yes** (`required`) | Lease length. In-memory stores substitute `DefaultLeaseDuration` when this is zero. |
| `TenantId` | `string?` | `null` | No | Restricts the claim to one tenant partition. |

#### `PersistResult` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `AppliedCount` | `int` | constructor | **Yes** | Envelopes whose terminal state was written. Validated non-negative. |
| `SkippedCount` | `int` | constructor | **Yes** | Envelopes skipped because the lease was lost or no longer matched. Validated non-negative. |

Statics: `PersistResult.Empty`, `AllApplied(int)`, `FromOutcome(int, int)`, `FromMessageIds(IReadOnlyList<Guid>, ISet<Guid>)`.

#### `ProcessorPassResult` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `LeasedCount` | `int` | none | **Yes** (`required`) | Envelopes claimed in the pass; compared against `BatchSize` for adaptive polling. |
| `SucceededCount` | `int` | `0` | No | Completed/published envelopes. |
| `FailedCount` | `int` | `0` | No | Envelopes marked failed for retry. |
| `DeadLetteredCount` | `int` | `0` | No | Envelopes moved to dead letter. |
| `ElapsedTime` | `TimeSpan` | `TimeSpan.Zero` | No | Wall-clock duration of the pass including leasing and persistence. |

### 3.3 Durable acceptance and enqueue metadata

#### `InboxAcceptMetadata` (sealed record)

All five members are `required`. `InboxAcceptMetadata.Immediate` is the canonical starting point; refine it with `with`.

| Property Name | Data Type | Default (`Immediate`) | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Identity` | `MessageIdentity` | `MessageIdentity.Generated.Instance` | **Yes** | `Generated` lets the factory create a `Guid`; `Supplied(Guid)` stores the caller's id, which stores treat as a duplicate key. |
| `Idempotency` | `Idempotency` | `Idempotency.None.Instance` | **Yes** | `Keyed(key, conflictMode)` writes `IdempotencyKey`; duplicates either return the existing row or throw. |
| `Visibility` | `MessageVisibility` | `MessageVisibility.Immediate.Instance` | **Yes** | `At(DateTimeOffset)` or `After(TimeSpan)` set `VisibleAfter`, deferring leasing. |
| `Trace` | `MessageTrace` | `MessageTrace.None.Instance` | **Yes** | `Correlated`, `Workflow`, `Distributed` map to `CorrelationId` / `CausationId` / `TraceContext` columns. |
| `Tenant` | `TenantScope` | `TenantScope.Unscoped.Instance` | **Yes** | `Isolated(tenantId)` writes `TenantId`, which participates in the idempotency uniqueness scope. |

#### `OutboxEnqueueMetadata` (sealed record)

Same five members plus one; `OutboxEnqueueMetadata.Immediate` is the base value.

| Property Name | Data Type | Default (`Immediate`) | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Identity` | `MessageIdentity` | `Generated.Instance` | **Yes** | As above. |
| `Idempotency` | `Idempotency` | `None.Instance` | **Yes** | As above. |
| `Visibility` | `MessageVisibility` | `Immediate.Instance` | **Yes** | As above. |
| `Trace` | `MessageTrace` | `None.Instance` | **Yes** | As above. |
| `Tenant` | `TenantScope` | `Unscoped.Instance` | **Yes** | As above. |
| `Target` | `PublicationTarget` | `PublicationTarget.ContractDefault.Instance` | **Yes** | `Topic(name)`, `Exchange(name)` and `Queue(name)` write the `Topic` column, which `TransportOutboxDispatcher` prefers over `ResolveRoute` and the contract name. |

#### `InboxAcceptItem` (sealed record, untyped)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Message` | `object` | none | **Yes** (`required`) | The instance serialized into the payload. |
| `MessageType` | `Type?` | `null` | No | Overrides contract lookup for heterogeneous batches; falls back to `Message.GetType()`. The factory validates assignability and throws `ArgumentException` otherwise. |
| `Metadata` | `InboxAcceptMetadata` | `InboxAcceptMetadata.Immediate` | No | Durable annotations. |

Statics: `From(object, InboxAcceptMetadata?)`, `From(object, Type, InboxAcceptMetadata?)`, `From<TMessage>(InboxAcceptItem<TMessage>)`.

#### `InboxAcceptItem<TMessage>` (sealed record, `TMessage : notnull`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Message` | `TMessage` | none | **Yes** (`required`) | Typed message instance. |
| `Metadata` | `InboxAcceptMetadata` | `InboxAcceptMetadata.Immediate` | No | Durable annotations. |

Statics: `From(TMessage, InboxAcceptMetadata?)`, `ScheduledAt(TMessage, DateTimeOffset)`, `ScheduledAfter(TMessage, TimeSpan)`, `WithIdempotency(TMessage, string)`, `WithIdentity(TMessage, Guid)`, `WithCorrelation(TMessage, string)`, `WithTrace(TMessage, MessageTrace)`, `WithTenant(TMessage, string)`.

#### `OutboxEnqueueItem` (sealed record, untyped)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Message` | `object` | none | **Yes** (`required`) | Event instance. |
| `MessageType` | `Type` | none | **Yes** (`required`) | Runtime type used for contract lookup. Unlike the inbox item this is not optional. |
| `Metadata` | `OutboxEnqueueMetadata` | `OutboxEnqueueMetadata.Immediate` | No | Durable annotations. |

Statics: `From(object)`, `From(object, Type)`, `From(object, Type, OutboxEnqueueMetadata)`.

#### `OutboxEnqueueItem<TEvent>` (sealed record, `TEvent : notnull`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Message` | `TEvent` | none | **Yes** (`required`) | Typed event. |
| `Metadata` | `OutboxEnqueueMetadata` | `OutboxEnqueueMetadata.Immediate` | No | Durable annotations. |

Statics: `From(TEvent)`, `From(TEvent, OutboxEnqueueMetadata)`, `ScheduledAt`, `ScheduledAfter`, `WithIdempotency`, `WithIdentity`, `WithTopic(TEvent, string)`.

#### `InboxReceipt` / `InboxReceipt<TMessage>` (sealed records)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Id` | `Guid` | none | **Yes** (`required`) | Durable message identifier for tracking endpoints. |
| `MessageType` | `Type` | none | **Yes** (`required`) | The accepted CLR type (closed generic when applicable). |
| `Contract` | `MessageContractReference` | none | **Yes** (`required`) | Stable `Name` + `Version` written with the payload. |
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
| `ContractName` | `string` | none | **Yes** (`required`) | `contract_name`; resolves the CLR type at dispatch. |
| `ContractVersion` | `int` | none | **Yes** (`required`) | `contract_version`. |
| `Payload` | `string` | none | **Yes** (`required`) | Serialized, optionally encrypted message text; `payload` column. |
| `CreatedAt` | `DateTimeOffset` | none | **Yes** (`required`) | `created_at`; keyset pagination anchor. |
| `VisibleAfter` | `DateTimeOffset?` | `null` | No | `visible_after`; leasing skips rows whose value is in the future. |
| `AttemptCount` | `int` | none | **Yes** (`required`) | `attempt_count`; incremented by `AsLeased`. |
| `Status` | `InboxStatus` | none | **Yes** (`required`) | `status`, stored as `int`. |
| `IdempotencyKey` | `string?` | `null` | No | `idempotency_key`; unique together with `tenant_id` where not null. |
| `IdempotencyConflictMode` | `IdempotencyConflictMode` | `ReturnExisting` | No | **Not persisted**; carries the accept-time policy to the store. |
| `LeaseOwner` | `string?` | `null` | No | `lease_owner`. |
| `LeaseGeneration` | `long` | `0` | No | `lease_generation`; monotonic fencing token, `checked` increment in `AsLeased`. |
| `LeaseExpiresAt` | `DateTimeOffset?` | `null` | No | `lease_expires_at`; an expired lease makes the row claimable again. |
| `LastError` | `string?` | `null` | No | `last_error`; truncated to 1024 characters by `MessageProcessorDiagnostics.FormatError`. |
| `CorrelationId` | `string?` | `null` | No | `correlation_id`. |
| `CausationId` | `string?` | `null` | No | `causation_id`. |
| `TenantId` | `string?` | `null` | No | `tenant_id`. |
| `TraceContext` | `string?` | `null` | No | `trace_context`; W3C trace parent string or a JSON object with `traceparent` and optional `tracestate`. |
| `CompletedAt` | `DateTimeOffset?` | `null` | No | `completed_at`; retention cutoff column. |

Transitions: `AsLeased(owner, expiresAt)` (from any status), `AsCompleted()`, `AsFailed(error, visibleAfter?)`, `AsDeadLettered(reason)` (all three require `Processing`), `AsRequeued()` (requires `DeadLettered`; resets `VisibleAfter`, `AttemptCount`, lease fields and `LastError`). A wrong source status throws `InvalidOperationException`. `AsFailed`/`AsDeadLettered` throw `ArgumentException` on a null or whitespace reason.

#### `OutboxEnvelope` (sealed record)

Identical to `InboxEnvelope` except for these members:

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Topic` | `string?` | `null` | No | `topic`; explicit publication destination, preferred by `TransportOutboxDispatcher` over any configured route resolver. Indexed where not null. |
| `Status` | `OutboxStatus` | none | **Yes** (`required`) | `Pending` / `Publishing` / `Published` / `Failed` / `DeadLettered`. |
| `PublishedAt` | `DateTimeOffset?` | `null` | No | `published_at`; retention cutoff column. |

Transitions: `AsLeased`, `AsPublished(DateTimeOffset publishedAt)` (requires `Publishing`), `AsFailed`, `AsDeadLettered`, `AsRequeued`.

Both relational schemas additionally persist operational columns that are not on the envelope record: `last_attempted_at`, `first_failed_at`, `dead_lettered_at`, `last_lease_owner`, `error_type`.

### 3.5 Query / purge filters and paging

#### `InboxMessageFilter` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MessageId` | `Guid?` | `null` | No | Exact id match. |
| `MessageIds` | `IReadOnlyList<Guid>?` | `null` | No | Id set match. |
| `Statuses` | `IReadOnlyList<InboxStatus>?` | `null` | No | Status set; null or empty means no status filter. |
| `ContractName` | `string?` | `null` | No | Exact contract name match. |
| `CorrelationId` | `string?` | `null` | No | Exact match. |
| `CausationId` | `string?` | `null` | No | Exact match. |
| `TenantId` | `string?` | `null` | No | Exact match. |
| `CreatedAfter` | `DateTimeOffset?` | `null` | No | Inclusive lower bound on `CreatedAt`. |
| `CreatedBefore` | `DateTimeOffset?` | `null` | No | Inclusive upper bound on `CreatedAt`. |

Static: `InboxMessageFilter.DeadLettered` (only `Statuses = [DeadLettered]`). Extensions: `HasMinimumCriteria()` and `IsUnrestricted()`. `IInboxManager.PurgeAsync` refuses an unrestricted filter unless `confirm: true`, throwing `InboxManagementException`.

#### `OutboxMessageFilter` (sealed record)

Same members plus `Topic` (`string?`, default `null`, exact match). Static `OutboxMessageFilter.DeadLettered`; extensions `HasMinimumCriteria()` / `IsUnrestricted()`; `IOutboxManager.PurgeAsync` throws `OutboxManagementException` for an unconfirmed unrestricted purge.

#### `InboxMessagePageRequest` / `OutboxMessagePageRequest` (sealed records)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `PageSize` | `int` | `50` | No | Rows per page. The ASP.NET Core management endpoints clamp this to `LiteBusManagementOptions.MaxPageSize`. |
| `Cursor` | `string?` | `null` | No | Opaque Base64 keyset cursor holding `CreatedAt` and `MessageId`, produced by `InboxMessagePageCursor.Encode` / `OutboxMessagePageCursor.Encode`. `null` requests the first page. |

`InboxMessagePage` / `OutboxMessagePage` are positional records: `(IReadOnlyList<Envelope> Items, bool HasMore, string? NextCursor)`. `RequeueResult` (both axes) is `(int Requested, int Requeued)`.

### 3.6 Storage adapter options

#### `InMemoryInboxStoreOptions` / `InMemoryOutboxStoreOptions` (sealed records, implement `IMessageStoreRetentionOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Capacity` | `int` | `0` | No | `0` means unbounded. A positive value makes the store throw `InboxStorageException` / `OutboxStorageException` once that many rows exist. A negative value throws `ArgumentOutOfRangeException` from the store constructor. |
| `DefaultLeaseDuration` | `TimeSpan` | `1 minute` | No | Substituted when a lease request supplies `TimeSpan.Zero`. Validated `> TimeSpan.Zero` by the store constructor. |
| `TerminalRetention` | `TimeSpan?` | `null` | No | Advisory retention window exposed through `IMessageStoreRetentionOptions`. |

#### `PostgreSqlSchemaStoreOptions` (public record; base of the PostgreSQL store options)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MetadataSchemaName` | `string` | `"public"` | No | Schema holding the LiteBus schema-version metadata table. |
| `MetadataTableName` | `string` | `"litebus_schema_versions"` | No | Table holding one row per component with its recorded schema version. |
| `Logger` | `IPostgreSqlSchemaLogger?` | `null` | No | Receives schema create/upgrade/validate entries at `PostgreSqlSchemaLogLevel`. |
| `ValidateIndexesOnStartup` | `bool` | `true` | No | When `true`, validation also asserts that the required indexes exist on the store table. |

#### `PostgreSqlInboxStoreOptions` (sealed record : `PostgreSqlSchemaStoreOptions`, `IPostgreSqlStoreTableOptions`, `IMessageStoreRetentionOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `SchemaName` | `string` | `"public"` | No | Schema containing the inbox table. |
| `TableName` | `string` | `"litebus_inbox_messages"` | No | Inbox table name. |
| `EnsureSchemaCreationOnStartup` | `bool` | `true` | No | Startup task creates or upgrades the schema. |
| `ValidateSchemaCreationOnStartup` | `bool` | `true` | No | Startup fails when the table does not match the expected schema version. |
| `UseListenNotify` | `bool` | `false` | No | When `true` the module registers `PostgreSqlInboxWorkSignal` (LISTEN/NOTIFY) instead of `InboxPollingWorkSignal`, so an insert wakes the processor without waiting for the poll interval. Polling remains the fallback. |
| `TerminalRetention` | `TimeSpan?` | `null` | No | Advisory retention window. |
| `MetadataSchemaName`, `MetadataTableName`, `Logger`, `ValidateIndexesOnStartup` | inherited | see base | No | Schema metadata settings. |

#### `PostgreSqlOutboxStoreOptions`

Identical members; `TableName` defaults to `"litebus_outbox_messages"`.

#### `EntityFrameworkCoreInboxStoreOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `SchemaName` | `string` | `"public"` | No | Schema used by `ToTable`; ignored for MySQL and SQLite, which have no schemas. |
| `TableName` | `string` | `"litebus_inbox_messages"` | No | Table name. |
| `LeaseProvider` | `EfCoreStorageProvider?` | `null` | No | Forces the raw-SQL lease dialect. When `null` the provider is detected from `DbContext.Database.ProviderName`. |

#### `EntityFrameworkCoreOutboxStoreOptions`

Identical members; `TableName` defaults to `"litebus_outbox_messages"`.

#### `PostgreSqlSagaStoreOptions` (sealed record : `PostgreSqlSchemaStoreOptions`, `IPostgreSqlStoreTableOptions`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `SchemaName` | `string` | `"public"` | No | Schema containing saga tables. |
| `TableName` | `string` | `"litebus_saga_instances"` | No | Saga instance table. |
| `EnsureSchemaCreationOnStartup` | `bool` | `true` | No | Startup creates the schema. |
| `ValidateSchemaCreationOnStartup` | `bool` | `false` | No | **Off by default**, unlike the inbox and outbox options. |
| `MetadataSchemaName`, `MetadataTableName`, `Logger`, `ValidateIndexesOnStartup` | inherited | see base | No | Schema metadata settings. |

#### `IPostgreSqlStoreTableOptions` (interface implemented by the three PostgreSQL option records)

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
| `TerminalRetention` | `TimeSpan?` | How long completed/published rows may remain before cleanup deletes them. |

### 3.7 Dispatch adapter options

#### `TransportInboxDispatcherOptions` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `DefaultDestination` | `string` | `string.Empty` | No, but effectively required per broker | `TransportPublishRequest.Destination`: AMQP exchange, Kafka topic, SQS queue URL, or Service Bus entity. |
| `ContentType` | `string` | `"application/json"` | No | Written to transport message properties. |
| `Persistent` | `bool` | `true` | No | Requests broker-side persistence where supported. |
| `Mandatory` | `bool` | `true` | No | Requires the broker to route to at least one consumer; an unroutable publish fails. Note this defaults to `true` here while `TransportPublishRequest.Mandatory` defaults to `false`. |
| `ResolveRoute` | `Func<InboxEnvelope, string>?` | `null` | No | Per-envelope route. Ignored when an `ITenantRoutingStrategy` is registered. When both are absent the route is `envelope.ContractName`. |
| `ValidatePayloadBeforeDispatch` | `bool` | `false` | No | When `true` the payload is deserialized before publishing, turning a contract-wiring mistake into a dispatch failure instead of a poison message downstream. |

#### `TransportOutboxDispatcherOptions` (sealed class)

Same six members with `ResolveRoute` typed `Func<OutboxEnvelope, string>?`. Route precedence: `ITenantRoutingStrategy` -> `envelope.Topic` -> `ResolveRoute` -> `envelope.ContractName`.

### 3.8 Inbox ingress options

#### `TransportInboxIngressSafetyOptions` (sealed record) - shared by every broker ingress

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MaxMessageBytes` | `int` | `4194304` (4 MiB, `DefaultMaxMessageBytes`) | No | Deliveries larger than this are rejected before deserialization. `0` disables the limit. A negative value throws `LiteBusConfigurationException` from `Validate()`. |
| `RequireStableIdentity` | `bool` | `true` | No | Requires a stable broker delivery identity so ingress can derive a deterministic message id; without it, redelivery would create duplicate inbox rows. |
| `TrustApplicationHeaders` | `bool` | `false` | No | When `true`, LiteBus application headers may override broker-derived identity and tenant metadata. Leave `false` for untrusted publishers. |
| `AuthorizeDeliveryAsync` | `Func<TransportMessage, CancellationToken, Task>?` | `null` | No | Invoked before deserialization; throw to reject a delivery. |
| `MaxInFlightMessages` | `int` | `32` (`TransportConsumerOptions.DefaultMaxInFlightMessages`) | No | LiteBus-side concurrency gate for ingress handlers. Validated `>= 1`. |
| `EnableBatchAccept` | `bool` | `false` | No | Buffers deliveries and accepts them through `IInbox.AcceptBatchAsync`. |
| `BatchSize` | `int` | `10` (`DefaultBatchSize`) | No | Deliveries per batch accept. Validated `>= 1`. |
| `BatchMaxWait` | `TimeSpan` | `200 ms` | No | Maximum wait before flushing a partial batch. Validated `>= TimeSpan.Zero`. |

Method: `void Validate()`.

#### `TransportInboxIngressOptions` (sealed record) - the transport-neutral consumer loop options

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Destination` | `string` | `string.Empty` | No | Subscription source. |
| `SubscriptionName` | `string?` | `null` | No | Named subscription for topic destinations. |
| `PrefetchCount` | `int` | `0` | No | Native broker prefetch. |
| `ReceiveBatchSize` | `int` | `1` | No | Messages requested per receive call for batch-receiving transports (SQS). |
| `MaxConcurrentCalls` | `int?` | `null` | No | Native broker callback concurrency (Azure Service Bus). |
| `DeclareDestination` | `bool` | `false` | No | Whether the consumer declares the destination. Note this default is `false` while `TransportConsumerOptions.DeclareDestination` defaults to `true`. |
| `DurableDestination` | `bool` | `false` | No | Whether a declared destination survives broker restarts. Same default caveat. |
| `RequeueOnFailure` | `bool` | `true` | No | Whether a failed store write returns the delivery to the broker; `IngressAckPolicy` still refuses to requeue non-transient failures. |
| `Safety` | `TransportInboxIngressSafetyOptions` | `new()` | No | Safety limits above. |

Constant: `TransportInboxIngressOptions.DefaultMaxMessageBytes` (same value as the safety constant).

#### `TransportInboxIngressHostOptions` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Enabled` | `bool` | `true` | No | When `false` the ingress background loop returns immediately. |
| `RetryPollInterval` | `TimeSpan` | `5 seconds` | No | Delay between consumer restart attempts after a startup or connection failure. Validated `>= TimeSpan.Zero`. |

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
| `ReceiveBatchSize` | `int` | `1` | No | Messages per `ReceiveMessage` call. |
| `RequeueOnFailure` | `bool` | `true` | No | Returns messages for retry by resetting visibility. |

#### `AzureServiceBusInboxIngressOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Safety` | `TransportInboxIngressSafetyOptions` | `new()` | No | Shared safety limits. |
| `Destination` | `string` | `string.Empty` | No | Queue or topic name. |
| `SubscriptionName` | `string?` | `null` | No | Required when `Destination` names a topic. |
| `PrefetchCount` | `int` | `0` | No | Processor prefetch. |
| `MaxConcurrentCalls` | `int?` | `null` | No | Service Bus processor callback concurrency. |
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
| `PrefetchCount` | `int` | `0` | No | Native prefetch for brokers that support it. |
| `ReceiveBatchSize` | `int` | `1` | No | Messages requested per broker batch receive. |
| `MaxConcurrentCalls` | `int?` | `null` | No | Native callback concurrency limit. |
| `MaxInFlightMessages` | `int` | `32` (`DefaultMaxInFlightMessages`) | No | LiteBus-side handler concurrency gate. |
| `DeclareDestination` | `bool` | `true` | No | Declare the destination before subscribing. |
| `DurableDestination` | `bool` | `true` | No | Declared destination survives broker restarts. |
| `Exclusive` | `bool` | `false` | No | Exclusive subscription on this connection. |
| `ConsumerTag` | `string?` | `null` | No | Client-assigned consumer tag. |
| `DestinationArguments` | `IReadOnlyDictionary<string, object?>?` | `null` | No | Extra declaration arguments passed to the broker. |

Constant: `TransportConsumerOptions.DefaultMaxInFlightMessages = 32`.

#### `TransportPublishRequest` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Destination` | `string` | none | **Yes** (`required`) | Primary address (AMQP exchange, Service Bus entity, Kafka topic, SQS queue URL). |
| `Route` | `string?` | `null` | No | Route within the destination (AMQP routing key, Service Bus subject, Kafka key). |
| `Body` | `ReadOnlyMemory<byte>` | none | **Yes** (`required`) | Message body; LiteBus dispatchers pass UTF-8 payload text. |
| `Headers` | `IReadOnlyDictionary<string, object?>?` | `null` | No | Application headers; LiteBus dispatchers fill these with the `TransportHeaders` names. |
| `ContentType` | `string` | `"application/json"` | No | Content type property. |
| `ContentEncoding` | `string?` | `null` | No | Content encoding property; SQS base64 bodies also use the `litebus-content-encoding` header. |
| `Persistent` | `bool` | `true` | No | Broker persistence flag. |
| `Mandatory` | `bool` | `false` | No | Requires routing to at least one consumer. |
| `MessageId` | `string?` | `null` | No | Broker message id property. |
| `CorrelationId` | `string?` | `null` | No | Broker correlation id property. |

#### `TransportMessage` (sealed class, inbound delivery)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MessagingSystem` | `string?` | `null` | No | OTel `messaging.system` value supplied by the adapter (see `TransportMessagingSystems`). |
| `Body` | `ReadOnlyMemory<byte>` | none | **Yes** (`required`) | Delivery body; copy or deserialize before the handler returns. |
| `Headers` | `IReadOnlyDictionary<string, object?>` | none | **Yes** (`required`) | Application headers. |
| `Destination` | `string?` | `null` | No | Destination the message was published to. |
| `Route` | `string?` | `null` | No | Route within the destination. |
| `MessageId` | `string?` | `null` | No | Broker message id. |
| `CorrelationId` | `string?` | `null` | No | Broker correlation id. |
| `Redelivered` | `bool` | `false` | No | Whether the broker previously attempted delivery. |
| `AckAsync` | `Func<CancellationToken, Task>` | none | **Yes** (`required`) | Positive settlement delegate. |
| `NackAsync` | `Func<bool, CancellationToken, Task>` | none | **Yes** (`required`) | Negative settlement delegate; the `bool` requests requeue. |

Helpers: `AcceptAsync()`, `DiscardAsync()` (nack without requeue), `ReturnToQueueAsync()` (nack with requeue).

#### `TransportConsumerAckHandlers` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `AckAsync` | `Func<CancellationToken, Task>` | none | **Yes** (`required`) | Bundled positive settlement delegate. |
| `NackAsync` | `Func<bool, CancellationToken, Task>` | none | **Yes** (`required`) | Bundled negative settlement delegate. |

#### `TransportCircuitBreakerOptions` (sealed record, `LiteBus.Transport`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `FailureThreshold` | `int` | `5` | No | Consecutive failures needed to open the circuit. `0` disables breaking. |
| `BreakDuration` | `TimeSpan` | `30 seconds` | No | How long broker operations are rejected with `TransportCircuitBreakerOpenException` once open. |

#### `TransportReconnectOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `AutomaticRecoveryEnabled` | `bool` | `true` | No | Whether the client auto-recovers dropped connections. |
| `RecoveryInterval` | `TimeSpan` | `5 seconds` | No | Interval between recovery attempts. |

#### `AmqpConnectionOptions` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Uri` | `Uri?` | `null` | No | When set, replaces the discrete fields. Must be absolute, use `amqp` or `amqps`, and carry a host, or `AmqpTransportModule` throws `ArgumentException`. |
| `HostName` | `string` | `"localhost"` | Validated when `Uri` is null | Broker host; must be non-blank. |
| `Port` | `int` | `5672` | Validated when `Uri` is null | Broker port; validated `1..65535`. |
| `VirtualHost` | `string` | `"/"` | Validated when `Uri` is null | AMQP virtual host; must be non-blank. |
| `UserName` | `string` | `"guest"` | Validated when `Uri` is null | Auth user; must be non-null. |
| `Password` | `string` | `"guest"` | Validated when `Uri` is null | Auth password; must be non-null. |
| `ClientProvidedName` | `string?` | `null` | No | Connection name shown in broker management UIs. |
| `AutomaticRecoveryEnabled` | `bool` | `true` | No | RabbitMQ client auto-recovery. |
| `NetworkRecoveryInterval` | `TimeSpan` | `5 seconds` | No | Validated `> TimeSpan.Zero` when recovery is enabled. |
| `CircuitBreaker` | `AmqpCircuitBreakerOptions` | `new()` | No | Connection and per-exchange breaker settings; must be non-null. |

#### `AmqpCircuitBreakerOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `FailureThreshold` | `int` | `5` | No | Validated `>= 0`. |
| `BreakDuration` | `TimeSpan` | `30 seconds` | No | Validated `> TimeSpan.Zero` when the threshold is positive. |

Internal helper: `ToTransportOptions()` projects onto `TransportCircuitBreakerOptions`.

#### `AmqpConsumerOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `QueueName` | `string` | none | **Yes** (`required`) | Queue consumed. |
| `PrefetchCount` | `ushort` | `1` | No | basic.qos prefetch. |
| `Exclusive` | `bool` | `false` | No | Exclusive consumer. |
| `ConsumerTag` | `string?` | `null` | No | Client-supplied consumer tag. |
| `DeclareQueue` | `bool` | `true` | No | Declare before subscribing. |
| `DurableQueue` | `bool` | `true` | No | Durable declaration. |
| `QueueArguments` | `IReadOnlyDictionary<string, object?>?` | `null` | No | Arguments passed to `queue.declare`. |

#### `KafkaTransportOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `BootstrapServers` | `string` | none | **Yes** (`required`) | Kafka bootstrap list. |
| `ClientId` | `string?` | `null` | No | Client id for producer, consumer and admin clients. |
| `ConsumerGroupId` | `string` | `"litebus-transport"` | No | Consumer group used by `KafkaConsumer`. |
| `MessageTimeoutMs` | `int?` | `null` | No | When set, also sets `SocketTimeoutMs` and forces `MessageSendMaxRetries = 0`. |
| `ConnectivityCheckTimeout` | `TimeSpan` | `5 seconds` | No | Cluster describe timeout for the connectivity probe. Validated `> TimeSpan.Zero`. |
| `SeekFailureBackoffInitial` | `TimeSpan` | `250 ms` | No | First delay before re-consuming an offset that failed ingress. |
| `SeekFailureBackoffMax` | `TimeSpan` | `30 seconds` | No | Cap on the seek backoff. Validated `>= SeekFailureBackoffInitial`. |
| `SeekFailureBackoffMultiplier` | `double` | `2.0` | No | Growth factor per repeated failure at the same offset. Must be finite and `>= 1`. |

The producer config is fixed to `Acks.All`; the consumer config is fixed to `EnableAutoCommit = false` and `AutoOffsetReset.Earliest`.

#### `AwsSqsTransportOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Region` | `string?` | `null` | No | AWS region system name; used only when `ServiceUrl` is not set. |
| `ServiceUrl` | `string?` | `null` | No | Custom endpoint such as LocalStack. Takes precedence over `Region`. |
| `AccessKey` | `string?` | `null` | No | Explicit credentials. Must be supplied together with `SecretKey` or `ArgumentException` is thrown. |
| `SecretKey` | `string?` | `null` | No | See `AccessKey`. |
| `ConnectivityCheckQueueUrl` | `string?` | `null` | No | Queue whose attributes the connectivity probe reads. |
| `LongPollWaitTimeSeconds` | `int` | `20` | No | SQS long-poll wait. |
| `VisibilityTimeoutSeconds` | `int` | `30` | No | Default visibility timeout applied on receive. |
| `RequeueVisibilityTimeoutSeconds` | `int` | `30` | No | Base visibility timeout when a handler requests requeue. |
| `MaxRequeueVisibilityTimeoutSeconds` | `int` | `900` | No | Cap on requeue backoff. Validated `>= RequeueVisibilityTimeoutSeconds`. |
| `RequeueBackoffMultiplier` | `double` | `2.0` | No | Growth per prior receive attempt. Finite and `>= 1`. |
| `PollBackoffInitial` | `TimeSpan` | `500 ms` | No | Delay before polling again after a fully failed batch. |
| `PollBackoffMax` | `TimeSpan` | `30 seconds` | No | Cap on poll backoff. |
| `PollBackoffMultiplier` | `double` | `2.0` | No | Growth per consecutive full-batch failure. Finite and `>= 1`. |

#### `AzureServiceBusTransportOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `ConnectionString` | `string` | none | **Yes** (`required`) | Namespace connection string; must be non-blank. |
| `ClientId` | `string?` | `null` | No | Sets `ServiceBusClientOptions.Identifier`. |
| `ConnectivityCheckTarget` | `AzureServiceBusDiagnosticTarget?` | `null` | No | `AzureServiceBusQueueDiagnosticTarget(QueueName)` or `AzureServiceBusSubscriptionDiagnosticTarget(TopicName, SubscriptionName)`, peeked by the connectivity probe. Names must be non-blank. |
| `ConsumerErrorRetryInterval` | `TimeSpan` | `5 seconds` | No | Delay before restarting the processor after a recoverable error. Validated `> TimeSpan.Zero`. |
| `ConsumerErrorRetryMaxInterval` | `TimeSpan` | `1 minute` | No | Maximum restart delay. Validated `>= ConsumerErrorRetryInterval`. |

#### `InMemoryTransportOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `DestinationCapacity` | `int` | `1024` (`DefaultDestinationCapacity`) | No | Bounded channel capacity per destination. Validated `>= 1` by `InMemoryTransportModule`. |

### 3.10 Diagnostics, health and management options

#### `DiagnosticCheckRunOptions` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `MaxParallelism` | `int` | `4` | No | Concurrent probe limit enforced with a `SemaphoreSlim`. Validated `> 0`. |
| `Timeout` | `TimeSpan` | `5 seconds` | No | Per-probe timeout; a timeout yields `Unhealthy` with description "The diagnostic check timed out." Validated `> 0` ticks. |

#### `LiteBusHealthCheckOptions` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `FailHealthWhenNoProbes` | `bool` | `true` | No | When `true` and the manifest has zero probes, the run reports `Degraded` with a synthetic probe named `litebus.probes`. |
| `DiagnosticChecks` | `DiagnosticCheckRunOptions` | `new()` | No | Timeout and parallelism for the probe run. |

`AddLiteBus(this IHealthChecksBuilder, Action<LiteBusHealthCheckOptions>? configure = null, string name = "litebus")` registers the check with failure status `Unhealthy` and tags `litebus` and `ready`.

#### `LiteBusManagementOptions` (sealed class)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `RoutePrefix` | `string` | `"litebus"` | No | Route prefix; trimmed of slashes. Must be non-blank. |
| `AllowAnonymousManagement` | `bool` | `false` | No | When `false` every management route calls `RequireAuthorization`. |
| `AuthorizationPolicy` | `string?` | `null` | No | Named policy applied instead of the default authorization requirement. |
| `FailHealthWhenNoProbes` | `bool` | `true` | No | Applies to `GET {prefix}/health`. |
| `DiagnosticChecks` | `DiagnosticCheckRunOptions` | `new()` | No | Timeout and parallelism for `GET {prefix}/health`. Must be non-null with positive values. |
| `DefaultDrainTimeout` | `TimeSpan` | `30 seconds` | No | Drain timeout when the caller supplies none. Must be positive and `<= MaxDrainTimeout`. |
| `MaxPageSize` | `int` | `100` | No | Upper bound applied to `PageSize` on query endpoints. Must be positive. |
| `MaxBulkMessageIds` | `int` | `1000` | No | Maximum ids accepted by a bulk requeue request. Must be positive. |
| `MaxDrainTimeout` | `TimeSpan` | `5 minutes` | No | Maximum accepted drain timeout. Must be positive. |

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
| `RecordedVersion` | `int` | positional | **Yes** | Version recorded in the metadata table. |
| `SchemaName` | `string?` | `null` | No | Database schema, when applicable. |
| `TableName` | `string?` | `null` | No | Table name, when applicable. |

Static: `StoreSchemaInfo.ForLogicalStore(component, version)` for stores with no persisted metadata.

#### `RetentionRunStatus` (sealed record, positional)

| Property Name | Data Type | Description & Impact |
| --- | --- | --- |
| `Enabled` | `bool` | Whether the cleanup loop is configured to run. |
| `Retention` | `TimeSpan?` | Configured retention window; `null` disables cleanup. |
| `Interval` | `TimeSpan` | Configured cleanup interval. |
| `LastRunAt` | `DateTimeOffset?` | Timestamp of the last pass. |
| `LastDeletedCount` | `int` | Rows deleted by the last pass. |
| `LastError` | `string?` | Error message from the last failed pass. |

### 3.11 Auditing configuration

#### `AuditedAttribute` (sealed, `AttributeTargets.Class | AttributeTargets.Struct`, `Inherited = false`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Action` | `string` | constructor argument | **Yes** | Use-case identity written to the record, for example `orders.place-order`. |
| `Category` | `string?` | `null` | No | Grouping for review and retention. |
| `TargetKind` | `string?` | `null` | No | Kind of resource acted on. |
| `ReasonRequired` | `bool` | `false` | No | When `true` and the outcome is `Succeeded` with no reason recorded, `AuditRecordWriter` throws `LiteBusConfigurationException`. |

#### `AuditExemptAttribute` (sealed)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Rationale` | `string` | constructor argument | **Yes** | Recorded reason the message is not audited. Analyzer `LB1018` reports messages that declare neither position. |

Both attributes implement `IMessageDeclarationSource` with `DeclarationType => typeof(AuditDeclaration)`.

#### `AuditedDeclaration` (sealed record : `AuditDeclaration`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Action` | `string` | constructor | **Yes** | Non-blank; `ArgumentException` otherwise. |
| `Category` | `string?` | `null` | No | Grouping. |
| `TargetKind` | `string?` | `null` | No | Resource kind. |
| `ReasonRequired` | `bool` | `false` | No | Enforced at write time. |
| `IsAudited` | `bool` (override) | `true` | - | Discriminates from `AuditExemptDeclaration`. |

#### `AuditExemptDeclaration` (sealed record : `AuditDeclaration`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Rationale` | `string` | constructor | **Yes** | Non-blank. |
| `IsAudited` | `bool` (override) | `false` | - | - |

Factories: `AuditDeclaration.Audited(string action)` and `AuditDeclaration.Exempt(string rationale)`. The base constructor is `internal`, so the hierarchy is closed to these two positions.

#### `AuditRecord` (sealed record)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Action` | `string` | none | **Yes** (`required`) | From the declaration. |
| `Outcome` | `AuditOutcome` | none | **Yes** (`required`) | From `IAuditOutcomeMapper.Map`. |
| `OccurredAt` | `DateTimeOffset` | none | **Yes** (`required`) | `TimeProvider.GetUtcNow()`. |
| `Duration` | `TimeSpan` | `TimeSpan.Zero` | No | Mediation duration from the completion context. |
| `Category` | `string?` | `null` | No | From the declaration. |
| `TargetKind` | `string?` | `null` | No | From the declaration. |
| `TargetId` | `string?` | `null` | No | From `IAuditScope.WithTarget`. |
| `Reason` | `string?` | `null` | No | `IAuditScope.Reason` when set, otherwise the pipeline decision reason. |
| `FailureCode` | `string?` | `null` | No | From `IAuditOutcomeMapper.MapFailureCode`; defaults to the exception type name and is `null` for guard denials. |
| `MessageType` | `string?` | `null` | No | `messageType.FullName`. |
| `CorrelationId` | `string?` | `null` | No | Read from execution-context item `__LiteBus.Trace.CorrelationId`. |
| `TenantId` | `string?` | `null` | No | Read from execution-context item `__LiteBus.Trace.TenantId`. |
| `Properties` | `IReadOnlyDictionary<string, string>` | empty, ordinal comparer | No | Copied from `IAuditScope.WithProperty` calls. |

### 3.12 Contracts, metadata and payload protection

#### `MessageContractAttribute` (sealed, `Class | Struct`, `Inherited = false`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `Name` | `string` | constructor | **Yes** | Stable contract name persisted with the payload. |
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
| `MessageId` | `Guid` | none | **Yes** (`required`) | Bound as authenticated metadata. |
| `ContractName` | `string` | none | **Yes** (`required`) | Bound as authenticated metadata. |
| `ContractVersion` | `int` | none | **Yes** (`required`) | Bound as authenticated metadata. |
| `TenantId` | `string?` | `null` | No | Bound when present. |
| `Axis` | `string` | none | **Yes** (`required`) | Literal `inbox` or `outbox` as set by the LiteBus writers and dispatchers. |

#### Handler attributes

| Attribute | Target | Members | Notes |
| --- | --- | --- | --- |
| `HandlerPriorityAttribute` | `Class` | `int Priority` (constructor) | Lower runs first. Absent means `HandlerPriorities.Default` (`0`). |
| `HandlerTagAttribute` | `Class`, `AllowMultiple = true` | `string Tag` | Repeatable per handler. |
| `HandlerTagsAttribute` | `Class` | `string[] Tags` (`params`) | Bulk form; combined with any `HandlerTagAttribute` values. |

### 3.13 Composition builders (the configuration surface)

#### `MessageModuleBuilder`

| Member | Signature | Description & Impact |
| --- | --- | --- |
| `Contracts` | `IContractWriter { get; }` | Live contract registry writer. |
| `UseTimeProvider` | `(TimeProvider) -> MessageModuleBuilder` | Registers the `TimeProvider` used by auditing, envelope factories, processors and stores. |
| `UseAuditTrail<TAuditTrail>` | `(InstanceLifetime lifetime = Scoped) -> MessageModuleBuilder`, `TAuditTrail : class, IAuditTrail` | Container-constructed trail. The lifetime is an explicit parameter, not a consequence of the overload reached for. |
| `UseAuditTrailInstance` | `(IAuditTrail) -> MessageModuleBuilder` | Pre-created trail, necessarily a **Singleton**. The name carries the lifetime, because a trail built here with a scoped session captures it for the life of the process. |
| `UseAuditOutcomeMapper` | `(IAuditOutcomeMapper) -> MessageModuleBuilder` | Overrides `DefaultAuditOutcomeMapper`. |
| `UseAuditOutcomeMapper<T>` | `() -> MessageModuleBuilder`, `T : IAuditOutcomeMapper, new()` | Constructs and registers the mapper. |
| `Register<T>()` / `Register(Type)` | `-> MessageModuleBuilder` | Registers any message or handler type with no axis validation. |
| `RegisterFromAssembly` | `(Assembly) -> MessageModuleBuilder` | Registers **every** type in the assembly with the message registry and calls `Contracts.AddFromAssembly`. |

#### `CommandModuleBuilder` / `QueryModuleBuilder`

| Member | Description & Impact |
| --- | --- |
| `Contracts` | `IContractWriter` for durable command/query contracts. |
| `EnableAuditing()` | Registers `CommandAuditCompletionHandler` / `QueryAuditCompletionHandler` (priority `HandlerPriorities.Observability`) and the `litebus.audit.trail` diagnostic probe. |
| `Register<T>()` / `Register(Type)` | Validates that the type is a command/query construct, otherwise throws `LiteBusNotSupportedException`. |
| `RegisterFromAssembly(Assembly)` | Registers every concrete, non-abstract command/query construct found. |

A **command construct** is a type assignable to `ICommand`, or implementing any of `ICommandHandler<>`, `ICommandHandler<,>`, `ICommandPreHandler`, `ICommandPreHandler<>`, `ICommandGuard<>`, `ICommandValidator<>`, `ICommandShortcut<>`, `ICommandShortcut<,>`, `ICommandRefusalMapper<,>`, `ICommandPostHandler`, `ICommandPostHandler<>`, `ICommandPostHandler<,>`, `ICommandErrorHandler`, `ICommandErrorHandler<>`, `ICommandErrorHandler<,>`, `ICommandCompletionHandler`, `ICommandCompletionHandler<>`, `ICommandCompletionHandler<,>`, or an `IMessageDefinition<TMessage, TValue>` whose `TMessage` is an `ICommand`.

A **query construct** mirrors that list with `IQueryHandler<,>`, `IQueryPreHandler`, `IQueryPreHandler<>`, `IQueryGuard<>`, `IQueryValidator<>`, `IQueryShortcut<,>`, `IQueryRefusalMapper<,>`, `IStreamQueryShortcut<,>`, `IStreamQueryRefusalMapper<,>`, `IQueryPostHandler`, `IQueryPostHandler<>`, `IQueryPostHandler<,>`, `IQueryErrorHandler`, `IQueryErrorHandler<>`, `IQueryErrorHandler<,>`, `IStreamQueryHandler<,>`, `IStreamQueryPostHandler<,>`, `IQueryCompletionHandler`, `IQueryCompletionHandler<>`, `IQueryCompletionHandler<,>`, plus `IMessageDefinition<,>` over an `IQuery`.

#### `EventModuleBuilder`

`Contracts`, `Register<T>()`, `Register(Type)`, `RegisterFromAssembly(Assembly)`. **Event constructs**: `IEvent` plus `IEventHandler<>`, `IEventPreHandler`, `IEventPreHandler<>`, `IEventGuard<>`, `IEventValidator<>`, `IEventShortcut<>`, `IEventPostHandler`, `IEventPostHandler<>`, `IEventErrorHandler`, `IEventErrorHandler<>`, `IEventCompletionHandler`, `IEventCompletionHandler<>`, and `IMessageDefinition<,>` over an `IEvent`. **There is no `EnableAuditing()` on the event axis.**

#### `InboxModuleBuilder`

| Member | Type / Signature | Default | Description & Impact |
| --- | --- | --- | --- |
| `Contracts` | `IContractWriter` | deferred `MessageContractBuilder` | Registrations are replayed onto the shared registry during `Build`. |
| `ProcessorOptions` | `InboxProcessorOptions { get; private set; }` | `new()` | Replaced by `UseProcessorOptions`. |
| `ProcessorHostOptions` | `InboxProcessorHostOptions { get; }` | `new()` | Mutated by the `EnableInboxProcessor` callback. |
| `CleanupHostOptions` | `InboxCleanupHostOptions { get; }` | `new()` | Mutated by the `EnableCleanup` callback. |
| `IsInboxProcessorEnabled` | `bool` | `false` | Read by `InboxModule.Build` validation. |
| `IsCleanupEnabled` | `bool` | `false` | Read by `InboxModule.Build`. |
| `IsStorageConfigured` | `bool` | `false` | `Build` throws `LiteBusConfigurationException` when still false. |
| `IsDispatcherConfigured` | `bool` | `false` | `Build` throws when the processor is enabled and this is false. |
| `IsPayloadEncryptionConfigured` | `bool` | `false` | Whether `UsePayloadEncryption` was called. |
| `EnableInboxProcessor(Action<InboxProcessorHostOptions>? configure = null)` | fluent | - | Registers `InboxProcessorBackgroundService` and processor control services. Requires a dispatcher. |
| `EnableCleanup(Action<InboxCleanupHostOptions>? configure = null)` | fluent | - | Registers `InboxCleanupBackgroundService`. |
| `UseProcessorOptions(InboxProcessorOptions)` | fluent | - | Replaces batch, lease and retry settings. |
| `RegisterStorage(IInboxStorageModule)` | fluent | - | Exactly one; a second call throws `LiteBusConfigurationException`. |
| `RegisterDispatcher(IInboxDispatcherModule)` | fluent | - | Exactly one; a second call throws. |
| `RegisterIngress(IInboxIngressModule)` | fluent | - | Exactly one; a second call throws. |
| `RegisterSaga(IModule)` | fluent | - | Adds a feature-bridge child module; used by `EnableSaga`. |
| `UsePayloadEncryption(IPayloadEncryptor)` | fluent | - | Registers `IInboxPayloadProtector`. |
| `AddDiagnosticCheck<TCheck>(string name)` | fluent, `TCheck : IDiagnosticCheck` | - | Adds a consumer-owned probe (Singleton) to the host manifest. |
| `CollectSubModules()` | `IReadOnlyList<IModule>` | - | Order: storage, dispatcher, ingress, feature bridges. |
| `ApplyContracts(IMessageContractRegistry)` | `void` | - | Replays deferred contract registrations. |

#### `OutboxModuleBuilder`

Same shape minus ingress and saga, with `EnableOutboxProcessor` and `IsOutboxProcessorEnabled`, plus one extra behaviour: `ProcessorOptions` returns the stored options `with { HookFailurePolicy = dispatcherModule.DefaultHookFailurePolicy }` when `UseProcessorOptions` was **not** called and a dispatcher is registered. Transport dispatch modules report `CompleteDespiteHookFailure`; the in-process dispatcher inherits the `DeadLetter` default.

#### Storage builders

`InMemoryInboxStorageModuleBuilder` / `InMemoryOutboxStorageModuleBuilder`

| Member | Default | Description & Impact |
| --- | --- | --- |
| `Options` | `new()` | `UseOptions(InMemoryXStoreOptions)`. |
| `TimeProvider` | `null` | `UseTimeProvider(TimeProvider)`; when set it is also registered as the container's `TimeProvider`. |

`PostgreSqlInboxModuleBuilder` / `PostgreSqlOutboxModuleBuilder`

| Member | Default | Description & Impact |
| --- | --- | --- |
| `DataSource` | `null` | Set by `UseDataSource(NpgsqlDataSource)` (not owned) or `UseConnectionString(string)` (owned and registered so the container disposes it). **Required** - `Build` throws when null. |
| `Options` | `new()` | `UseOptions(...)`. |
| `EnableSchemaInitialization` | `true` | `DisableSchemaInitialization()` removes the startup task. |
| `EnsureSchemaCreationOnStartup()` | - | Sets `Options.EnsureSchemaCreationOnStartup = true`. |
| `EnableAmbientTransactionProviderRegistration` | `false` | Set by `EnableAmbientTransactionProvider(TransactionalWriteMode mode = RequireActiveTransaction)`; registers scoped `ITransactionalInbox` / `ITransactionalOutbox` over `IPostgreSqlTransactionProvider`. |
| `TransactionalWriteMode` | `RequireActiveTransaction` | See the enum catalog. |

`EfCoreInboxStorageModuleBuilder` / `EfCoreOutboxStorageModuleBuilder`

| Member | Default | Description & Impact |
| --- | --- | --- |
| `DbContextType` | `null` | Set by `UseDbContext<TContext>()` where `TContext : DbContext, IInboxDbContext` / `IOutboxDbContext`. **Required** - `Build` throws `LiteBusConfigurationException` when null. |
| `Options` | `new()` | `UseOptions(...)`. |
| `RegisterSaveChangesInterceptor` | `false` | `EnableSaveChangesInterceptor()` registers `LiteBusInboxSaveChangesInterceptor` / `LiteBusOutboxSaveChangesInterceptor` (Singleton) and the scoped `ITransactionalInbox<TContext>` / `ITransactionalOutbox<TContext>`. |
| `RequireTransactionalSetup` | `false` | `EnforceTransactionalSetup()` makes `Build` throw when the interceptor was not enabled. |

#### Ingress builders

`AmqpInboxIngressModuleBuilder`, `KafkaInboxIngressModuleBuilder`, `AwsSqsInboxIngressModuleBuilder`, `AzureServiceBusInboxIngressModuleBuilder` and `InMemoryInboxIngressModuleBuilder` all expose:

| Member | Default | Description & Impact |
| --- | --- | --- |
| `Options` | `new()` for AMQP and in-memory; `null!` for Kafka, SQS and Service Bus | `UseOptions(...)`. Effectively required on the three that start null. |
| `HostOptions` | `new TransportInboxIngressHostOptions()` | Loop lifecycle and restart delay. |
| `EnableIngressConsumer` | `true` | Cleared by `DisableIngressConsumer()`, which skips registering the background consumer. |
| `ConfigureHost(Action<TransportInboxIngressHostOptions>)` | - | **Kafka builder only.** |

#### `SagaModuleBuilder`

| Member | Description & Impact |
| --- | --- |
| `IsStorageConfigured` | Whether a store was chosen. |
| `DefineState<TState>(string sagaDefinitionId)` and `RegisterState<TState>(string)` | Register the saga state CLR type for a definition id (`TState : class, new()`). |
| `MapContract(string contractName, string sagaDefinitionId)` | Routes one durable contract to a saga definition. |
| `MapState<TState>(string contractName)` | Shorthand that uses the contract name as the definition id. |
| `UseInMemoryStorage()` | Selects `InMemorySagaStorageModule`. |
| `RegisterStorage(ISagaStorageModule)` | Exactly one; a second call throws `LiteBusConfigurationException`. Registering none throws at collect time. |

`PostgreSqlSagaModuleBuilder`: `UseDataSource(NpgsqlDataSource)`, `UseConnectionString(string)`, `UseOptions(PostgreSqlSagaStoreOptions)`, `DisableSchemaInitialization()`.

#### `DependencyDescriptor` (sealed class, `IEquatable<DependencyDescriptor>`)

| Property Name | Data Type | Default Value | Required? | Description & Impact |
| --- | --- | --- | --- | --- |
| `DependencyType` | `Type` | constructor | **Yes** | Service type. |
| `ImplementationType` | `Type?` | `null` | No | Set by the type-registration constructors. Must be a concrete class assignable to, or an open generic closing, the service type. |
| `Instance` | `object?` | `null` | No | Set by the instance constructor; forces `Singleton`. Must be assignable to the service type. |
| `Factory` | `Func<IServiceProvider, object>?` | `null` | No | Set by the factory constructors. |
| `Lifetime` | `InstanceLifetime` | `Transient` for type and factory forms, `Singleton` for the instance form | No | Validated with `Enum.IsDefined`. |
| `IsCollectionRegistration` | `bool` | `false` | No | `true` only via the `ForCollection` factories; such descriptors must go through `IDependencyRegistry.RegisterCollection`, and non-collection descriptors must not. |

Statics: `ForCollection(Type, Type, InstanceLifetime = Transient)`, `ForCollection(Type, object)`, `ForCollection(Type, Func<IServiceProvider, object>, InstanceLifetime = Transient)`. Equality compares service type, lifetime and the collection flag, then implementation type, or instance/factory by reference.

#### Saga configuration and data types

| Type | Members | Notes |
| --- | --- | --- |
| `SagaCorrelation` (sealed record) | `CorrelationId` (`string`, required), `SagaDefinitionId` (`string`, required), `TenantId` (`string?`, default `null`) | Storage primary key. |
| `SagaInstance<TState>` (sealed class) | `Correlation` (required), `State` (`TState`, required), `Version` (`int`, required), `IsCompleted` (`bool`, required), `LastAppliedMessageId` (`Guid?`, default `null`) | Loaded instance plus its optimistic token. |
| `SagaSaveItem<TState>` (sealed record) | positional `(SagaCorrelation Correlation, TState State, int ExpectedVersion)` + `AppliedMessageId` (`Guid?`) | `From(correlation, state, expectedVersion, appliedMessageId = null)`. |
| `SagaCompleteItem` (sealed record) | positional `(SagaCorrelation Correlation, int ExpectedVersion)` + `AppliedMessageId` (`Guid?`) | `From(correlation, expectedVersion, appliedMessageId = null)`. |
| `SagaInstanceSummary` (sealed record) | `Correlation` (required), `Version` (`int`, required), `IsCompleted` (`bool`, required), `CreatedAt` (`DateTimeOffset?`), `UpdatedAt` (`DateTimeOffset?`) | Returned by `ISagaStore.QueryAsync`. |
| `SagaQueryFilter` (sealed record) | `SagaDefinitionId` (`string?`), `CorrelationId` (`string?`), `TenantId` (`string?`), `IsCompleted` (`bool?`), `Take` (`int`, default `100`) | All filters default to `null`. |
| `SagaPurgeFilter` (sealed record) | `SagaDefinitionId`, `CorrelationId`, `TenantId`, `IsCompleted` (`bool?`), `CompletedBefore` (`DateTimeOffset?`) | All default to `null`. |

---

## 4. Feature-by-Feature Deep Dive

### 4.1 Commands

**Primary types:** `ICommand`, `ICommand<TCommandResult>`, `ICommandMediator`, `CommandMediator`, `CommandMediatorExtensions`, `CommandModule`, `CommandModuleBuilder`.

**Interface:**

```csharp
public interface ICommandMediator
{
    Task SendAsync(ICommand command,
                   CommandMediationSettings? commandMediationSettings = null,
                   CancellationToken cancellationToken = default);

    Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                   CommandMediationSettings? commandMediationSettings = null,
                                                   CancellationToken cancellationToken = default);
}
```

**Extension overloads** (`CommandMediatorExtensions`):

| Signature | Return | Behaviour |
| --- | --- | --- |
| `SendAsync(ICommand, CancellationToken)` | `Task` | Passes `null` settings. |
| `SendAsync<TCommandResult>(ICommand<TCommandResult>, CancellationToken)` | `Task<TCommandResult>` | Passes `null` settings. |
| `SendAsync(ICommand, string tag, CancellationToken)` | `Task` | Builds `CommandMediationSettings { Routing = new() { Tags = [tag] } }`. |
| `SendAsync<TCommandResult>(ICommand<TCommandResult>, string tag, CancellationToken)` | `Task<TCommandResult>` | Same, typed. |

**Handler contracts:**

| Contract | Method | Notes |
| --- | --- | --- |
| `ICommandHandler<in TCommand>` | `Task HandleAsync(TCommand, CancellationToken = default)` | `TCommand : ICommand`. Extends `IAsyncMessageHandler<TCommand>`. Exactly one per command. |
| `ICommandHandler<in TCommand, TCommandResult>` | `Task<TCommandResult> HandleAsync(TCommand, CancellationToken = default)` | `TCommand : ICommand<TCommandResult>`. |
| `ICommandGuard<in TCommand>` | `Task<Verdict> DecideAsync(TCommand, CancellationToken = default)` | Stage 1. |
| `ICommandValidator<in TCommand>` | `Task<Validity> ValidateAsync(TCommand, CancellationToken = default)` | Stage 2, aggregating. |
| `ICommandShortcut<in TCommand>` | `Task<Shortcut> TryAnswerAsync(TCommand, CancellationToken = default)` | Stage 3, void commands. |
| `ICommandShortcut<in TCommand, TCommandResult>` | `Task<Shortcut<TCommandResult>> TryAnswerAsync(...)` | Stage 3, result commands. |
| `ICommandPreHandler` / `ICommandPreHandler<in TCommand>` | `Task PreHandleAsync(TCommand, CancellationToken = default)` | Stage 4. The non-generic form targets `ICommand`, so it runs for every command. |
| `ICommandPostHandler` / `ICommandPostHandler<in TCommand>` / `ICommandPostHandler<in TCommand, in TCommandResult>` | `Task PostHandleAsync(TCommand, TCommandResult?, CancellationToken = default)` | Runs after the main handler. The one- and zero-arg forms bind `TCommandResult` to `object`. |
| `ICommandErrorHandler` / `ICommandErrorHandler<TCommand>` / `ICommandErrorHandler<TCommand, TCommandResult>` | `Task HandleErrorAsync(MessageErrorContext<TCommand, TCommandResult>, CancellationToken = default)` | Runs on a recoverable fault. |
| `ICommandCompletionHandler` / `ICommandCompletionHandler<TCommand>` / `ICommandCompletionHandler<TCommand, TCommandResult>` | `Task HandleCompletionAsync(MessageCompletionContext<...>, CancellationToken)` | Runs on every path. |
| `ICommandRefusalMapper<in TCommand, out TCommandResult>` | `TCommandResult Map(TCommand, Refusal)` | Turns a denial or validation failure into a return value. |

**Standard usage:**

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

**Advanced usage - tags, items and a handler predicate:**

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

**Exceptions and edge cases:**

| Condition | Result |
| --- | --- |
| No main handler after filtering | `NoHandlerFoundException` (not routed to error handlers) |
| More than one main handler | `MultipleHandlerFoundException` with `HandlerTypes` populated |
| Guard denies and no refusal mapper produces the result type | `LiteBusMessageDeniedException` (`MessageType`, `Reason`, `Code`) |
| Validator reports failures and no refusal mapper applies | `LiteBusMessageInvalidException` (`MessageType`, `Failures`) |
| Untyped shortcut answers a command that produces a result | `LiteBusConfigurationException` telling you to implement `IMessageShortcut<TMessage, TMessageResult>` (analyzer `LB1019` catches this at compile time) |
| Shortcut answers with a result of the wrong type | `LiteBusConfigurationException` naming both types |
| Two refusal mappers at the same level produce the same result type | `LiteBusConfigurationException` |
| `null` command argument | `ArgumentNullException` |
| Registering a non-command type through `CommandModuleBuilder.Register` | `LiteBusNotSupportedException` |
| Descriptor resolution finds two equally specific base types | `AmbiguousMessageResolveException` |

Direct handler registrations preferred over indirect ones: `SingleMainHandlerResolver` first looks at `MessageDependencies.MainHandlers` (handlers registered for the exact message type) and only falls back to `IndirectMainHandlers` (base-type or interface registrations) when the direct list is empty.

### 4.2 Queries and stream queries

**Primary types:** `IQuery`, `IQuery<TQueryResult>`, `IStreamQuery<out TQueryResult>`, `IQueryMediator`, `QueryMediator`, `QueryMediatorExtensions`.

```csharp
public interface IQueryMediator
{
    Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                QueryMediationSettings? queryMediationSettings = null,
                                                CancellationToken cancellationToken = default);

    IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                             QueryMediationSettings? queryMediationSettings = null,
                                                             CancellationToken cancellationToken = default);
}
```

Extensions: `QueryAsync(query, ct)`, `QueryAsync(query, tag, ct)`, `StreamAsync(query, ct)`, `StreamAsync(query, tag, ct)`.

**Handler contracts:** `IQueryHandler<in TQuery, TQueryResult>` (`Task<TQueryResult> HandleAsync`), `IStreamQueryHandler<in TQuery, out TQueryResult>` (`IAsyncEnumerable<TQueryResult> StreamAsync`), plus `IQueryGuard<>`, `IQueryValidator<>`, `IQueryShortcut<,>`, `IStreamQueryShortcut<,>`, `IQueryRefusalMapper<,>`, `IStreamQueryRefusalMapper<,>`, `IQueryPreHandler`(`<>`), `IQueryPostHandler`(`<>`, `<,>`), `IStreamQueryPostHandler<,>`, `IQueryErrorHandler`(`<>`, `<,>`), `IQueryCompletionHandler`(`<>`, `<,>`).

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

**Streaming edge cases.** The returned enumerable owns the per-message dispatch scope. Enumerating it twice throws `InvalidOperationException` ("A mediated asynchronous stream can be enumerated only once because it owns one dispatch scope."). The scope is disposed when enumeration completes, faults, or the enumerator is disposed. `IStreamQueryPostHandler<TQuery, TQueryResult>` receives the whole `IAsyncEnumerable<TQueryResult>` rather than materialized items. Analyzer `LB1003` warns when a query handler injects a mediator or durable writer, because query handlers are expected to be side-effect free.

### 4.3 Events

**Primary types:** `IEvent`, `IEventMediator`, `EventMediator`, `EventMediatorExtensions`, `AsyncBroadcastMediationStrategy<TMessage>`.

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

Extensions: `PublishAsync(@event, ct)`, `PublishAsync(@event, tag, ct)`, `PublishAsync<TEvent>(@event, tag, ct)`.

**Broadcast semantics.** `AsyncBroadcastMediationStrategy<TMessage>`:

1. Runs the pre-stages once for the event. A refusal throws (`LiteBusMessageDeniedException` / `LiteBusMessageInvalidException`); an `Answered` shortcut returns without running handlers.
2. Concatenates direct + indirect main handlers, orders them by `Priority` then `RegistrationSequence`, and groups by `Priority`.
3. Runs the priority groups per `PriorityGroupsConcurrencyMode`, and handlers inside a group per `HandlersWithinSamePriorityConcurrencyMode`.
4. Parallel awaits obey `ParallelFaultMode`.
5. Runs post-handlers with the *task* of the handler batch as the "result" argument, then error handlers on a recoverable fault, then completion handlers with `messageResult: null`.
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
        ParallelFaultMode = ParallelFaultMode.AggregateAll
    }
}, cancellationToken);
```

**Edge cases.** With `ThrowIfNoHandlerFound = false` (the default) an event with no handlers completes silently. `AutoRegisterUnregisteredMessageTypes = true` lets an event type that was never registered be added to the registry during the publish; without it, the mediator throws `NoHandlerFoundException` for an unknown type. `IEventHandler<in TEvent>` only constrains `TEvent : notnull`, so plain POCO events work; `IEventGuard<>`, `IEventValidator<>` and `IEventShortcut<>` constrain `TEvent : IEvent`.

### 4.4 Pipeline stage semantics

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

`Verdict` is a readonly struct: `Verdict.Allow` (the `default`), `Verdict.Deny(string reason, string? code = null)` (reason must be non-blank or `ArgumentException`), `IsDenied`, `Reason`, `Code`, value equality and `==`/`!=`. A denial stops at the first guard, reports `MediationOutcome.Denied`, does **not** reach error handlers, and reaches completion handlers.

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

`Validity` is a readonly struct: `Validity.Valid` (the `default`), `Validity.Invalid(string message, string? member = null, string? code = null)`, `Validity.Invalid(IEnumerable<ValidationFailure>)`, `Validity.Invalid(params ValidationFailure[])` - the collection overloads return `Valid` for an empty input. Members: `IsInvalid`, `Failures` (never null). `ValidationFailure` is a readonly struct `(string Message, string? Member = null, string? Code = null)`; `Message` must be non-blank; `ToString()` renders `member: message` or just `message`.

**Aggregation.** The validator stage is the only stage with `StageAggregation.CollectFailures`: every validator runs (indirect first, then direct) and all reported failures are merged into one `PipelineDecision.Invalid`. The decision's `Reason` is the failures joined with `"; "`; `Code` is the single failure's code when exactly one failure was reported, otherwise `null`.

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

`Shortcut`: `None` (the `default`), `Answer(string? reason = null)`, `IsAnswered`, `Reason`. `Shortcut<TMessageResult>`: `None`, `Answer(TMessageResult result, string? reason = null)`, `IsAnswered`, `Result`, `Reason`. Both are readonly structs with value equality. The first answering shortcut stops the stage, reports `MediationOutcome.Answered`, and (for the untyped contract on a result-producing message) raises `LiteBusConfigurationException` because the untyped answer cannot carry a value.

#### Pre-handlers - `IMessagePreHandler<in TMessage>` (`PreStage.PreHandler`)

Cannot stop the pipeline by returning; throwing is a fault. Use for enrichment and cross-cutting preparation.

#### Post-handlers - `IMessagePostHandler<in TMessage, in TMessageResult>`

`Task PostHandleAsync(TMessage message, TMessageResult? messageResult, CancellationToken cancellationToken = default)`. Direct post-handlers run before indirect ones. `IExecutionContext.SuppressPostHandlers()` stops any post-handler that has not run yet without changing the outcome (the mediation still reports `Succeeded`). A post-handler may overwrite `IExecutionContext.MessageResult` to change what the caller receives.

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

`MessageErrorContext` members: `Message` (required), `Exception` (required), `MessageResult`, `Outcome` (`MessageErrorOutcome`, default `Unhandled`), `HandledResult`, and `AsTyped<TMessage, TMessageResult>()`. Indirect error handlers run first, then direct ones; all of them see the same mutable context. If `Outcome` is still `Unhandled` after every handler, the captured exception is rethrown with its original stack trace via `ExceptionDispatchInfo`. When there are **no** error handlers at all, the exception is rethrown immediately.

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

`MessageCompletionContext` members: `Message` (required), `Outcome` (`MediationOutcome`, required), `MessageResult`, `Exception`, `Reason`, `Duration`, `Faulted` (`Outcome is Failed or Canceled`), `AsTyped<TMessage>()`, `AsTyped<TMessage, TMessageResult>()`. The typed views add `HasResult` (result type match) and a typed `MessageResult`, plus `AsUntyped()`.

Completion handlers are always invoked with `CancellationToken.None`. A completion handler that throws while `context.Exception` is already set has its exception appended to the list stored at `exception.Data[MediationExceptionData.SuppressedCompletionFaults]` (key string `"LiteBus.SuppressedCompletionFaults"`); the original fault still propagates.

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

`Refusal` is a readonly struct: `Refusal.Denied(reason, code = null)`, `Refusal.Invalid(reason, code = null)`, `Outcome` (`Denied` or `Invalid`), `Reason`, `Code`, `IsDenied`, `ToString()`. Selection picks the single mapper whose `MessageResultType` is assignable to the caller's expected result type, preferring direct registrations over indirect ones. Two candidates at the same level throw `LiteBusConfigurationException`. With no mapper, the pipeline throws `LiteBusMessageDeniedException` or `LiteBusMessageInvalidException`. For a void command a refusal always throws (there is no value to map).

### 4.5 Handler discovery, tags, priorities and open generics

**Discovery.** `IMessageRegistry.Register(Type)` is the single entry point. For a given type the registry:

1. Detects an `IMessageDefinition` implementation and binds its declarations (see 4.7).
2. Otherwise runs six descriptor builders - `HandlerDescriptorBuilder` (main), `CompletionHandlerDescriptorBuilder`, `ErrorHandlerDescriptorBuilder`, `PostHandlerDescriptorBuilder`, `PreStageHandlerDescriptorBuilder`, `RefusalMapperDescriptorBuilder` - and collects the descriptors they produce.
3. If no descriptors were produced, the type is treated as a message type (unless its namespace is `System` or starts with `System.`, in which case it is ignored). A type that carries a pipeline marker interface but exposes no contract naming a message type throws `LiteBusConfigurationException`.
4. If the type is an open generic type definition whose descriptors have a generic-parameter message type, it is stored as an open generic handler and closed over every known and future concrete message type. An open generic handler with anything other than exactly one type parameter throws `UnsupportedOpenGenericHandlerException` (analyzer `LB1005`).
5. Handler descriptors get a monotonically increasing `RegistrationSequence`, then handlers and messages are cross-linked and committed.

Message types are normalized: a generic type that still contains generic parameters is reduced to its generic type definition; closed generic messages keep their exact type.

**Descriptor metadata** (`IHandlerDescriptor`): `MessageType`, `Priority`, `RegistrationSequence`, `Tags`, `HandlerType`, `ContractType`. Role-specific descriptors add `MessageResultType` (`IMainHandlerDescriptor`, `IPostHandlerDescriptor`, `IRefusalMapperDescriptor`, `ICompletionHandlerDescriptor` - nullable there), `Stage` (`IPreStageHandlerDescriptor`) and a `PipelineDispatch? Dispatch` (marked `EditorBrowsableState.Never`).

**Tags:**

```csharp
[HandlerTag("legacy")]
[HandlerTag("migration")]
public sealed class LegacyPlaceOrderHandler : ICommandHandler<PlaceOrderCommand> { ... }

[HandlerTags("batch", "nightly")]
public sealed class BatchPlaceOrderHandler : ICommandHandler<PlaceOrderCommand> { ... }

await commandMediator.SendAsync(command, "batch", cancellationToken);   // selects the batch handler
```

A descriptor participates when `descriptor.Tags.Count == 0` **or** `descriptor.Tags` intersects the mediation tags. Analyzer `LB1011` warns about a tag no mediation call in the compilation references.

**Priorities.** Handlers run in **ascending** priority order: a lower number runs earlier, a higher number runs later, and ties break on `RegistrationSequence`. `HandlerPriorities` reserves a window for framework handlers and names the application band on each side of it:

| Constant | Value | Meaning |
| --- | --- | --- |
| `HandlerPriorities.Default` | `0` | Assigned when no `[HandlerPriority]` is present. |
| `HandlerPriorities.ReservedFloor` | `1000000` | Lowest value reserved for LiteBus-shipped handlers. |
| `HandlerPriorities.Persistence` | `1000100` | LiteBus handlers that persist state. |
| `HandlerPriorities.Observability` | `1000200` | LiteBus handlers that observe and record, such as the audit writers. |
| `HandlerPriorities.ReservedCeiling` | `2000000` | First value above the reserved window. |
| `HandlerPriorities.UnitOfWork` | `2000000` | Where an application commits its unit of work, after every LiteBus handler. |

Application handlers belong below `ReservedFloor` or at/above `ReservedCeiling`. Only `Persistence` and `Observability` may be reordered between releases, and only relative to each other; the floor and ceiling are stable.

An application that needs an audit record atomic with the change it describes registers an `ICommandCompletionHandler` at `HandlerPriorities.UnitOfWork`, gates the commit on `context.Outcome`, and stages the record from its `IAuditTrail` rather than writing it. The commit flushes both. A record for a non-success outcome cannot ride the transaction being rolled back and has to be written out of band.

**Open generic handlers:**

```csharp
public sealed class LogAnyCommand<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default) => ...;
}

// Register the open definition; LiteBus closes it per concrete command type.
liteBus.AddCommands(commands => commands.Register(typeof(LogAnyCommand<>)));
```

**Runtime dispatch.** Handlers registered under a closed contract carry a pre-bound `PipelineDispatch`. Handlers registered under an open contract get a dispatch bound lazily and cached in a `ConcurrentDictionary` keyed by the closed contract type (`PipelineHandlerInvoker.ResolveRuntimeDispatch`). A contract the pipeline cannot dispatch throws `LiteBusConfigurationException`. `PipelineDispatch.For(Type)` and `PipelineDispatch.StageFor(Type)` are public but `EditorBrowsable(Never)`; both are annotated `RequiresUnreferencedCode`, so trimming needs care.

### 4.6 Execution context

```csharp
public interface IExecutionContext
{
    CancellationToken CancellationToken { get; }
    IDictionary<string, object> Items { get; }
    IReadOnlyCollection<string> Tags { get; }
    object? MessageResult { get; set; }
    bool PostHandlersSuppressed { get; }
    void SuppressPostHandlers();
}
```

Access it from anywhere inside a mediation through `AmbientExecutionContext`:

| Member | Behaviour |
| --- | --- |
| `AmbientExecutionContext.Current` | Get throws `NoExecutionContextException` when no context is set; set replaces the ambient value (assigning `null` clears it). |
| `AmbientExecutionContext.HasCurrent` | `bool`, no throw. |
| `AmbientExecutionContext.GetCurrentOrDefault()` | `IExecutionContext?`. |
| `AmbientExecutionContext.CreateScope(IExecutionContext)` | Returns `ExecutionContextScope` implementing both `IDisposable` and `IAsyncDisposable`; disposal restores the previous context and is idempotent. |
| `AmbientExecutionContext.ResetForTesting()` | Clears the `AsyncLocal` value. Test isolation only. |

Well-known `Items` keys:

| Key constant | String value | Written by |
| --- | --- | --- |
| `MessageTraceContextKeys.CorrelationId` | `__LiteBus.Trace.CorrelationId` | inbox/outbox dispatch (`MessageProcessorDiagnostics.ApplyTraceMetadata`) |
| `MessageTraceContextKeys.CausationId` | `__LiteBus.Trace.CausationId` | same |
| `MessageTraceContextKeys.TenantId` | `__LiteBus.Trace.TenantId` | same |
| `MessageTraceContextKeys.TraceContext` | `__LiteBus.Trace.TraceContext` | same |
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

`IExecutionContext.Data` returns an `IHandleContextData`: a store keyed by the CLR type of the value rather than by a string, created once per mediation.

| Member | Behavior |
| --- | --- |
| `Set<T>(T value)` | Stores under `typeof(T)`, replacing any existing value. Throws `ArgumentNullException` on null. |
| `Get<T>()` | Returns the value or throws `HandleContextDataNotFoundException` (exposes `DataType`). |
| `TryGet<T>(out T value)` | `false` instead of throwing when absent. |
| `Contains<T>()` / `Remove<T>()` | Presence check and removal. |

The implementation is `HandleContextData` in `LiteBus.Messaging.Abstractions`, public so a test double implementing `IExecutionContext` has a working store to return. Access is guarded by a `Lock`, because event handlers can run in parallel over one execution context.

It exists so a pre-stage handler can hand a resolved object to the main handler. The motivating case is a guard whose authorization decision depends on a loaded aggregate: without a typed channel the handler loads the same aggregate again, which is why authorization tends to stay inside handlers instead of moving to the stage that owns the decision.

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

Use `Items` (`IDictionary<string, object>`) only where the key comes from outside the process or the value is a flag; `Data` is preferred for anything with a type to key on.

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

**Source 1 - attributes** that implement `IMessageDeclarationSource`:

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

Only attributes implementing this interface are collected, so unrelated attributes never pollute the metadata bag.

**Source 2 - definition classes** implementing closed `IMessageDefinition<TMessage, TValue>`:

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

A definition must have a parameterless constructor (public or not) and is instantiated **once** during registration - definitions cannot take dependencies. `IAuditDefinition<TMessage>` is the shipped specialization (`AuditDeclaration Audit { get; }` mapped onto `IMessageDefinition<TMessage, AuditDeclaration>.Value`).

**Precedence** (`MessageMetadata.Wins` and `MetadataSourceKind`):

* Same declaring message type: `Definition` (1) beats `Attribute` (0).
* Different declaring types: the more derived declaring type wins.
* Two unrelated declaring types both covering one message: `LiteBusConfigurationException` telling you to declare the value on the message itself.
* A declaration whose value is not an instance of its declared key type: `LiteBusConfigurationException`.
* Two definitions declaring the same value type for the same message: `LiteBusConfigurationException`.
* A definition that implements no `IMessageDefinition<,>`, exposes no readable `Value`, or returns `null`: `LiteBusConfigurationException`.

A declaration written for a base type covers derived messages through `IsAssignableFrom`; open generic types never cover each other.

**Requiring a declaration.** `MessageModuleBuilder.RequireDeclaration<TValue>()` fails composition for any registered message that neither declares `TValue` nor records an exemption from it.

```csharp
registry.AddMessaging(messaging => messaging
    .RequireDeclaration<RequiredPermission>()
    .RequireDeclaration<RetentionClass>());
```

The check is registered through `IModuleConfiguration.RegisterCompositionValidation(Action)` and runs after every module has been built, because the messaging module is foundational and has no commands or queries to inspect during its own build. Both host adapters (`AddLiteBus` and the Autofac `RegisterLiteBus`) run `moduleConfiguration.CompositionValidations` after the module loop. Abstract types and interfaces are skipped: they are shapes, and a declaration on one covers the messages beneath it. The `LiteBusConfigurationException` names every offender grouped by the omitted value type, not just the first.

**Exemptions.** `[DeclarationExempt(typeof(TValue), "rationale")]` is repeatable and every instance is aggregated by `MessageDescriptor` into one `DeclarationExemptions` metadata value (`Values`, `Covers<TValue>()`, `Covers(Type)`, `TryGet(Type, out DeclarationExemption)`, `Merge`). It deliberately does **not** implement `IMessageDeclarationSource`, because that contract maps one attribute to one value and several exemptions have to collapse into one set. A definition may contribute `DeclarationExemptions` directly, replacing the attribute set rather than adding to it. `[AuditExempt]` needs none of this: it already produces an `AuditDeclaration`, so an audit-exempt message satisfies `RequireDeclaration<AuditDeclaration>()`.

**Making an attribute analyzable.** `[MessageDeclaration(typeof(TValue))]` on an attribute class states which value that attribute declares. It exists because `IMessageDeclarationSource.DeclarationType` is a runtime property an analyzer cannot execute, and `LB1020` needs a static answer. Registration throws `LiteBusConfigurationException` when the annotation and the property name different types. LiteBus annotates `[Audited]` and `[AuditExempt]` with `typeof(AuditDeclaration)`. Definition classes need no annotation; their declaration is the second type argument of `IMessageDefinition<TMessage, TValue>`.

**Reading declarations from application code.** Resolve `IMessageMetadataAccessor` (singleton, `MessageMetadataAccessor` over `IMessageReader`). It is the supported surface; reaching for `IMessageRegistry.Find(...)!.Metadata` makes the descriptor shape part of the application.

```csharp
public interface IMessageMetadataAccessor
{
    IMessageMetadata ForMessage(Type messageType);
    IMessageMetadata ForMessage<TMessage>() where TMessage : notnull;
    bool TryGet<TValue>(Type messageType, [MaybeNullWhen(false)] out TValue value) where TValue : notnull;
    bool TryGet<TMessage, TValue>([MaybeNullWhen(false)] out TValue value) where TMessage : notnull where TValue : notnull;
}
```

An unregistered type raises `MessageMetadataNotFoundException` (exposes `MessageType`) rather than returning an empty bag, because an empty answer turns a missing registration into a permission check that silently passes. A closed generic message resolves to its generic type definition, matching `IMessageReader.Find`. Registered by `AddMessaging`, so it is available with or without auditing.

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

**Declaration values may be delegates.** A definition is instantiated once and cannot take dependencies, but its value is an ordinary object and may carry a `Func` over the message. That covers every scope derivable from the message itself, not only constants:

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

**Types:** `AuditDeclaration` / `AuditedDeclaration` / `AuditExemptDeclaration`, `AuditedAttribute` / `AuditExemptAttribute`, `IAuditDefinition<TMessage>`, `AuditRecord`, `AuditOutcome`, `IAuditTrail`, `IAuditScope`, `IAuditRecordWriter`, `IAuditOutcomeMapper`, `DefaultAuditOutcomeMapper`, `AuditTrailDiagnosticCheck`, `CommandAuditCompletionHandler`, `QueryAuditCompletionHandler`.

**The declaration half is separable from the writing half.** `IAuditScope` and `IAuditOutcomeMapper` are registered by `AddMessaging`, not by `EnableAuditing()`, so both resolve whether or not any axis produces records. An application wanting the declaration model with its own writer injects `IAuditScope`, reads `AuditDeclaration` through `IMessageMetadataAccessor`, and never calls `EnableAuditing()`. Nothing consumes the scope in that configuration, so pushed values are discarded; that is intended, and the `litebus.audit.trail` probe stays quiet because auditing was never enabled.

**Wiring:**

```csharp
liteBus.AddMessaging(messaging => messaging
    .UseAuditTrail<SqlAuditTrail>()                     // scoped by default; pass InstanceLifetime to change it
    .UseAuditOutcomeMapper<UseCaseAuditOutcomeMapper>() // optional
    .UseTimeProvider(TimeProvider.System));

liteBus.AddCommands(commands => commands.RegisterFromAssembly(assembly).EnableAuditing());
liteBus.AddQueries(queries => queries.RegisterFromAssembly(assembly).EnableAuditing());
```

`EnableAuditing()` registers a completion handler at priority `HandlerPriorities.Observability` covering `ICommand` / `IQuery` respectively, plus the `litebus.audit.trail` diagnostic probe. The probe reports `Unhealthy` when auditing is enabled but no `IAuditTrail` is registered, and `Healthy` with `component`, `trailRegistered`, `trailType` and `trailIsSingleton` data otherwise. It resolves the trail through `IMessageDispatchScopeFactory` twice and compares instances, both to see the lifetime from outside the container and because resolving a scoped trail from a root provider is an error under `ValidateScopes`. Resolving `IAuditRecordWriter` without a trail throws `LiteBusConfigurationException`.

**Declaring a position:**

```csharp
[Audited("orders.place-order", Category = "money", TargetKind = "order", ReasonRequired = false)]
public sealed record PlaceOrderCommand(Guid CartId) : ICommand<OrderId>;

[AuditExempt("browsing a public storefront is not a sensitive action")]
public sealed record GetStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
```

Analyzer `LB1018` ("Message states no audit position", **disabled by default**) reports messages declaring neither; enable with `dotnet_diagnostic.LB1018.severity = warning`. It is the preconfigured instance of `LB1020`, sharing `DeclarationAnalysis` with `AuditDeclaration` as the required value type.

**Contributing the runtime half:**

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

`IAuditScope` methods throw `ArgumentException` on a blank argument and `NoExecutionContextException` when called outside a mediation (the scope needs the ambient execution context to store state).

**Outcome mapping.** `DefaultAuditOutcomeMapper.MapByOutcome`:

| `MediationOutcome` | `AuditOutcome` |
| --- | --- |
| `Succeeded` | `Succeeded` |
| `Answered` | `Succeeded` |
| `Denied` | `Denied` |
| `Invalid` | `Invalid` |
| `Canceled` | `Canceled` |
| `Failed` (and anything else) | `Failed` |

`IAuditOutcomeMapper.MapFailureCode` has a default implementation returning `null` for no exception and for `LiteBusMessageDeniedException`, and the exception type name otherwise.

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

**Trail contract and constraints:**

```csharp
public interface IAuditTrail
{
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
```

The record arrives at the completion stage, at priority `HandlerPriorities.Observability`. The completion stage is not cancellable, so the token is always `CancellationToken.None`. `ReasonRequired = true` with a `Succeeded` outcome and no reason raises `LiteBusConfigurationException` instead of writing an incomplete record.

**Making a record atomic with its change.** LiteBus never opens or commits a transaction, but it guarantees a position an application can commit from. Register a completion handler at `HandlerPriorities.UnitOfWork` and stage the record from the trail rather than writing it:

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

Three guarantees make it work: `UnitOfWork` is above `ReservedCeiling` so it runs after the writer at `Observability` in every release; the completion stage orders by priority alone, so registration breadth cannot reorder the commit against the writer; and a completion handler that throws on an otherwise clean mediation propagates, so a commit conflict reaches the caller instead of being swallowed. Do not put the commit in a post-handler: a post-handler is skipped when the main handler throws, and everything LiteBus writes afterwards is outside the transaction by construction.

### 4.9 Message contracts and serialization

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

Extension methods: `IContractWriter.RegisterFromAssembly(Assembly)` and `IMessageContractRegistry.RegisterFromAssembly(Assembly)` both delegate to `AddFromAssembly`.

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

**Behaviour and errors:**

| Condition | Result |
| --- | --- |
| Open generic message type passed to `Register` | `ArgumentException` - register each closed shape with its own name and version. |
| `version <= 0` | `ArgumentOutOfRangeException` |
| Blank `name` | `ArgumentException` |
| Same type registered again with different values, or a name/version pair reused for another type | `MessageContractAlreadyRegisteredException` |
| Explicit registration disagreeing with `[MessageContract]` on the same type | `MessageContractMismatchException` naming both sides |
| Lookup for an unregistered type | `MessageContractNotRegisteredException(Type)` |
| Lookup for an unregistered `(name, version)` | `MessageContractNotRegisteredException(string, int)` |

`MessageContractBuilder` is the deferred writer used by composite module builders (`InboxModuleBuilder.Contracts`, `OutboxModuleBuilder.Contracts`): it records registrations as data (`HasRegistrations`) and replays them with `ApplyTo(IMessageContractRegistry)` once the live registry exists.

`IMessageContractResolver` decides which CLR type is used for contract lookup during accept/enqueue. Unregistered, the runtime instance type is used. Register `DeclaredTypeMessageContractResolver` to use the declared parameter type instead:

```csharp
services.AddSingleton<IMessageContractResolver, DeclaredTypeMessageContractResolver>();
```

**Serialization.**

```csharp
public interface IMessageSerializer
{
    Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;
    Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default);
}
```

`SystemTextJsonMessageSerializer` uses `new JsonSerializerOptions(JsonSerializerDefaults.Web)` unless options are supplied to the constructor, serializes with `message.GetType()` (so derived shapes round-trip), and wraps `JsonException` / `NotSupportedException` in `MessageSerializationException` with `Operation` set to `"serialized"` or `"deserialized"`. A `null` deserialization result becomes a `MessageSerializationException` too. Replace the default with your own registration after `AddLiteBus`, or register a custom `IMessageSerializer` implementation.

### 4.10 Payload encryption

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

The module wraps the encryptor in `IInboxPayloadProtector` / `IOutboxPayloadProtector` (both derive from `IPayloadEncryptor`). `PayloadProtection.ProtectAsync` / `UnprotectAsync` are the static seams used by the envelope factories and every dispatcher; when the encryptor also implements `IContextualPayloadEncryptor` and a context is supplied, the contextual overload is used, binding `MessageId`, `ContractName`, `ContractVersion`, `TenantId` and `Axis` (`"inbox"` or `"outbox"`) as authenticated metadata. A `null` encryptor is a no-op pass-through. Both methods honour cancellation before doing any work.

### 4.11 Inbox

**Purpose.** Accept a command durably now, execute it later through a processor, exactly-once-per-envelope with at-least-once dispatch.

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

`ITransactionalInbox` has the same shape minus the untyped single accept; `ITransactionalInbox<TContext>` adds the EF Core flavour where rows are written when the scoped context saves changes.

**Composition:**

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

**Accepting:**

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

**Envelope creation** (`InboxEnvelopeFactory`), in order: validate the instance is assignable to the declared type (`ArgumentException` otherwise) -> `InboxCommandMessageGuard.EnsureVoidCommand` (throws `InvalidOperationException` for an `ICommand<TResult>`; analyzer `LB1004` catches it at compile time) -> resolve the contract -> stamp `CreatedAt` from `TimeProvider` -> resolve the message id -> resolve the tenant id -> serialize -> optionally encrypt with a `PayloadProtectionContext` -> project trace columns -> build the `Pending` envelope with `AttemptCount = 0`.

**Operator API** (`IInboxManager`):

| Method | Return | Behaviour |
| --- | --- | --- |
| `QueryAsync(filter, pageRequest, ct)` | `Task<InboxMessagePage>` | Keyset page. |
| `GetMessageAsync(messageId, ct)` | `Task<InboxEnvelope?>` | Single-row query with `PageSize = 1`. |
| `RequeueDeadLettersAsync(ct)` | `Task<int>` | Pages dead letters 200 at a time and requeues each page. |
| `RequeueAsync(messageIds, ct)` | `Task<RequeueResult>` | Empty input short-circuits to `(0, 0)`. |
| `PurgeAsync(filter, confirm = false, ct)` | `Task<int>` | Throws `InboxManagementException` for an unrestricted filter without `confirm: true`. |
| `GetStatusCountsAsync(ct)` | `Task<IReadOnlyDictionary<InboxStatus, int>>` | Queue depth by status. |
| `GetSchemaInfoAsync(ct)` | `Task<StoreSchemaInfo>` | Expected vs recorded schema version. |
| `GetRetentionStatusAsync(ct)` | `Task<RetentionRunStatus>` | Snapshot from `InboxRetentionCoordinator`. |
| `RunRetentionPurgeAsync(ct)` | `Task<int>` | Immediate retention pass; returns 0 when `Retention` is null or non-positive; records success or failure on the coordinator and rethrows on failure. |

**Store roles.** `IInboxStore` (`AddAsync`, `AddBatchAsync`), `IInboxLeaseStore` (`LeasePendingAsync` + inherited `RenewLeaseAsync`), `IInboxStateWriter` (`PersistAsync`), `IInboxDeadLetterStore` (`RequeueAsync(IReadOnlyList<Guid>)` and a default single-id overload), `IInboxRetentionStore` (`DeleteCompletedOlderThanAsync`), `IInboxDiagnosticsStore` (`GetStatusCountsAsync`, `GetSchemaInfoAsync`), `IInboxMessageQuery` (`QueryAsync`), `IInboxPurgeStore` (`PurgeAsync`). Composites: `IInboxProcessingStore` (lease + state writer) and `IInboxOperationsStore` (dead letter + retention + diagnostics + query + purge). `ITransactionalInboxStore : IInboxStore` marks a writer that joins the caller's transaction. Extension: `IInboxStore.EnqueueAsync(envelope, ct)` aliases `AddAsync`; `IInboxDeadLetterStore.RequeueAsync(IReadOnlyList<string>, ct)` parses string ids and throws `ArgumentException` on a malformed value.

**Exceptions.** `InboxDispatchException`, `InboxIngressException`, `InboxStorageException`, `InboxManagementException`, `IdempotencyConflictException`.

### 4.12 Outbox

**Purpose.** Persist an event inside the caller's transaction, publish it later.

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

`ITransactionalOutbox` and `ITransactionalOutbox<TContext>` mirror this for transactional writes.

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

`IOutboxManager` mirrors `IInboxManager` with `OutboxMessageFilter`, `OutboxMessagePage`, `OutboxStatus` and `RunRetentionPurgeAsync` calling `DeletePublishedOlderThanAsync`. Store roles mirror the inbox: `IOutboxStore`, `IOutboxLeaseStore`, `IOutboxStateWriter`, `IOutboxDeadLetterStore`, `IOutboxRetentionStore` (`DeletePublishedOlderThanAsync`), `IOutboxDiagnosticsStore`, `IOutboxMessageQuery`, `IOutboxPurgeStore`, `IOutboxProcessingStore`, `IOutboxOperationsStore`, `ITransactionalOutboxStore`.

**Exceptions.** `LiteBusDispatchException` (raised by `EventOutboxDispatcher` around any non-dispatch failure with a message naming the contract), `OutboxManagementException`, `IdempotencyConflictException`.

### 4.13 Durable processors

**Type:** `PipelinedMessageProcessor<TEnvelope, TOptions>` (internal, shared), specialized by `PipelinedInboxProcessor` and `PipelinedOutboxProcessor` through `IPipelinedMessageProcessorOperations<TEnvelope, TOptions>`.

**One pass (`ProcessPendingAsync`):**

1. Start the pass activity (`inbox.processor.pass` / `outbox.processor.pass`) and a `ProcessorPassStopwatch`.
2. `LeasePendingAsync(leaseOwner, options, now, ct)` claims up to `BatchSize` rows in `CreatedAt` order, optionally filtered by `TenantId`. A row is claimable when it is `Pending` or `Failed` and its `VisibleAfter` is null or already past; or it is `Processing` / `Publishing` with a `LeaseExpiresAt` at or before `now`; or it is `Processing` / `Publishing` with a null `LeaseExpiresAt` and a `CreatedAt` older than `now - LeaseDuration` (the stale cutoff). Each claimed row gets `LeaseOwner`, an incremented `LeaseGeneration`, `LeaseExpiresAt = now + LeaseDuration`, and an incremented `AttemptCount`. A lease request with a zero `LeaseDuration` falls back to the store's `DefaultLeaseDuration`.
3. Log the batch (`EventId 3001`), record `leases_acquired`. An empty batch finalizes the pass immediately.
4. Fan out one worker task per envelope, gated by a `SemaphoreSlim(DispatcherConcurrency)`.
5. Each worker runs under `ProcessorLeaseHeartbeat.RunWithHeartbeatAsync`: renew once up front, then renew every `LeaseHeartbeatInterval` until the operation ends. A failed renewal logs `EventId 3002`, records `lease_lost`, and cancels the dispatch token, so the dispatch observes `OperationCanceledException` and the envelope is persisted as retryable with `MessageProcessorDiagnostics.LeaseLostDuringProcessingError` ("Lease lost during processing; scheduling retry.").
6. Dispatch: run `IProcessorEnvelopeHook.BeforeDispatchAsync`, `PrepareDispatchScope`, then `ShouldDispatch` (a `false` from any hook completes/publishes the envelope without dispatching), then `IInboxDispatcher.DispatchAsync` / `IOutboxDispatcher.DispatchAsync`, then record the dispatch-duration histogram, then transition to `AsCompleted()` / `AsPublished(now)`.
7. Failure mapping: format the error, log `DispatchFailed`, then dead-letter when `AttemptCount >= Retry.MaxAttempts` **or** `MediationExceptionFilters.IsRetryableDispatchException(exception)` is false (that filter excludes `NoHandlerFoundException`, `MultipleHandlerFoundException` and refusals). Otherwise mark failed with `VisibleAfter = now + Retry.CalculateDelay(AttemptCount)`.
8. `AfterDispatchAsync` hooks run while the lease is still held. A hook failure follows `HookFailurePolicy`: `CompleteDespiteHookFailure` logs and persists the success; `DeadLetter` dead-letters the source envelope.
9. Persist the terminal state through `PersistAsync`, using the shutdown token when `HonorShutdownTokenOnPersist` is true. A `PersistResult` with `SkippedCount > 0` records `persist_skipped` / `persist_rejected`. A persistence exception after a completed dispatch logs `EventId 3003`, records `persist_failed`, and the pass continues with the remaining envelopes - it does not abort the batch.
10. `FinalizePass` builds the `ProcessorPassResult` and records telemetry.

**Loop (`ProcessorBackgroundService<TProcessor>`):** validate host options, exit when `Enabled` is false, apply `StartupDelay` (interruptible by a drain request), then loop: `WaitIfPausedAsync` -> one pass -> `SignalPassComplete` -> when the pass leased less than `BatchSize` (or `UseAdaptivePolling` is off) wait on `IProcessorWorkSignal.WaitForWorkOrDelayAsync(PollInterval, token)`. An unexpected pass exception records a loop error, logs, and continues (unless it was the drain pass, which rethrows). Work-signal failures also record and continue, so a broken LISTEN/NOTIFY connection cannot kill the loop.

**Control surface:**

```csharp
public interface IInboxProcessorControl   // and IOutboxProcessorControl
{
    ProcessorState State { get; }
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
```

`PauseAsync` flips the state to `Paused` and awaits the in-flight pass. `ResumeAsync` releases the gate. `DrainAsync` requests one final pass then exit; `Timeout.InfiniteTimeSpan` is accepted, any other negative value throws `ArgumentOutOfRangeException`. Calling pause or resume while draining throws. The implementations also implement `IProcessorBackgroundControl` (`DrainRequestedToken`, `IsDraining`, `WaitIfPausedAsync`, `SignalPassComplete`, `SignalDrainComplete`) and `IAsyncDisposable`.

**Work signals.** `IProcessorWorkSignal.WaitForWorkOrDelayAsync(TimeSpan pollInterval, CancellationToken)`; `IInboxWorkSignal` / `IOutboxWorkSignal` are axis markers. `InboxPollingWorkSignal` / `OutboxPollingWorkSignal` simply `Task.Delay` (returning immediately for a non-positive interval). `PostgreSqlInboxWorkSignal` / `PostgreSqlOutboxWorkSignal` use LISTEN/NOTIFY when `UseListenNotify` is enabled.

**Processor hooks:**

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

Hooks are resolved from `IEnumerable<IProcessorEnvelopeHook>`, falling back to a single `IProcessorEnvelopeHook` registration, falling back to none. `ShouldDispatch` is combined with `&=` across hooks. `AbandonDispatchScope` runs in a `finally` whenever dispatch did not complete.

**Retention cleanup** (`InboxCleanupBackgroundService` / `OutboxCleanupBackgroundService`): validate, exit when disabled or `Retention` is null, then loop `DeleteCompletedOlderThanAsync(now - Retention)` / `DeletePublishedOlderThanAsync(...)`, record success on the coordinator, and sleep `Interval`. On failure it increments the `cleanup.errors` counter, records the failure, logs (`EventId 1101`), sleeps the current backoff, then doubles it up to a 5-minute cap.

### 4.14 Dispatch adapters

| Extension | Package | Registers |
| --- | --- | --- |
| `InboxModuleBuilder.UseInProcessDispatch()` | `LiteBus.Inbox.Dispatch.InProcess` | `CommandInboxDispatchModule` (requires `InboxModule` and `CommandModule`) -> `IInboxDispatcher` = `CommandInboxDispatcher` |
| `OutboxModuleBuilder.UseInProcessDispatch()` | `LiteBus.Outbox.Dispatch.InProcess` | `EventOutboxDispatchModule` (requires `OutboxModule` and `EventModule`) -> `IOutboxDispatcher` = `EventOutboxDispatcher` |
| `UseInMemoryDispatch(Action<TransportInboxDispatcherOptions>? = null)` | `LiteBus.Inbox.Dispatch.InMemory` | `TransportInboxDispatchModule<InMemoryTransportModule>` |
| `UseAmqpDispatch(Action<TransportInboxDispatcherOptions>)` | `LiteBus.Inbox.Dispatch.Amqp` | `TransportInboxDispatchModule<AmqpTransportModule>` |
| `UseKafkaDispatch(...)` | `LiteBus.Inbox.Dispatch.Kafka` | `TransportInboxDispatchModule<KafkaTransportModule>` |
| `UseAwsSqsDispatch(...)` | `LiteBus.Inbox.Dispatch.AwsSqs` | `TransportInboxDispatchModule<AwsSqsTransportModule>` |
| `UseAzureServiceBusDispatch(...)` | `LiteBus.Inbox.Dispatch.AzureServiceBus` | `TransportInboxDispatchModule<AzureServiceBusTransportModule>` |
| the same five `Use*Dispatch` names on `OutboxModuleBuilder` | `LiteBus.Outbox.Dispatch.*` | `TransportOutboxDispatchModule<TTransportModule>` |

`TransportInboxDispatchModule<TTransportModule>` / `TransportOutboxDispatchModule<TTransportModule>` declare `IRequires<TTransportModule>`, so the matching `Add*Transport(...)` call at the root is mandatory; the outbox variant reports `DefaultHookFailurePolicy => CompleteDespiteHookFailure`.

**`CommandInboxDispatcher`**: resolves the CLR type from the contract, decrypts, deserializes, requires the result to implement `ICommand` (`InvalidOperationException` otherwise), builds `CommandMediationSettings` with `IsInboxExecution`, `MessageId`, `ContractName` and the trace items, then sends through `ICommandMediator`.

**`EventOutboxDispatcher`**: same resolution, then publishes through `IEventMediator`. If the instance implements `IEvent` it uses the non-generic overload; otherwise it builds and caches a closed generic `PublishAsync<TEvent>` delegate per message type. Every non-`LiteBusDispatchException` failure is wrapped in `LiteBusDispatchException` with remediation text.

**`TransportInboxDispatcher` / `TransportOutboxDispatcher`**: resolve the CLR type, decrypt, optionally deserialize for validation, resolve the route, UTF-8 encode the payload and publish a `TransportPublishRequest` with `MessageId = envelope.Id.ToString("D")`, `CorrelationId`, and the canonical `TransportHeaders`.

### 4.15 Transports

**Contracts:**

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

**Canonical headers** (`TransportHeaders`):

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

`TransportEnvelopeHeaderMapper.BuildHeaders(TransportEnvelopeHeaderSource)` produces this dictionary; `TransportEnvelopeHeaderSource` is `(Guid MessageId, string ContractName, int ContractVersion, string? CorrelationId, string? CausationId, string? TenantId, string? TraceContext, string? IdempotencyKey, DateTimeOffset? VisibleAfter)`. `TransportHeaderValues.GetString`, `GetInt32` and `ConvertToString` read header values that brokers may deliver as strings, byte arrays or numerics. A missing or invalid required header raises `TransportHeaderMappingException` (factories `MissingRequiredHeader(name)` and `InvalidRequiredHeader(name, detail)`).

**Circuit breaker.** `ITransportCircuitBreakerRegistry` hands out named `ITransportCircuitBreaker` instances; `TransportCircuitBreaker` opens after `FailureThreshold` consecutive failures and rejects calls with `TransportCircuitBreakerOpenException` for `BreakDuration`. `TransportCircuitBreakerPermit` is the disposable success/failure token. Gauges `litebus.transport.circuit_breaker.open` and `litebus.transport.circuit_breaker.failure_count` are tagged with `litebus.transport.broker`.

**Adapter registration and probes:**

| Transport | Root call | Registered services | Diagnostic probe |
| --- | --- | --- | --- |
| In-memory | `AddInMemoryTransport()` | `InMemoryTransportOptions`, `InMemoryTransportBroker` (Singleton), `ITransportCircuitBreakerRegistry`, `ITransportPublisher` = `InMemoryPublisher`, `IMessageConsumer` = `InMemoryConsumer` | none |
| AMQP (RabbitMQ, LavinMQ) | `AddAmqpTransport(AmqpConnectionOptions)` | `AmqpConnectionOptions`, `IAmqpConnectionManager` = `AmqpConnectionManager` (Singleton), `ITransportCircuitBreakerRegistry` (from `options.CircuitBreaker`), `ITransportPublisher`/`IAmqpPublisher` = `AmqpPublisher`, `IMessageConsumer`/`IAmqpConsumer` = `AmqpConsumer` | `transport.amqp.connectivity` |
| Kafka | `AddKafkaTransport(KafkaTransportOptions)` | `KafkaTransportOptions`, `IProducer<string, byte[]>`, `IConsumer<string, byte[]>`, `IAdminClient` (all Singleton), `ITransportCircuitBreakerRegistry`, `ITransportPublisher` = `KafkaPublisher`, `IMessageConsumer` = `KafkaConsumer` | `transport.kafka.connectivity` |
| AWS SQS | `AddAwsSqsTransport(AwsSqsTransportOptions)` | `AwsSqsTransportOptions`, `IAmazonSQS` (Singleton), `ITransportCircuitBreakerRegistry`, `ITransportPublisher` = `AwsSqsPublisher`, `IMessageConsumer` = `AwsSqsConsumer` | `transport.sqs.connectivity` |
| Azure Service Bus | `AddAzureServiceBusTransport(AzureServiceBusTransportOptions)` | `AzureServiceBusTransportOptions`, `ServiceBusClient` (Singleton), `ITransportCircuitBreakerRegistry`, `ITransportPublisher` = `AzureServiceBusPublisher`, `IMessageConsumer` = `AzureServiceBusConsumer` (Singleton) | `transport.azure_service_bus.connectivity` |

Every adapter also calls `TransportMetricsRegistration.RegisterIfNeeded(configuration, brokerName)`, which registers `TransportObservableMetrics` plus the `TransportObservableMetricsInitializer` startup task.

`TransportConsumerHandlerInvoker.CreateBoundedHandler(handler, maxInFlight)` wraps a delivery handler with a concurrency gate and returns `TransportConsumerInvocationOutcome` (`Handled` or `Requeued`).

### 4.16 Inbox ingress (broker to inbox)

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

`TransportInboxIngressConsumer` is an `IBackgroundService` that starts the broker consumer, maps each delivery through `TransportInboxIngressMapper.ToInboxAcceptMetadata` (identity, idempotency, visibility, trace, tenant derived from `TransportHeaders` and broker properties, honouring `RequireStableIdentity` and `TrustApplicationHeaders`), accepts it into the inbox, and settles the delivery. On a loop failure it logs `EventId 3002` and restarts after `RetryPollInterval`. A batch flush failure logs `EventId 3003`. When broker acknowledgement fails *after* a successful accept it logs `EventId 3004` and increments `ingress.ack_failed_after_accept`, because that is the at-least-once window.

`IngressAckPolicy.ShouldRequeue(exception, requeueOnFailure)` refuses to requeue when `requeueOnFailure` is false, or when the unwrapped exception is one of `MessageContractNotRegisteredException`, `InboxDispatchException`, `InboxIngressException`, `InboxStorageException`, `InvalidOperationException`, `ArgumentException`, `FormatException`, `JsonException` - these are poison-message classes that would loop forever. `IngressAckPolicy.UnwrapException` peels `TargetInvocationException` and single-inner `AggregateException` wrappers.

Only **one** ingress source may be registered per inbox module, because ingress host options, the transport consumer and background-service ownership are singular.

### 4.17 Saga orchestration

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

`EnableSaga` registers two child modules: `SagaModule` (a `ChildrenFirst` composite that owns the storage module) and `SagaInboxCommandScopeModule`. `SagaModule.Build` registers `ISagaStateTypeRegistry`, `SagaExecutionContext`, `ISagaContext` (projected from `SagaExecutionContext`) and `IProcessorEnvelopeHook` = `SagaProcessorHook`, all Singleton.

Handlers read and write state through the ambient saga context:

```csharp
public sealed class PaymentAuthorizedHandler : ICommandHandler<PaymentAuthorized>
{
    private readonly ISagaContext _saga;

    public PaymentAuthorizedHandler(ISagaContext saga) => _saga = saga;

    public Task HandleAsync(PaymentAuthorized message, CancellationToken cancellationToken = default)
    {
        var state = _saga.GetState<CheckoutSagaState>();
        state.PaymentAuthorized = true;
        _saga.SetState(state);

        if (state.IsComplete)
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
    Task<SagaInstance<TState>?> LoadAsync<TState>(SagaCorrelation correlation, CancellationToken ct = default)
        where TState : class, new();
    Task SaveAsync<TState>(SagaSaveItem<TState> item, CancellationToken ct = default) where TState : class, new();
    Task CompleteAsync(SagaCompleteItem item, CancellationToken ct = default);
    Task<IReadOnlyList<SagaInstanceSummary>> QueryAsync(SagaQueryFilter filter, CancellationToken ct = default);
    Task<int> PurgeAsync(SagaPurgeFilter filter, CancellationToken ct = default);
}
```

`SagaProcessorHook` resolves the definition id from the envelope's contract name, loads (or creates) the instance for the correlation, publishes it as the ambient saga state, and on `AfterDispatchAsync` saves or completes with the expected version. A version conflict raises `SagaConcurrencyException` (carrying the `Correlation`); completion-only dispatches retry a bounded number of times. `LastAppliedMessageId` / `AppliedMessageId` let a store make a replayed inbox message a no-op.

### 4.18 Storage adapters

#### In-memory

```csharp
inbox.UseInMemoryStorage(store => store
    .UseOptions(new InMemoryInboxStoreOptions { Capacity = 10_000, DefaultLeaseDuration = TimeSpan.FromSeconds(30) })
    .UseTimeProvider(new ManualTimeProvider(DateTimeOffset.UnixEpoch)));
```

Registers one store instance under every inbox role interface plus the concrete `InMemoryInboxStore`, and `IInboxWorkSignal` = `InboxPollingWorkSignal`. Idempotency is indexed by a composite scope key built from the normalized tenant id and the idempotency key (`DurableIdempotencyScope.CreateScopeKey`, separated by U+001F; `DurableTenantId.Normalize` maps null/blank to the empty string). The outbox variant is symmetric.

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

Registers the store under every role, `PostgreSqlInboxStoreRegistration`, the `PostgreSqlInboxSchemaInitializer` startup task (unless disabled), the `inbox.postgresql.schema` diagnostic probe, and the work signal (LISTEN/NOTIFY or polling). `UseConnectionString` creates and registers an `NpgsqlDataSource` the container disposes; `UseDataSource` does not take ownership. Omitting both throws `InboxPostgreSqlStorageConfigurationException`.

With `EnableAmbientTransactionProvider`, a scoped `PostgreSqlTransactionalInboxParticipant` resolves the store bound to the ambient `IPostgreSqlTransactionProvider` connection and transaction. `TransactionalWriteMode.RequireActiveTransaction` throws when no ambient transaction is present; `AllowImmediateCommit` falls back to the auto-commit singleton store and is documented as development/test only.

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

The module registers `EfCoreInboxStore` / `EfCoreOutboxStore` as Singletons over `IDbContextFactory<TContext>` and projects them onto every role interface plus `ITransactionalInboxStore` / `ITransactionalOutboxStore`, and registers `IInboxWorkSignal` / `IOutboxWorkSignal` = the polling signal. `EnableSaveChangesInterceptor()` additionally registers the scoped `ITransactionalInbox<TContext>` / `ITransactionalOutbox<TContext>`.

`GetModelBuilderConfiguration` maps snake_case columns (`message_id`, `contract_name`, `contract_version`, `payload`, `topic`, `created_at`, `visible_after`, `attempt_count`, `status`, `idempotency_key`, `lease_owner`, `lease_generation`, `lease_expires_at`, `last_error`, `correlation_id`, `causation_id`, `tenant_id`, `trace_context`, `completed_at` / `published_at`, `last_attempted_at`, `first_failed_at`, `dead_lettered_at`, `last_lease_owner`, `error_type`) and creates: a filtered unique index on `(tenant_id, idempotency_key)` where `idempotency_key IS NOT NULL`, a composite index on `(status, visible_after, lease_expires_at, created_at)`, a `created_at` index (`IX_LiteBus_Inbox_CreatedAt` / `IX_LiteBus_Outbox_CreatedAt`), and for the outbox a filtered `topic` index. `ToTable` omits the schema for MySQL and SQLite.

Provider support (`EfCoreStorageProvider`, `EfCoreRelationalProviderNames`, `EfCoreRelationalTableQualifier`): PostgreSQL (`"schema"."table"`), SQL Server (`[schema].[table]`), MySQL (backtick table only), SQLite (quoted table only), and the EF in-memory provider. Raw lease SQL lives in `EfCorePostgreSqlLeaseSql`, `EfCoreSqlServerLeaseSql` and `EfCoreMySqlLeaseSql`; an unsupported provider throws `EfCoreStorageNotSupportedException`.

### 4.19 Modules, ordering and the host manifest

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

* Throws `LiteBusConfigurationException` after `BuildOrder()` has been called ("Cannot register modules after BuildOrder() has been called.").
* Module identity is the **concrete type**: registering two instances of one module type throws.
* A composite's `DeclareChildren` runs during `Register` (so the builder action must run there), children are staged recursively, and an ordering edge is added in the direction implied by `BuildOrder` (`ParentFirst` makes each child depend on the parent; `ChildrenFirst` makes the parent depend on each child). An undefined `BuildOrder` value throws.
* `BuildOrder()` performs a DFS topological sort over `IRequires<>` plus the implicit composite edges, ordering each node's dependencies by full type name for determinism. A cycle throws `LiteBusConfigurationException` naming the cycle path; a missing required module throws naming both modules.

`IModuleConfiguration`:

| Member | Behaviour |
| --- | --- |
| `DependencyRegistry` | The container-neutral registry. |
| `StartupTasks`, `BackgroundServices`, `DiagnosticChecks` | Snapshots of the host manifest being built. |
| `RegisterStartupTask(Type)` | Must implement `IStartupTask` and be a concrete class; duplicates are ignored without reordering. |
| `RegisterBackgroundService(Type)` | Must implement `IBackgroundService`; a type that also implements `IStartupTask` throws `ArgumentException` pointing at `RegisterStartupTask`. |
| `RegisterDiagnosticCheck(Type, string name)` | Must implement `IDiagnosticCheck`; re-registering the same type with a different name throws `LiteBusConfigurationException`. |
| `GetContext<T>()` | Throws `LiteBusConfigurationException` when the context is missing (module ordering problem). |
| `SetContext<T>(T)` | Throws when a different instance is already registered for that type. |
| `TryGetContext<T>(out T?)` | Non-throwing probe. |
| `GetOrCreateContext<T>(Func<T>)` | Creates on first use; a factory returning `null` throws. |

`LiteBusHostManifest` (`StartupTasks`, `BackgroundServices`, `DiagnosticChecks`) is built by `LiteBusHostManifest.FromConfiguration(...)` and registered as a Singleton. `LiteBusHostOrchestrator` runs every `IStartupTask.RunAsync` sequentially during `StartAsync`, then starts all `IBackgroundService.ExecuteAsync` loops in `ExecuteAsync`; an expected shutdown cancellation is swallowed, while any other exception calls `IHostApplicationLifetime.StopApplication()` and rethrows.

**Registration conflict policy** (`DependencyRegistrationTracker`): `Register` enforces one descriptor per service type - an equal duplicate is ignored, a different descriptor for the same service type throws `LiteBusConfigurationException` ("Each LiteBus module may register a given service type only once."). `RegisterCollection` skips that check so multiple implementations can be resolved through `IEnumerable<T>`. Order of first registration is preserved for deterministic container translation.

### 4.20 Diagnostics and health

```csharp
public interface IDiagnosticCheck
{
    string Name { get; }
    Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default);
}
```

`DiagnosticCheckExecution.CheckAsync(descriptor, check, ct)` validates that `check.Name` matches the manifest descriptor and throws `DiagnosticCheckNameMismatchException` (with `ImplementationType`, `DescriptorName`, `CheckName`) otherwise.

`DiagnosticCheckRunner.RunAsync(manifest, services, failHealthWhenNoProbes, [options], ct)` resolves each probe from the provider, runs them under a `SemaphoreSlim(MaxParallelism)` with a per-probe `Timeout`, and isolates failures:

| Situation | Probe outcome |
| --- | --- |
| Probe type not resolvable | `Unhealthy`, "The diagnostic check is not registered." |
| Probe exceeded `Timeout` | `Unhealthy`, "The diagnostic check timed out." (the linked token is cancelled) |
| Probe threw | `Unhealthy`, "The diagnostic check failed." |
| Caller cancelled | `OperationCanceledException` propagates |
| Manifest has no probes and `failHealthWhenNoProbes` is true | one synthetic `litebus.probes` outcome, `Degraded` |
| Manifest has no probes and the flag is false | `Healthy` with an empty probe list |

Aggregation: all `Healthy` -> `Healthy`; any `Unhealthy` -> `Unhealthy`; otherwise `Degraded`.

**Shipped probe names:**

| Name | Probe | Reports |
| --- | --- | --- |
| `litebus.audit.trail` | `AuditTrailDiagnosticCheck` | `Unhealthy` when auditing is enabled but no `IAuditTrail` is registered; otherwise `Healthy` with `component`, `trailRegistered`, `trailType`. |
| `inbox.postgresql.schema` | `PostgreSqlInboxSchemaDiagnosticCheck` | Expected vs recorded inbox schema version. |
| `outbox.postgresql.schema` | `PostgreSqlOutboxSchemaDiagnosticCheck` | Expected vs recorded outbox schema version. |
| `transport.amqp.connectivity` | `AmqpConnectivityDiagnosticCheck` | Broker connection reachability. |
| `transport.kafka.connectivity` | `KafkaConnectivityDiagnosticCheck` | Cluster describe within `ConnectivityCheckTimeout`. |
| `transport.sqs.connectivity` | `AwsSqsConnectivityDiagnosticCheck` | Reads `ConnectivityCheckQueueUrl` attributes. |
| `transport.azure_service_bus.connectivity` | `AzureServiceBusConnectivityDiagnosticCheck` | Peeks `ConnectivityCheckTarget`. |

`W3CTraceContextParser.TryParse(string?, out ActivityContext)` accepts either a bare `traceparent` string or a JSON object with `traceparent` and optional `tracestate`, and is used by `MessageProcessorDiagnostics.TryGetParentActivityContext` so a durable dispatch continues the originating trace.

### 4.21 Operator HTTP endpoints (`LiteBus.Extensions.AspNetCore`)

```csharp
builder.Services.AddLiteBusManagement(options => options.AuthorizationPolicy = "ops");
var app = builder.Build();
app.AddLiteBusManagementEndpoints();   // or AddLiteBusManagementEndpoints(explicitOptions)
```

Routes are grouped under `/{RoutePrefix}/inbox` and `/{RoutePrefix}/outbox`; an axis with no registered manager gets a fallback returning `404 Not Found` with the text `"Inbox is not configured."` / `"Outbox is not configured."`.

| Method and route (per axis) | Handler |
| --- | --- |
| `GET /messages` | `QueryAsync` with `[AsParameters]` filter binding, page size clamped to `MaxPageSize` |
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
| `POST /processor/drain` | `DrainAsync`, timeout defaulted to `DefaultDrainTimeout` and capped at `MaxDrainTimeout` |

Plus one shared route: `GET /{RoutePrefix}/health`, which runs the manifest probes through `DiagnosticCheckRunner` with `FailHealthWhenNoProbes` and `DiagnosticChecks`.

Query-string binding types: `InboxMessageQueryBinding`, `InboxMessagePurgeBinding`, `OutboxMessageQueryBinding`, `OutboxMessagePurgeBinding` (each exposes `ToFilter()` and, for query bindings, `ToPageRequest(maxPageSize)`). Failures are mapped to responses and logged: invalid input `EventId 4001` (Warning), operator-safety rejection `EventId 4002` (Warning), unexpected failure `EventId 5001` (Error). Every route gets `RequireAuthorization()` (or `RequireAuthorization(policy)`) unless `AllowAnonymousManagement` is set.

Option validation at map time throws `ArgumentException` / `ArgumentOutOfRangeException` for a blank `RoutePrefix`, non-positive `MaxPageSize`, `MaxBulkMessageIds`, `DefaultDrainTimeout`, `MaxDrainTimeout`, or diagnostic limits, and when `DefaultDrainTimeout > MaxDrainTimeout`.

### 4.22 OpenTelemetry

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

**Activity sources:** `LiteBus.Inbox`, `LiteBus.Outbox`, `LiteBus.Transport`. Spans: `inbox.processor.pass`, `inbox.processor.message`, `outbox.processor.pass`, `outbox.processor.message` (each message span tagged `litebus.message_id` and parented on the stored trace context), plus transport `send` and `process` spans.

**Meters:** `LiteBus.Inbox`, `LiteBus.Outbox`, `LiteBus.Transport`.

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

**Transport span tags** (`LiteBusTransportTelemetry`): `messaging.system`, `messaging.operation.name` (`send` / `process`), `messaging.operation.type`, `messaging.destination.name`, `messaging.message.id`, `messaging.message.conversation_id`, `messaging.kafka.message.key`, `messaging.rabbitmq.destination.routing_key`, `litebus.transport.route`, `litebus.transport.redelivered`. `TransportMessagingSystems` values: `aws_sqs`, `kafka`, `litebus_in_memory`, `litebus` (fallback for custom transports), `rabbitmq`, and `azure_service_bus` (used by the ASB module registration).

### 4.23 Testing support

| Package | Type | Purpose |
| --- | --- | --- |
| `LiteBus.Testing` | `LiteBusTestBase` | Abstract `IAsyncDisposable` base for tests sharing infrastructure. |
| `LiteBus.Testing` | `ManualTimeProvider` | `TimeProvider` with `Advance(TimeSpan)` and `SetUtcNow(DateTimeOffset)`. |
| `LiteBus.Testing.Mediation` | `TestCommandMediator` | Records `Commands`, returns `default` results, `Clear()`. |
| `LiteBus.Testing.Mediation` | `TestQueryMediator` | Records `Queries`, returns `NextResult` (settable), `Clear()`. |
| `LiteBus.Testing.Mediation` | `TestEventMediator` | Records `Events`, `Clear()`. |
| `LiteBus.Testing.Transport` | `TestMessageTransport` | `ITransportPublisher` double with `Published`, `NextPublishException` (single-shot), `IsDisconnected`, `Clear()`. |
| `LiteBus.Testing.Hosting` | `LiteBusHostedServiceExtensions` | `StartLiteBusHostedServicesAsync`, `StopLiteBusHostedServicesAsync` (reverse order), `GetInboxProcessorHostedService`, `AssertBackgroundServices(provider, params Type[])`. |
| `LiteBus.Testing.DurableMessaging` | `InboxOutboxStoreServiceCollectionExtensions` | `AddInboxStoreRoles<TStore>(store)` / `AddOutboxStoreRoles<TStore>(store)` register one instance under all ten role interfaces. |
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

### 4.24 Roslyn analyzers

Ship `LiteBus.Analyzers` as an analyzer package reference. Rules with the `CompilationEnd` tag only report after the whole compilation is analyzed.

| Id | Title | Category | Severity | Enabled | Message shape |
| --- | --- | --- | --- | --- | --- |
| `LB1001` | Duplicate command handler | `LiteBus.Handlers` | Error | Yes (CompilationEnd) | Command type has more than one command handler. |
| `LB1003` | Query handler impurity | `LiteBus.Handlers` | Warning | Yes | Query handler depends on a command, event, inbox or outbox API. |
| `LB1004` | Command with result scheduled to inbox | `LiteBus.Inbox` | Error | Yes | `ICommand<TResult>` cannot be stored through `IInbox.AcceptAsync` / `AcceptBatchAsync`. |
| `LB1005` | Unsupported open generic handler | `LiteBus.Handlers` | Error | Yes | Open generic handler must expose exactly one type parameter. |
| `LB1007` | Missing message contract registration | `LiteBus.Contracts` | Warning | Yes (CompilationEnd) | Handled message type has no durable contract registration. |
| `LB1008` | Missing command handler | `LiteBus.Handlers` | Error | Yes (CompilationEnd) | Command type has no command handler. |
| `LB1009` | Missing query handler | `LiteBus.Handlers` | Error | Yes (CompilationEnd) | Query type has no query handler. |
| `LB1010` | Duplicate query handler | `LiteBus.Handlers` | Error | Yes (CompilationEnd) | Query type has more than one query handler. |
| `LB1011` | Orphan handler tag | `LiteBus.Handlers` | Warning | Yes (CompilationEnd) | A handler tag no mediation filter references. |
| `LB1012` | Duplicate handler across assemblies | `LiteBus.Handlers` | Warning | Yes (CompilationEnd) | Same handler name in two assemblies, so `RegisterFromAssembly` may register both. |
| `LB1013` | Transactional outbox without DbContext | `LiteBus.Outbox` | Warning | Yes | Injects `ITransactionalOutboxStore` without a `DbContext` in the same constructor. |
| `LB1014` | Processor enabled without dispatcher | `LiteBus.Configuration` | Error | Yes | `Enable{Inbox,Outbox}Processor` with no dispatcher registration in the same configuration scope. |
| `LB1015` | Transactional storage without interceptor | `LiteBus.Configuration` | Warning | Yes | `EnforceTransactionalSetup()` without `EnableSaveChangesInterceptor()`. |
| `LB1016` | Transactional inbox without DbContext | `LiteBus.Inbox` | Warning | Yes | Injects `ITransactionalInboxStore` without a `DbContext`. |
| `LB1017` | Explicit message contract registration recommended | `LiteBus.Contracts` | Warning | Yes (CompilationEnd) | `[MessageContract]` present but no explicit registration. |
| `LB1018` | Message states no audit position | `LiteBus.Auditing` | Warning | **No** (opt in with `dotnet_diagnostic.LB1018.severity = warning`) | Neither `[Audited]` nor `[AuditExempt]` nor an `IAuditDefinition`. |
| `LB1019` | Untyped shortcut on a message that produces a result | `LiteBus.Handlers` | Warning | Yes | Untyped shortcut contract used for a result-producing message; answering would throw `LiteBusConfigurationException`. |
| `LB1020` | Message states no position on a required declaration | `LiteBus.Declarations` | Warning | **No** (opt in with `dotnet_diagnostic.LB1020.severity = warning`) | The message declares none of the value types named in `litebus_required_declarations` and records no exemption. Generalizes `LB1018`. |
| `LB1021` | Required declaration type not found | `LiteBus.Declarations` | Warning | Yes | A name in `litebus_required_declarations` does not resolve; reported rather than skipped, because skipping would silently disable the requirement. |

The analyzer package is held on Roslyn 4.x (`Microsoft.CodeAnalysis.CSharp` 4.14.0) so it loads on consumer SDKs.

### 4.25 Exception catalog

| Exception | Namespace | Thrown when |
| --- | --- | --- |
| `NoHandlerFoundException` | `LiteBus.Messaging.Abstractions` | No main handler for a message (or an unresolvable descriptor with `RegisterPlainMessagesOnSpot` off, or an event with `ThrowIfNoHandlerFound`). Carries `MessageType`. Never routed to error handlers. |
| `MultipleHandlerFoundException` | same | Single-handler mediation found more than one. Carries `MessageType` and `HandlerTypes`. |
| `AmbiguousMessageResolveException` | same | Two equally specific assignable descriptors. Carries `MessageType`, `ResolveStrategyType`. |
| `MessageDescriptorNotFoundException` | same | Descriptor still missing after on-the-spot registration. Carries `MessageType`, `ResolveStrategyType`, `RegisterPlainMessagesOnSpot`, `RegisteredMessageCount`. |
| `UnsupportedOpenGenericHandlerException` | same | Open generic handler with an arity other than one. Carries `HandlerType`, `GenericParameterCount`. |
| `LiteBusMessageDeniedException` | same | A guard denied and no refusal mapper applied. Carries `MessageType?`, `Reason?`, `Code?`. |
| `LiteBusMessageInvalidException` | same | Validators reported failures and no refusal mapper applied. Carries `MessageType?`, `Failures`. |
| `NoExecutionContextException` | same | Ambient execution context accessed outside a mediation. |
| `HandleContextDataNotFoundException` | same | `IExecutionContext.Data.Get<T>()` asked for a type no stage stored. Carries `DataType`. Use `TryGet<T>` where the value is optional. |
| `MessageMetadataNotFoundException` | same | `IMessageMetadataAccessor` asked about a type the registry does not hold. Carries `MessageType`. |
| `MessageContractNotRegisteredException` | same | Contract lookup failed by type or by `(name, version)`. |
| `MessageContractAlreadyRegisteredException` | same | Conflicting contract registration. |
| `MessageContractMismatchException` | same | Explicit registration disagrees with `[MessageContract]`. Carries `MessageType`. |
| `MessageSerializationException` | same (`Serialization`) | Payload could not be serialized or deserialized. Carries `MessageType?`, `ContractName?`, `ContractVersion?`, `Operation`. |
| `NotResolvedException` | `LiteBus.Messaging.Exceptions` | Legacy service-resolution failure. |
| `LiteBusConfigurationException` | `LiteBus.Runtime.Abstractions.Exceptions` | Any composition or declaration error (duplicate module, cycle, missing context, missing storage/dispatcher, audit trail missing, refusal mapper conflict, metadata conflict, untyped-shortcut misuse, ...). |
| `LiteBusDependencyResolutionException` | same | Required service missing from the container. Carries `ServiceType`. |
| `LiteBusNotSupportedException` | same | Registering a type that is not a construct of the axis being configured. |
| `LiteBusTimeoutException` | same | A LiteBus operation exceeded its configured time limit. |
| `DiagnosticCheckNameMismatchException` | `LiteBus.Runtime.Abstractions.Diagnostics` | `IDiagnosticCheck.Name` differs from the manifest descriptor. |
| `IdempotencyConflictException` | `LiteBus.Messaging.Abstractions.DurableMessaging` | `IdempotencyConflictMode.Strict` and a duplicate key or id. |
| `InboxStorageException` / `OutboxStorageException` | `LiteBus.Inbox.Abstractions.Exceptions` / `LiteBus.Outbox.Storage.InMemory` | Store rejected a write (including in-memory capacity). |
| `InboxDispatchException` | `LiteBus.Inbox.Abstractions.Exceptions` | Inbox dispatch or ingress could not accept or route a message. |
| `InboxIngressException` | same | Ingress could not map, authorize or accept a broker delivery. |
| `InboxManagementException` / `OutboxManagementException` | `LiteBus.Inbox.Abstractions` / `LiteBus.Outbox.Abstractions` | Operator safety rule violated (unconfirmed unrestricted purge). |
| `LiteBusDispatchException` | `LiteBus.Outbox.Abstractions.Exceptions` | Outbox dispatch could not publish or replay a leased envelope. |
| `InboxPostgreSqlStorageConfigurationException` | `LiteBus.Inbox.Storage.PostgreSql.Exceptions` | No PostgreSQL data source configured. |
| `EfCoreStorageNotSupportedException` | `LiteBus.Storage.EntityFrameworkCore.Exceptions` | Helper does not support the current EF Core provider. |
| `SagaConcurrencyException` | `LiteBus.Saga.Abstractions` | Optimistic saga save detected a concurrent update. Carries `Correlation`. |
| `TransportHeaderMappingException` | `LiteBus.Transport.Abstractions` | Required LiteBus transport header missing or invalid. Carries `HeaderName?`. |
| `TransportCircuitBreakerOpenException` | `LiteBus.Transport` | Broker operation rejected while the circuit is open. |
| `InvalidOperationException` | - | Re-enumerating a mediated stream; storing an `ICommand<TResult>` in the inbox; an envelope transition from the wrong status; starting an already-started in-memory consumer; batch append result count mismatch. |

---

## 5. Enum & Constants Catalog

### 5.1 Public enums

#### `MediationOutcome` (`LiteBus.Messaging.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Succeeded` | `0` | The main handler and all post-handlers ran without raising. |
| `Answered` | `1` | A shortcut answered the message, so the main handler never ran. |
| `Denied` | `2` | A guard refused the message; the main handler never ran. |
| `Failed` | `3` | The pipeline raised an exception other than cancellation or a refusal. |
| `Canceled` | `4` | The mediation cancellation token fired. |
| `Invalid` | `5` | A validator reported the message malformed; the main handler never ran. |

#### `PreStage` (`LiteBus.Messaging.Abstractions`) - declaration order **is** execution order

| Member | Value | Meaning |
| --- | --- | --- |
| `Guard` | `0` | May the message proceed. Stops at the first denial. |
| `Validator` | `1` | Is the message well-formed. Every validator runs; failures are collected. |
| `Shortcut` | `2` | Is the answer already known. Stops at the first answer. |
| `PreHandler` | `3` | Prepare the message. Cannot stop the pipeline by returning. |

#### `MessageErrorOutcome` (`LiteBus.Messaging.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Unhandled` | `0` | Default. The pipeline rethrows the original exception after error handlers run. |
| `Handled` | `1` | The pipeline suppresses the exception and may return `HandledResult`. |

#### `ParallelFaultMode` (`LiteBus.Messaging.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `PropagateFirst` | `0` | `Task.WhenAll`: one failure propagates once already-started siblings settle. |
| `AggregateAll` | `1` | Every handler runs to completion; failures are aggregated (single exception rethrown as-is, several wrapped in `AggregateException`). |

#### `AuditOutcome` (`LiteBus.Messaging.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Succeeded` | `0` | The action completed. |
| `Denied` | `1` | The action was refused before it took effect. |
| `Failed` | `2` | The action was permitted but did not complete. |
| `Canceled` | `3` | The action was cancelled before it completed. |
| `Invalid` | `4` | The action was rejected because its input failed validation. |

#### `ConcurrencyMode` (`LiteBus.Events.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Sequential` | `0` | Execute one after another; deterministic order. This is the default. |
| `Parallel` | `1` | Execute concurrently; non-deterministic order. |

#### `RetryBackoff` (`LiteBus.Messaging.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Fixed` | `0` | Every retry uses `InitialDelay` before jitter. |
| `Exponential` | `1` | Delay grows as `InitialDelay * 2^(attemptCount-1)` before the `MaxDelay` cap and jitter. |

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
| `Running` | `0` | Actively leasing and dispatching (or publishing). |
| `Paused` | `1` | Suspended; no new passes start. |
| `Draining` | `2` | Finishing one final pass before stopping. |

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
| `RequireActiveTransaction` | `0` | Throw when `IPostgreSqlTransactionProvider.TryGetCurrent` returns false. This is the default for ambient registration. |
| `AllowImmediateCommit` | `1` | Fall back to the auto-commit singleton store when no ambient transaction exists. Development and tests only; it breaks atomicity between domain and messaging writes. |

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

Trace-context column types by provider: PostgreSQL `jsonb`, SQL Server `nvarchar(max)`, MySQL `json`, SQLite `TEXT`, EF in-memory `TEXT`. The payload column is always `TEXT`. `ExecuteUpdate` bulk paths are skipped for the in-memory and SQLite providers.

#### `TransportConsumerInvocationOutcome` (`LiteBus.Transport.Abstractions`)

| Member | Value | Meaning |
| --- | --- | --- |
| `Handled` | `0` | The handler completed without the invoker settling the delivery. |
| `Requeued` | `1` | The invoker returned the delivery to the broker after a handler failure. |

### 5.2 Internal enums (not part of the public API; documented because they govern behaviour)

| Enum | Members | Meaning |
| --- | --- | --- |
| `PipelineFamily` (`LiteBus.Messaging.Abstractions`) | `PreStage = 0`, `PostHandler = 1`, `CompletionHandler = 2`, `RefusalMapper = 3` | Groups dispatchable contracts by call shape. `RefusalMapper` is the one family that is not a pipeline stage: it runs on the refusal path in place of raising. |
| `StageAggregation` (`LiteBus.Messaging.Abstractions`) | `StopAtFirst = 0`, `CollectFailures = 1` | `CollectFailures` is used only by `PreStage.Validator`. |
| `MetadataSourceKind` (`LiteBus.Messaging.Registry`) | `Attribute = 0`, `Definition = 1` | Higher wins for the same declaring message type, so a definition overrides an attribute. |
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
| `UnitOfWork` | `int` | `2000000` (`ReservedCeiling`) |

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

Internal: persisted error text is truncated to 1024 characters.

#### `AuditTrailDiagnosticCheck` (`LiteBus.Messaging.Audit`)

| Constant | Type | Value |
| --- | --- | --- |
| `CheckName` | `string` | `litebus.audit.trail` |

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

#### `TransportMessagingSystems` (`LiteBus.Transport`)

| Constant | Type | Value |
| --- | --- | --- |
| `AmazonSqs` | `string` | `aws_sqs` |
| `Kafka` | `string` | `kafka` |
| `LiteBusInMemory` | `string` | `litebus_in_memory` |
| `Other` | `string` | `litebus` |
| `RabbitMq` | `string` | `rabbitmq` |

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
| `BrokerTagName` | `string` | `litebus.transport.broker` |

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

Same constant names with `LiteBus.Outbox` for the source and meter names and `litebus.outbox.*` instrument names (`queue.depth`, `processor.state`, `processor.lease_lost`, `processor.persist_skipped`, `cleanup.errors`, `processor.dispatch_duration`, `processor.leases_acquired`, `processor.persist_rejected`, `diagnostics.unavailable`, `processor.persist_failed`), and `QueueStatusAttributeName` = `litebus.outbox.status`.

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

`LB1001` DuplicateCommandHandler, `LB1003` QueryHandlerImpurity, `LB1004` CommandWithResultScheduledToInbox, `LB1005` UnsupportedOpenGenericHandler, `LB1007` MissingMessageContractRegistration, `LB1008` MissingCommandHandler, `LB1009` MissingQueryHandler, `LB1010` DuplicateQueryHandler, `LB1011` OrphanHandlerTag, `LB1012` DuplicateHandlerAcrossAssemblies, `LB1013` TransactionalOutboxWithoutDbContext, `LB1014` ProcessorEnabledWithoutDispatcher, `LB1015` TransactionalStorageWithoutInterceptor, `LB1016` TransactionalInboxWithoutDbContext, `LB1017` ExplicitMessageContractRegistration, `LB1018` MissingAuditDeclaration, `LB1019` UntypedShortcutOnResultMessage, `LB1020` MissingDeclaration, `LB1021` UnresolvedRequiredDeclaration. (There is no `LB1002` or `LB1006`.)

#### Log event ids

| EventId | Name | Level | Source |
| --- | --- | --- | --- |
| `1101` | `CleanupFailed` | Error | inbox / outbox retention cleanup |
| `3001` | `LeasedBatch` | Debug | shared processor |
| `3002` | `LeaseRenewalFailed` | Warning | shared processor |
| `3002` | `IngressRestarting` | Warning | transport inbox ingress |
| `3003` | `TerminalPersistenceFailed` | Error | shared processor |
| `3003` | `BatchFlushFailed` | Error | transport inbox ingress |
| `3004` | `AckFailedAfterAccept` | Error | transport inbox ingress |
| `4001` | `InvalidManagementRequest` | Warning | ASP.NET Core management endpoints |
| `4002` | `ManagementSafetyRejection` | Warning | ASP.NET Core management endpoints |
| `5001` | `ManagementOperationFailed` | Error | ASP.NET Core management endpoints |

#### Other well-known string values

| Value | Where |
| --- | --- |
| `"inbox"` / `"outbox"` | `PayloadProtectionContext.Axis` |
| `"application/json"` | default `ContentType` on `TransportPublishRequest`, `TransportInboxDispatcherOptions`, `TransportOutboxDispatcherOptions` |
| `"litebus"` | default `LiteBusManagementOptions.RoutePrefix`, default health-check name |
| `"litebus.probes"` | synthetic probe name emitted when the manifest has no probes and `FailHealthWhenNoProbes` is true |
| `"litebus_inbox_messages"`, `"litebus_outbox_messages"`, `"litebus_saga_instances"`, `"litebus_schema_versions"` | default table names |
| `"public"` | default schema and metadata-schema name |
| `"litebus-transport"` | default `KafkaTransportOptions.ConsumerGroupId` |
| `"localhost"`, `"/"`, `"guest"` | AMQP default host, virtual host, user and password |
| `U+001F` | separator inside `DurableIdempotencyScope.CreateScopeKey` |
| `IX_LiteBus_Inbox_CreatedAt`, `IX_LiteBus_Outbox_CreatedAt` | EF Core `created_at` index names |

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

Note: a command accepted into the inbox must be a **void** `ICommand`. An `ICommand<TResult>` throws `InvalidOperationException` from `InboxEnvelopeFactory` and is reported at compile time by `LB1004`.

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
        ParallelFaultMode = ParallelFaultMode.AggregateAll
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
    foreach (var failure in invalid.Failures)
    {
        logger.LogWarning("invalid {Member}: {Message} ({Code})", failure.Member, failure.Message, failure.Code);
    }
}
catch (LiteBusMessageDeniedException denied)
{
    logger.LogWarning("denied {MessageType}: {Reason} ({Code})", denied.MessageType?.Name, denied.Reason, denied.Code);
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

Equivalent HTTP calls once `AddLiteBusManagementEndpoints()` is mapped:

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

(The same routes exist under `/litebus/outbox`.)

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

Substituting mediators in a unit test:

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

Simulating lease loss mid-dispatch:

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

A custom diagnostic probe registered against an axis:

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

A `Name` that differs from the registered descriptor name throws `DiagnosticCheckNameMismatchException` when the probe runs.
