# Dependency Graph

LiteBus v6 splits packages by concern: runtime, messaging, semantic modules, durable orchestration, storage adapters, dispatch adapters, ingress adapters, and shared transport helpers. Applications reference only the packages they compose.

v6.0 rename tables (`UseInProcessDispatch`, `*.AwsSqs` package IDs, EF Core public surface, and others) are documented in [Migration Guide v6](../migration/v6.md).

## Layer Assignment

| Layer | Number | Packages |
|---|---|---|
| Platform contracts | 0 | `Runtime.Abstractions`, `Transport.Abstractions` |
| Domain abstractions | 1 | `Messaging.Abstractions`, `Commands.Abstractions`, `Events.Abstractions`, `Queries.Abstractions`, `Inbox.Abstractions`, `Outbox.Abstractions`, `Orchestration.Abstractions`, `Saga.Abstractions` |
| Core implementations | 2 | `Runtime`, `Messaging`, `Commands`, `Events`, `Queries`, `Inbox`, `Outbox`, `Saga`, `Transport`, `Transport.Amqp`, `Transport.AzureServiceBus`, `Transport.AwsSqs`, `Transport.InMemory`, `Transport.Kafka` |
| Shared storage infrastructure | 3 | `Storage.PostgreSql`, `Storage.EntityFrameworkCore` |
| Integration adapters | 4 | `Inbox.Storage.*`, `Inbox.Dispatch.*`, `Inbox.Ingress.*`, `Outbox.Storage.*`, `Outbox.Dispatch.*`, `Saga.Storage.*` |
| Hosting / composition | 5 | `Runtime.Extensions.Microsoft.DI`, `Runtime.Extensions.Autofac`, `Runtime.Extensions.*.Hosting`, `*.Extensions.Microsoft.DependencyInjection`, `*.Extensions.Autofac`, `Extensions.Microsoft.DependencyInjection`, `Extensions.Diagnostics.HealthChecks`, `Saga.InboxIntegration`, `Inbox.Extensions.OpenTelemetry`, `Outbox.Extensions.OpenTelemetry`, `Transport.Extensions.OpenTelemetry`, `Extensions.AspNetCore`, `LiteBus.Testing` |

Only the `LiteBus` aggregate meta-package (NuGet ID `LiteBus`, assembly `LiteBus`) bundles core mediators and durable orchestration. The module registry and host manifest live in `LiteBus.Runtime` (NuGet ID `LiteBus.Runtime`). Do not confuse the meta-package with the runtime assembly. Storage, dispatch, ingress, transport brokers, and OpenTelemetry packages remain opt-in.

## Layer Map

```text
Runtime.Abstractions
  -> Runtime
    -> Runtime.Extensions.Microsoft.DependencyInjection (defines AddLiteBus; namespace LiteBus.Extensions.Microsoft.DependencyInjection)
    -> Runtime.Extensions.Autofac

Transport.Abstractions (no LiteBus project references)

Messaging.Abstractions -> Runtime.Abstractions
  -> Messaging
    -> Messaging.Extensions.Microsoft.DependencyInjection
    -> Messaging.Extensions.Autofac

Commands.Abstractions -> Messaging.Abstractions
Queries.Abstractions  -> Messaging.Abstractions
Events.Abstractions   -> Messaging.Abstractions

Orchestration.Abstractions -> Messaging.Abstractions

Inbox.Abstractions -> Messaging.Abstractions, Runtime.Abstractions
Outbox.Abstractions -> Messaging.Abstractions, Runtime.Abstractions

Inbox -> Inbox.Abstractions, Messaging, Orchestration.Abstractions, Runtime.Abstractions
Outbox -> Outbox.Abstractions, Messaging, Orchestration.Abstractions, Runtime.Abstractions

Saga -> Orchestration.Abstractions (SagaProcessorHook implements IProcessorEnvelopeHook)
Saga.InboxIntegration -> Saga, Inbox.Abstractions (EnableSaga builder extension)
Saga.Storage.PostgreSql -> Saga, Saga.Abstractions, Storage.PostgreSql, Inbox (IRequires ordering only)

Storage.PostgreSql (shared PG helpers)
  -> Inbox.Storage.PostgreSql
  -> Outbox.Storage.PostgreSql

Transport.Abstractions -> Transport (circuit breaker, header parsing)
  -> Transport.Amqp / Transport.AzureServiceBus / Transport.AwsSqs / Transport.InMemory / Transport.Kafka
     (each transport broker module -> Runtime.Abstractions for IModule registration)

Inbox.Dispatch.InProcess / Inbox.Dispatch (shared) / Inbox.Dispatch.* -> Messaging (PayloadProtection, trace helpers)
Outbox.Dispatch.InProcess / Outbox.Dispatch (shared) / Outbox.Dispatch.* -> Messaging (PayloadProtection, trace helpers)
Inbox.Dispatch.Amqp -> Inbox.Dispatch, Transport.Amqp
Inbox.Dispatch.AzureServiceBus -> Inbox.Dispatch, Transport.AzureServiceBus
Inbox.Dispatch.AwsSqs -> Inbox.Dispatch, Transport.AwsSqs
Inbox.Dispatch.Kafka -> Inbox.Dispatch, Transport.Kafka
Inbox.Dispatch.InMemory -> Inbox.Dispatch, Transport.InMemory
Outbox.Dispatch.Amqp -> Outbox.Dispatch, Transport.Amqp
Outbox.Dispatch.AzureServiceBus -> Outbox.Dispatch, Transport.AzureServiceBus
Outbox.Dispatch.AwsSqs -> Outbox.Dispatch, Transport.AwsSqs
Outbox.Dispatch.Kafka -> Outbox.Dispatch, Transport.Kafka
Outbox.Dispatch.InMemory -> Outbox.Dispatch, Transport.InMemory
Inbox.Ingress -> Inbox.Abstractions, Messaging.Abstractions, Runtime.Abstractions, Transport.Abstractions, Transport
Inbox.Ingress.* -> Inbox.Abstractions, Inbox.Ingress, Messaging.Abstractions, Runtime.Abstractions, matching Transport.* (no Inbox core reference)

Extensions.Microsoft.DependencyInjection -> semantic *.Extensions.Microsoft.DependencyInjection packages (convenience meta-package; no API surface)
Runtime.Extensions.Microsoft.DependencyInjection -> Runtime, Runtime.Extensions.Microsoft.Hosting, Messaging (defines AddLiteBus)
Extensions.Diagnostics.HealthChecks -> Runtime.Abstractions
Extensions.AspNetCore -> Inbox.Abstractions, Outbox.Abstractions, Runtime.Abstractions

Runtime.Extensions.Hosting -> Runtime.Abstractions
Runtime.Extensions.Microsoft.Hosting / Runtime.Extensions.Autofac.Hosting -> Runtime.Extensions.Hosting

LiteBus.Analyzers (Roslyn, no runtime dependency)

LiteBus.Testing -> Commands, Events, Queries, Inbox, Outbox, Messaging, Runtime hosting extensions (published test harness; not an IModule)
```

Semantic modules and durable core packages depend on Messaging and Runtime.Abstractions. Storage packages depend on abstractions and their store SDK. Dispatch packages depend on abstractions and their execution target (Messaging core when payload protection or trace helpers are required). Ingress packages depend on `Inbox.Abstractions`, `Messaging.Abstractions`, `Runtime.Abstractions`, shared `Transport` header mapping, and broker-specific `Transport.*` modules; broker ingress packages do not reference the inbox core assembly because child modules are registered through `InboxModuleBuilder` composite ordering.

## Runtime.Abstractions Edges

`Runtime.Abstractions` is layer 0. Packages below reference it directly for `IModule`, `IModuleConfiguration`, `DependencyDescriptor`, and related composition contracts. `Transport.Abstractions` does **not** reference `Runtime.Abstractions` (transport contracts are broker-neutral).

| Package | Why `Runtime.Abstractions` |
| --- | --- |
| `Messaging.Abstractions` | Handler pipeline settings, execution context, module-neutral mediation contracts |
| `Inbox.Abstractions` | `InboxModuleBuilder`, store role modules, diagnostic registration |
| `Outbox.Abstractions` | `OutboxModuleBuilder`, store role modules, diagnostic registration |
| `Commands`, `Events`, `Queries`, `Messaging`, `Inbox`, `Outbox`, `Saga` | Core `IModule` implementations and `Build()` registration |
| `*.Storage.*` | Storage sub-module `IModule` implementations |
| `*.Dispatch.*` | Dispatcher sub-module `IModule` implementations |
| `Transport.Amqp`, `Transport.InMemory`, `Transport.Kafka`, `Transport.AzureServiceBus`, `Transport.AwsSqs` | Transport `IModule` registration |
| `Transport` | Transport metrics module registration (`TransportMetricsRegistration`) |
| `Runtime.Extensions.Microsoft.Hosting`, `Runtime.Extensions.Autofac.Hosting` | `IBackgroundService` / `IStartupTask` manifest bridging |
| `Extensions.AspNetCore` | Management endpoints read `LiteBusHostManifest` |
| `Extensions.Diagnostics.HealthChecks` | Health check reads manifest diagnostic descriptors |
| `Saga.Storage.PostgreSql` | PostgreSQL saga storage module registration |

Orchestration and saga abstractions do **not** reference `Runtime.Abstractions` directly; saga core and inbox/outbox cores pull runtime composition types where needed.

## v6 Package Table

| Package | Role | Depends on | External |
| --- | --- | --- | --- |
| `LiteBus.Runtime.Abstractions` | DI-neutral module registration and shared W3C trace context parsing | none | none |
| `LiteBus.Runtime` | Module registry, dependency descriptors, `ILiteBusBuilder` composition surface | `Runtime.Abstractions`, `Messaging`, `Messaging.Abstractions` | none |
| `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | Minimal Microsoft DI adapter; defines `AddLiteBus` in namespace `LiteBus.Extensions.Microsoft.DependencyInjection` | `Runtime`, `Runtime.Extensions.Microsoft.Hosting`, `Messaging`, `Messaging.Abstractions` | `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `LiteBus.Runtime.Extensions.Autofac` | Autofac adapter | `Runtime`, `Runtime.Extensions.Autofac.Hosting` | `Autofac` |
| `LiteBus.Extensions.Microsoft.DependencyInjection` | Convenience meta-package for every semantic mediator Microsoft DI extension (no source files; does not define `AddLiteBus`) | `Commands`, `Events`, `Queries`, `Messaging` mediator `*.Extensions.Microsoft.DependencyInjection` packages | none |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | ASP.NET Core `IHealthCheck` adapter for manifest diagnostics | `Runtime.Abstractions` | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| `LiteBus.Extensions.AspNetCore` | Management HTTP endpoints (`/litebus/*`) | `Inbox.Abstractions`, `Outbox.Abstractions`, `Runtime.Abstractions` | ASP.NET Core |
| `LiteBus.Messaging.Abstractions` | Handler contracts, mediation, execution context | `Runtime.Abstractions` | none |
| `LiteBus.Messaging` | Registry, mediators, serializer, contract registry | `Messaging.Abstractions`, `Runtime.Abstractions` | BCL `System.Text.Json` |
| `LiteBus.Commands.Abstractions` | Command contracts and handlers | `Messaging.Abstractions` | none |
| `LiteBus.Commands` | Command mediator and module | `Commands.Abstractions`, `Messaging`, `Runtime.Abstractions` | none |
| `LiteBus.Queries.Abstractions` | Query and stream query contracts | `Messaging.Abstractions` | none |
| `LiteBus.Queries` | Query mediator and module | `Queries.Abstractions`, `Messaging`, `Runtime.Abstractions` | none |
| `LiteBus.Events.Abstractions` | Event contracts and handlers | `Messaging.Abstractions` | none |
| `LiteBus.Events` | Event mediator and module | `Events.Abstractions`, `Messaging`, `Messaging.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Inbox.Abstractions` | Inbox contracts, envelopes, store roles, dispatcher contract | `Messaging.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Orchestration.Abstractions` | Axis-neutral processor envelope hooks (`IProcessorEnvelopeHook`) | `Messaging.Abstractions` | none |
| `LiteBus.Inbox` | `InboxWriter`, `InboxProcessor`, module, processor options | `Inbox.Abstractions`, `Messaging`, `Orchestration.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Inbox.Storage.PostgreSql` | Npgsql inbox store (all three store roles), optional `PostgreSqlInboxSchemaInitializer` | `Inbox.Abstractions`, `Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Inbox.Storage.EntityFrameworkCore` | EF Core inbox store | `Inbox.Abstractions` | EF Core |
| `LiteBus.Inbox.Storage.InMemory` | Thread-safe in-memory inbox store | `Inbox.Abstractions` | none |
| `LiteBus.Inbox.Dispatch.InProcess` | `UseInProcessDispatch` via `ICommandMediator` | `Inbox.Abstractions`, `Commands.Abstractions`, `Messaging` | none |
| `LiteBus.Inbox.Dispatch` | Shared `TransportInboxDispatcher` and envelope mapping | `Inbox.Abstractions`, `Messaging`, `Transport.Abstractions`, `Transport` | none |
| `LiteBus.Inbox.Dispatch.Amqp` | `UseAmqpDispatch`: AMQP transport inbox dispatch | `Inbox.Dispatch`, `Transport.Amqp` | none |
| `LiteBus.Inbox.Dispatch.AzureServiceBus` | `UseAzureServiceBusDispatch` | `Inbox.Dispatch`, `Transport.AzureServiceBus` | none |
| `LiteBus.Inbox.Dispatch.AwsSqs` | `UseAwsSqsDispatch` | `Inbox.Dispatch`, `Transport.AwsSqs` | none |
| `LiteBus.Inbox.Dispatch.Kafka` | `UseKafkaDispatch` | `Inbox.Dispatch`, `Transport.Kafka` | none |
| `LiteBus.Inbox.Dispatch.InMemory` | `UseInMemoryDispatch`: in-memory transport inbox dispatch | `Inbox.Dispatch`, `Transport.InMemory` | none |
| `LiteBus.Inbox.Ingress` | Map transport deliveries to `IInbox.AcceptAsync` | `Inbox.Abstractions`, `Messaging.Abstractions`, `Runtime.Abstractions`, `Transport.Abstractions`, `Transport` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Inbox.Ingress.Amqp` | `UseAmqpIngress`: AMQP inbox ingress | `Inbox.Abstractions`, `Inbox.Ingress`, `Messaging.Abstractions`, `Runtime.Abstractions`, `Transport.Amqp` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Inbox.Ingress.AzureServiceBus` | `UseAzureServiceBusIngress` | `Inbox.Abstractions`, `Inbox.Ingress`, `Messaging.Abstractions`, `Runtime.Abstractions`, `Transport.AzureServiceBus` | none |
| `LiteBus.Inbox.Ingress.AwsSqs` | `UseAwsSqsIngress` | `Inbox.Abstractions`, `Inbox.Ingress`, `Messaging.Abstractions`, `Runtime.Abstractions`, `Transport.AwsSqs` | none |
| `LiteBus.Inbox.Ingress.Kafka` | `UseKafkaIngress` | `Inbox.Abstractions`, `Inbox.Ingress`, `Messaging.Abstractions`, `Runtime.Abstractions`, `Transport.Kafka` | none |
| `LiteBus.Inbox.Ingress.InMemory` | `UseInMemoryIngress` | `Inbox.Abstractions`, `Inbox.Ingress`, `Messaging.Abstractions`, `Runtime.Abstractions`, `Transport.InMemory` | none |
| `LiteBus.Outbox.Abstractions` | Outbox contracts, envelopes, store roles, dispatcher contract | `Messaging.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Outbox` | `OutboxWriter`, `OutboxProcessor`, module | `Outbox.Abstractions`, `Messaging`, `Orchestration.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Saga.Abstractions` | Saga instance contracts and store interfaces | `Commands.Abstractions`, `Messaging.Abstractions` | none |
| `LiteBus.Saga` | `SagaProcessorHook`, in-memory store, `SagaModule` | `Saga.Abstractions`, `Orchestration.Abstractions`, `Messaging.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Saga.InboxIntegration` | `EnableSaga()` on `InboxModuleBuilder` | `Saga`, `Inbox.Abstractions` | none |
| `LiteBus.Saga.Storage.PostgreSql` | PostgreSQL saga store and schema | `Saga`, `Saga.Abstractions`, `Storage.PostgreSql`, `Inbox`, `Messaging.Abstractions`, `Runtime.Abstractions` | `Npgsql` |
| `LiteBus.Outbox.Storage.PostgreSql` | Npgsql outbox store, optional `PostgreSqlOutboxSchemaInitializer` | `Outbox.Abstractions`, `Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | EF Core outbox store | `Outbox.Abstractions` | EF Core |
| `LiteBus.Outbox.Storage.InMemory` | Thread-safe in-memory outbox store | `Outbox.Abstractions` | none |
| `LiteBus.Outbox.Dispatch.InProcess` | `UseInProcessDispatch` via `IEventMediator` | `Outbox.Abstractions`, `Events.Abstractions`, `Messaging` | none |
| `LiteBus.Outbox.Dispatch` | Shared `TransportOutboxDispatcher` and envelope mapping | `Outbox.Abstractions`, `Messaging`, `Transport.Abstractions`, `Transport` | none |
| `LiteBus.Outbox.Dispatch.Amqp` | `UseAmqpDispatch`: AMQP transport outbox dispatch | `Outbox.Dispatch`, `Transport.Amqp` | none |
| `LiteBus.Outbox.Dispatch.AzureServiceBus` | `UseAzureServiceBusDispatch` | `Outbox.Dispatch`, `Transport.AzureServiceBus` | none |
| `LiteBus.Outbox.Dispatch.AwsSqs` | `UseAwsSqsDispatch` | `Outbox.Dispatch`, `Transport.AwsSqs` | none |
| `LiteBus.Outbox.Dispatch.Kafka` | `UseKafkaDispatch` | `Outbox.Dispatch`, `Transport.Kafka` | none |
| `LiteBus.Outbox.Dispatch.InMemory` | `UseInMemoryDispatch`: in-memory transport outbox dispatch | `Outbox.Dispatch`, `Transport.InMemory` | none |
| `LiteBus.Transport.Abstractions` | `IMessageTransport`, `IMessageConsumer`, transport headers | none | none |
| `LiteBus.Transport` | Circuit breaker metrics, transport tracing, and header value parsing | `Transport.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Transport.Amqp` | RabbitMQ adapter | `Transport.Abstractions`, `Transport`, `Runtime.Abstractions` | `RabbitMQ.Client` |
| `LiteBus.Inbox.Extensions.OpenTelemetry` | Register inbox traces and metrics | `Inbox` | `OpenTelemetry` |
| `LiteBus.Outbox.Extensions.OpenTelemetry` | Register outbox traces and metrics | `Outbox` | `OpenTelemetry` |
| `LiteBus.Transport.Extensions.OpenTelemetry` | Register transport tracing and circuit breaker metrics | `Transport` | `OpenTelemetry` |
| `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` | Register AMQP circuit breaker metrics | `Transport.Amqp` | `OpenTelemetry` |
| `LiteBus.Storage.PostgreSql` | Shared PG quoting, schema version, advisory locks | none | `Npgsql` |
| `LiteBus.Analyzers` | Roslyn analyzers for handler and contract rules | Roslyn only | `Microsoft.CodeAnalysis.CSharp` |
| `LiteBus.*.Extensions.Microsoft.DependencyInjection` | Module registration for Microsoft DI | Module package, Microsoft DI runtime adapter | Microsoft DI |
| `LiteBus.Runtime.Extensions.Hosting` | Shared host orchestrator and manual background-service wrapper | `Runtime.Abstractions`, Microsoft hosting abstractions | none |
| `LiteBus.Runtime.Extensions.Microsoft.Hosting` | Microsoft DI registration for manifest host adapters | `Runtime.Extensions.Hosting`, Microsoft DI abstractions | none |
| `LiteBus.Runtime.Extensions.Autofac.Hosting` | Autofac registration for manifest host adapters | `Runtime.Extensions.Hosting`, Autofac | none |
| `LiteBus.*.Extensions.Autofac` | Module registration for Autofac | Module package, Autofac runtime adapter | Autofac |
| `LiteBus` | Aggregate meta-package (core modules; storage/dispatch remain opt-in) | Commands, Queries, Events, Messaging, Inbox, Outbox, abstractions | none |
| `LiteBus.Testing` | Published test harness: `Test*` mediators and stores, `InboxOutboxTestHost`, processor pass helpers | `Commands`, `Events`, `Queries`, `Inbox`, `Outbox`, `Messaging`, `Runtime.Abstractions`, `Runtime.Extensions.Microsoft.Hosting`, `Extensions.Microsoft.DependencyInjection`, in-memory storage packages | `AwesomeAssertions`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting.Abstractions`, `Newtonsoft.Json` |

## Dependency Rules

| From | May depend on | Must NOT depend on |
| --- | --- | --- |
| `*.Abstractions` (except `Transport.Abstractions`) | `Messaging.Abstractions` and, where noted below, `Runtime.Abstractions` | Commands, Events, Npgsql, EF, RabbitMQ, hosting |
| `Transport.Abstractions` | none | Any LiteBus project reference |
| `Inbox` / `Outbox` core | Abstractions, Messaging, Runtime.Abstractions | Commands, Events, Npgsql, EF, RabbitMQ, any Dispatch/Ingress |
| `*.Storage.*` | Abstractions, store SDK, `Storage.PostgreSql` when PG | Dispatch, Ingress, Commands, Events |
| `*.Dispatch.*` | Abstractions, axis core (`Inbox` / `Outbox` for `IRequires` ordering), `Messaging` when payload protection or trace helpers are required, `Transport.Abstractions` for transport dispatch | Storage, Ingress, broker SDKs |
| `*.Ingress.*` | `Inbox.Abstractions`, `Messaging.Abstractions`, `Runtime.Abstractions`, shared `Inbox.Ingress` and broker `Transport.*` (base `Inbox.Ingress` also references `Transport` for header mapping) | Inbox core, Storage, Dispatch |
| `LiteBus.Transport` | `Transport.Abstractions` | Inbox, Outbox, broker SDKs |
| `LiteBus.Transport.Amqp` | `Transport.Abstractions`, `Transport` | Inbox, Outbox abstractions |
| `*.Extensions.OpenTelemetry` | Matching core package (`Inbox`, `Outbox`, or `Transport`) | Storage, dispatch, ingress |
| `LiteBus.Analyzers` | Roslyn | runtime libraries |

Additional rules:

- Query packages do not depend on inbox or outbox packages.
- Core inbox and outbox modules register writers and processors only. They do not register dispatchers.
- Processor background services validate that exactly one dispatcher is registered.
- Stable contracts use names and versions, not assembly-qualified CLR names.
- Closed generic messages are supported when each closed type is registered. Open generic contracts are rejected.

## Registration Reference

### Naming Schemes (Folder, Namespace, NuGet)

| Layer | Example |
| --- | --- |
| Repository folder / project | `LiteBus.Inbox.Storage.PostgreSql` |
| C# namespace | `LiteBus.Inbox.Storage.PostgreSql` |
| NuGet package ID | `LiteBus.Inbox.Storage.PostgreSql` (set in `src/Directory.Build.props`; only the `LiteBus` meta-package keeps the `LiteBus` ID) |
| Extension holder class | `InboxModuleBuilderPostgreSqlExtensions` |

### `Use*` Extension to Package Map

| Extension | Package |
| --- | --- |
| `UseInMemoryStorage()` (inbox) | `LiteBus.Inbox.Storage.InMemory` |
| `UsePostgreSqlStorage(...)` (inbox) | `LiteBus.Inbox.Storage.PostgreSql` |
| `UseEntityFrameworkCoreStorage(...)` (inbox) | `LiteBus.Inbox.Storage.EntityFrameworkCore` |
| `UseInProcessDispatch()` (inbox) | `LiteBus.Inbox.Dispatch.InProcess` |
| `UseAmqpDispatch(...)` (inbox) | `LiteBus.Inbox.Dispatch.Amqp` |
| `UseAzureServiceBusDispatch(...)` (inbox) | `LiteBus.Inbox.Dispatch.AzureServiceBus` |
| `UseAwsSqsDispatch(...)` (inbox) | `LiteBus.Inbox.Dispatch.AwsSqs` |
| `UseKafkaDispatch(...)` (inbox) | `LiteBus.Inbox.Dispatch.Kafka` |
| `UseInMemoryDispatch(...)` (inbox) | `LiteBus.Inbox.Dispatch.InMemory` |
| `UseAmqpIngress(...)` | `LiteBus.Inbox.Ingress.Amqp` |
| `UseAzureServiceBusIngress(...)` | `LiteBus.Inbox.Ingress.AzureServiceBus` |
| `UseAwsSqsIngress(...)` | `LiteBus.Inbox.Ingress.AwsSqs` |
| `UseKafkaIngress(...)` | `LiteBus.Inbox.Ingress.Kafka` |
| `UseInMemoryIngress(...)` | `LiteBus.Inbox.Ingress.InMemory` |
| `EnableSaga(...)` | `LiteBus.Saga.InboxIntegration` |
| `UseInMemoryStorage()` (outbox) | `LiteBus.Outbox.Storage.InMemory` |
| `UsePostgreSqlStorage(...)` (outbox) | `LiteBus.Outbox.Storage.PostgreSql` |
| `UseEntityFrameworkCoreStorage(...)` (outbox) | `LiteBus.Outbox.Storage.EntityFrameworkCore` |
| `UseInProcessDispatch()` (outbox) | `LiteBus.Outbox.Dispatch.InProcess` |
| `UseAmqpDispatch(...)` (outbox) | `LiteBus.Outbox.Dispatch.Amqp` |
| `UseAzureServiceBusDispatch(...)` (outbox) | `LiteBus.Outbox.Dispatch.AzureServiceBus` |
| `UseAwsSqsDispatch(...)` (outbox) | `LiteBus.Outbox.Dispatch.AwsSqs` |
| `UseKafkaDispatch(...)` (outbox) | `LiteBus.Outbox.Dispatch.Kafka` |
| `UseInMemoryDispatch(...)` (outbox) | `LiteBus.Outbox.Dispatch.InMemory` |

### Core Mediators (`ILiteBusBuilder`)

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddCommandModule(c => c.RegisterFromAssembly(typeof(Program).Assembly));
    builder.Modules.AddQueryModule(q => q.RegisterFromAssembly(typeof(Program).Assembly));
    builder.Modules.AddEventModule(e => e.RegisterFromAssembly(typeof(Program).Assembly));
});
```

### Inbox Composition

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.Modules.AddInboxModule(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UseProcessorOptions(new InboxProcessorOptions { BatchSize = 50 });
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor();
    });
});
```

### Outbox Composition

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.Modules.AddOutboxModule(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.order-submitted", 1);
        outbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor();
    });
});
```

### Transport Dispatch and Ingress

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.Modules.AddOutboxModule(outbox =>
    {
        outbox.UseAmqpDispatch(
            options =>
            {
                options.DefaultDestination = "orders.events";
            },
            new AmqpConnectionOptions
            {
                Uri = new Uri(configuration.GetConnectionString("Amqp")!)
            });
    });

    builder.Modules.AddInboxModule(inbox =>
    {
        inbox.UseAmqpIngress(ingress =>
        {
            ingress.UseOptions(new AmqpInboxIngressOptions
            {
                QueueName = "commands.inbox",
                Connection = new AmqpConnectionOptions
                {
                    Uri = new Uri(configuration.GetConnectionString("Amqp")!)
                }
            });
        });
    });
});
```

`UseAmqpDispatch` embeds `AmqpTransportModule` in the dispatch child module and always builds it during dispatcher registration. `UseAmqpIngress` bootstraps `AmqpTransportModule` from `AmqpInboxIngressOptions.Connection` only when `IMessageConsumer` is not already registered. In a combined inbox host, dispatch child modules run before ingress children, so dispatch typically registers the shared AMQP consumer first.

## Test Dependencies

Test-only packages live under `tests/Directory.Packages.props`.

| Package | Used for |
| --- | --- |
| `xunit`, `xunit.runner.visualstudio` | Unit and integration test execution |
| `AwesomeAssertions` | Test assertions |
| `Microsoft.NET.Test.Sdk` | .NET test host |
| `coverlet.collector`, `coverlet.msbuild` | Coverage collection |
| `Testcontainers.PostgreSql` | PostgreSQL storage integration tests |
| `Testcontainers.RabbitMq` | AMQP dispatch and ingress integration tests |
| `Testcontainers.Redpanda`, `Testcontainers.Kafka` | Kafka ingress, dispatch, and outbox integration tests |
| `Testcontainers.LocalStack` | AWS SQS transport integration tests |
| `Npgsql` | PostgreSQL integration tests |

PostgreSQL and AMQP integration tests require Docker. CI skips them gracefully when Docker is unavailable.

## Next

See [Architecture](README.md) for Storage / Dispatch / Ingress flow diagrams, or [Migration Guide v6](../migration/v6.md) for v5 to v6 renames.
