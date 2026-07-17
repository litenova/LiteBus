# Dependency Graph

LiteBus v6 splits packages by concern: runtime, messaging, semantic modules, durable orchestration, storage adapters, dispatch adapters, ingress adapters, and shared transport helpers. Applications reference only the packages they compose.

v6.0 rename tables (`UseInProcessDispatch`, `*.AwsSqs` package IDs, EF Core public surface, and others) are documented in [Migration Guide v6](../migration/v6.md).

The role table below is the normative dependency summary. [Generated Package Inventory](generated-package-inventory.md) records every evaluated shipping project reference and direct package reference; documentation CI rejects stale inventory.

## Dependency Roles

Roles describe dependency direction and package purpose. They are not numeric layers. Feature axes remain independent from the role assigned to a package.

| Role | Package examples | Allowed project dependencies | Allowed direct package dependencies |
| --- | --- | --- | --- |
| Platform contracts | `Runtime.Abstractions`, `Transport.Abstractions` | Platform contracts | BCL only |
| Mediation contracts | `Messaging.Abstractions`, command/query/event abstractions | Platform and mediation contracts | BCL only |
| Durable contracts | `DurableMessaging.Abstractions`, inbox/outbox/saga abstractions | Platform, mediation, and durable contracts | BCL only |
| Core implementation | `Runtime`, `Messaging`, semantic mediators, `Inbox`, `Outbox`, `Saga`, `Transport` | Contract roles and core implementations | Logging abstractions only where operational logging is required |
| Technology adapter | `Storage.PostgreSql`, `Storage.EntityFrameworkCore`, `Transport.Amqp`, `Transport.Kafka`, `Transport.AwsSqs`, `Transport.AzureServiceBus` | Contract roles, core implementations, and technology adapters | One relevant persistence or broker SDK, plus logging abstractions |
| Feature bridge | `Inbox.Storage.*`, `Inbox.Dispatch.*`, `Inbox.Ingress.*`, `Outbox.Storage.*`, `Outbox.Dispatch.*`, `Saga.InboxIntegration`, `Saga.Storage.*` | Contract roles, core implementations, technology adapters, and feature bridges | The relevant adapter SDK, plus logging abstractions |
| Host adapter | DI, hosting, OpenTelemetry, health checks, ASP.NET Core | Any applicable lower role and other host adapters | The relevant host framework |
| Consumer tooling | `LiteBus.Analyzers`, `LiteBus.Testing` | Any role needed by the tool | Analyzer or test-support packages |
| Aggregate | `LiteBus` | Contract and core implementation roles | BCL only |

`ArchitectureDependencyPolicyTests` parses every project under `src/`. It fails when a project has no role, a project reference points in a forbidden direction, a storage, dispatch, or ingress adapter crosses concerns, or a direct or transitive package reference is not approved for that role. Technology-family checks walk the complete project-reference closure, so a broker SDK cannot enter an adapter through a shared project. Update the matrix and the test together when introducing a new architectural role or SDK family.

Only the `LiteBus` aggregate meta-package bundles core mediators and durable messaging. Storage, dispatch, ingress, broker transports, and OpenTelemetry remain opt-in.

## Role Map

```text
Platform contracts
  Runtime.Abstractions
    -> Runtime
    -> Messaging.Abstractions -> Messaging
    -> DurableMessaging.Abstractions -> Inbox / Outbox / Saga
  Transport.Abstractions -> Transport

Technology adapters
  Storage.PostgreSql / Storage.EntityFrameworkCore
  Transport.Amqp / Transport.Kafka / Transport.AwsSqs / Transport.AzureServiceBus / Transport.InMemory

Feature bridges
  Inbox.Storage.* / Inbox.Dispatch.* / Inbox.Ingress.*
  Outbox.Storage.* / Outbox.Dispatch.*
  Saga.InboxIntegration / Saga.Storage.*

Host adapters
  Runtime.Extensions.* / *.Extensions.Microsoft.DependencyInjection / *.Extensions.Autofac
  *.Extensions.OpenTelemetry / Extensions.AspNetCore / Extensions.Diagnostics.HealthChecks
```

`LiteBus.Runtime` depends only on `Runtime.Abstractions`. `ILiteBusBuilder` lives in `Runtime.Abstractions` and exposes only `Modules`; feature packages add the normal `AddMessaging`, `AddCommands`, `AddQueries`, `AddEvents`, `AddInbox`, and `AddOutbox` extensions. The underlying `IModuleRegistry` remains available for advanced composition.

Shared transports are root modules. Dispatch and ingress modules declare `IRequires<TTransportModule>` and never create an implicit broker module. Storage, dispatch, and ingress modules also declare their parent inbox or outbox requirement where the type system permits it. The module registry validates the complete graph before any module builds.

Microsoft DI and Autofac adapters each register one `IMessageDispatchScopeFactory`. Core packages depend only on that contract and fail composition when no factory is present. A manual host can opt into `RootMessageDispatchScopeFactory` explicitly. EF Core storage uses `IDbContextFactory<TContext>` through adapter-owned operation factories and does not create container scopes inside stores.

## v6 Package Table

| Package | Role | Depends on | External |
| --- | --- | --- | --- |
| `LiteBus.Runtime.Abstractions` | DI-neutral modules, `ILiteBusBuilder`, dispatch-scope contracts, and shared W3C trace context parsing | none | none |
| `LiteBus.Runtime` | Module registry, dependency descriptors, and advanced module composition | `Runtime.Abstractions` | none |
| `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | Microsoft DI adapter; defines `AddLiteBus` in namespace `LiteBus.Extensions.Microsoft.DependencyInjection` | `Runtime`, `Runtime.Extensions.Microsoft.Hosting` | `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `LiteBus.Runtime.Extensions.Autofac` | Autofac adapter | `Runtime`, `Runtime.Extensions.Autofac.Hosting` | `Autofac` |
| `LiteBus.Messaging.Extensions.Microsoft.DependencyInjection` | Microsoft DI mediator registration | `Messaging`, runtime DI adapter | Microsoft DI |
| `LiteBus.Messaging.Extensions.Autofac` | Autofac mediator registration | `Messaging`, runtime Autofac adapter | Autofac |
| `LiteBus.Commands.Extensions.Microsoft.DependencyInjection` | Microsoft DI command registration | `Commands`, runtime DI adapter | Microsoft DI |
| `LiteBus.Commands.Extensions.Autofac` | Autofac command registration | `Commands`, runtime Autofac adapter | Autofac |
| `LiteBus.Queries.Extensions.Microsoft.DependencyInjection` | Microsoft DI query registration | `Queries`, runtime DI adapter | Microsoft DI |
| `LiteBus.Queries.Extensions.Autofac` | Autofac query registration | `Queries`, runtime Autofac adapter | Autofac |
| `LiteBus.Events.Extensions.Microsoft.DependencyInjection` | Microsoft DI event registration | `Events`, runtime DI adapter | Microsoft DI |
| `LiteBus.Events.Extensions.Autofac` | Autofac event registration | `Events`, runtime Autofac adapter | Autofac |
| `LiteBus.Extensions.Microsoft.DependencyInjection` | Convenience meta-package for every semantic mediator Microsoft DI extension (no source files; does not define `AddLiteBus`) | `Commands`, `Events`, `Queries`, `Messaging` mediator `*.Extensions.Microsoft.DependencyInjection` packages | none |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | ASP.NET Core `IHealthCheck` adapter for manifest diagnostics | `Runtime.Abstractions` | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| `LiteBus.Extensions.AspNetCore` | Management HTTP endpoints (`/litebus/*`) | `Inbox.Abstractions`, `Outbox.Abstractions`, `Runtime.Abstractions` | ASP.NET Core |
| `LiteBus.Messaging.Abstractions` | Handler contracts, mediation, execution context | `Runtime.Abstractions` | none |
| `LiteBus.Messaging` | Registry, mediators, serializer, contract registry | `DurableMessaging.Abstractions`, `Messaging.Abstractions`, `Runtime.Abstractions` | logging abstractions |
| `LiteBus.Commands.Abstractions` | Command contracts and handlers | `Messaging.Abstractions` | none |
| `LiteBus.Commands` | Command mediator and module | `Commands.Abstractions`, `Messaging`, `Runtime.Abstractions` | none |
| `LiteBus.Queries.Abstractions` | Query and stream query contracts | `Messaging.Abstractions` | none |
| `LiteBus.Queries` | Query mediator and module | `Queries.Abstractions`, `Messaging`, `Runtime.Abstractions` | none |
| `LiteBus.Events.Abstractions` | Event contracts and handlers | `Messaging.Abstractions` | none |
| `LiteBus.Events` | Event mediator and module | `Events.Abstractions`, `Messaging`, `Messaging.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Inbox.Abstractions` | Inbox records, contracts, envelopes, store roles, and dispatcher contract | `DurableMessaging.Abstractions`, `Messaging.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.DurableMessaging.Abstractions` | Durable metadata, retry, leases, processor settings, and processor envelope hooks | `Messaging.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Inbox` | Inbox writer services, processor, module, and `InboxModuleBuilder` | `Inbox.Abstractions`, `Messaging`, `DurableMessaging.Abstractions`, `Runtime.Abstractions` | logging abstractions |
| `LiteBus.Inbox.Storage.PostgreSql` | Npgsql inbox store (all three store roles), optional `PostgreSqlInboxSchemaInitializer` | `Inbox.Abstractions`, `Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Inbox.Storage.EntityFrameworkCore` | EF Core inbox store using `IDbContextFactory<TContext>` operation contexts | `Inbox`, `Inbox.Abstractions`, `Storage.EntityFrameworkCore` | EF Core |
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
| `LiteBus.Outbox.Abstractions` | Outbox records, contracts, envelopes, store roles, and dispatcher contract | `DurableMessaging.Abstractions`, `Messaging.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Outbox` | Outbox writer services, processor, module, and `OutboxModuleBuilder` | `Outbox.Abstractions`, `Messaging`, `DurableMessaging.Abstractions`, `Runtime.Abstractions` | logging abstractions |
| `LiteBus.Saga.Abstractions` | Saga instance contracts and store interfaces | `Commands.Abstractions`, `Messaging.Abstractions` | none |
| `LiteBus.Saga` | `SagaProcessorHook`, explicit in-memory store, `SagaModule`, and `SagaModuleBuilder` | `Saga.Abstractions`, `DurableMessaging.Abstractions`, `Messaging.Abstractions`, `Runtime.Abstractions` | logging abstractions |
| `LiteBus.Saga.InboxIntegration` | Nested `EnableSaga(...)` bridge on `InboxModuleBuilder` | `Saga`, `Inbox` | none |
| `LiteBus.Saga.Storage.PostgreSql` | Explicit PostgreSQL saga store and schema | `Saga`, `Saga.Abstractions`, `Storage.PostgreSql`, `Messaging.Abstractions`, `Runtime.Abstractions` | `Npgsql` |
| `LiteBus.Outbox.Storage.PostgreSql` | Npgsql outbox store, optional `PostgreSqlOutboxSchemaInitializer` | `Outbox.Abstractions`, `Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | EF Core outbox store using `IDbContextFactory<TContext>` operation contexts | `Outbox`, `Outbox.Abstractions`, `Storage.EntityFrameworkCore` | EF Core |
| `LiteBus.Outbox.Storage.InMemory` | Thread-safe in-memory outbox store | `Outbox.Abstractions` | none |
| `LiteBus.Outbox.Dispatch.InProcess` | `UseInProcessDispatch` via `IEventMediator` | `Outbox.Abstractions`, `Events.Abstractions`, `Messaging` | none |
| `LiteBus.Outbox.Dispatch` | Shared `TransportOutboxDispatcher` and envelope mapping | `Outbox.Abstractions`, `Messaging`, `Transport.Abstractions`, `Transport` | none |
| `LiteBus.Outbox.Dispatch.Amqp` | `UseAmqpDispatch`: AMQP transport outbox dispatch | `Outbox.Dispatch`, `Transport.Amqp` | none |
| `LiteBus.Outbox.Dispatch.AzureServiceBus` | `UseAzureServiceBusDispatch` | `Outbox.Dispatch`, `Transport.AzureServiceBus` | none |
| `LiteBus.Outbox.Dispatch.AwsSqs` | `UseAwsSqsDispatch` | `Outbox.Dispatch`, `Transport.AwsSqs` | none |
| `LiteBus.Outbox.Dispatch.Kafka` | `UseKafkaDispatch` | `Outbox.Dispatch`, `Transport.Kafka` | none |
| `LiteBus.Outbox.Dispatch.InMemory` | `UseInMemoryDispatch`: in-memory transport outbox dispatch | `Outbox.Dispatch`, `Transport.InMemory` | none |
| `LiteBus.Transport.Abstractions` | `ITransportPublisher`, `IMessageConsumer`, transport headers | none | none |
| `LiteBus.Transport` | Circuit breaker metrics, transport tracing, and header value parsing | `Transport.Abstractions`, `Runtime.Abstractions` | none |
| `LiteBus.Transport.Amqp` | RabbitMQ adapter | `Transport.Abstractions`, `Transport`, `Runtime.Abstractions` | `RabbitMQ.Client` |
| `LiteBus.Inbox.Extensions.OpenTelemetry` | Register inbox traces and metrics | `Inbox` | `OpenTelemetry` |
| `LiteBus.Outbox.Extensions.OpenTelemetry` | Register outbox traces and metrics | `Outbox` | `OpenTelemetry` |
| `LiteBus.Transport.Extensions.OpenTelemetry` | Register transport tracing and circuit breaker metrics | `Transport` | `OpenTelemetry` |
| `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` | Register AMQP circuit breaker metrics | `Transport.Amqp` | `OpenTelemetry` |
| `LiteBus.Storage.PostgreSql` | Shared PG quoting, schema version, advisory locks | none | `Npgsql` |
| `LiteBus.Storage.EntityFrameworkCore` | Shared EF Core options, transactions, and provider helpers | `DurableMessaging.Abstractions`, `Messaging.Abstractions` | EF Core |
| `LiteBus.Analyzers` | Roslyn analyzers for handler and contract rules | Roslyn only | `Microsoft.CodeAnalysis.CSharp` |
| `LiteBus.*.Extensions.Microsoft.DependencyInjection` | Module registration for Microsoft DI | Module package, Microsoft DI runtime adapter | Microsoft DI |
| `LiteBus.Runtime.Extensions.Hosting` | Shared host orchestrator and manual background-service wrapper | `Runtime.Abstractions`, Microsoft hosting abstractions | none |
| `LiteBus.Runtime.Extensions.Microsoft.Hosting` | Microsoft DI registration for manifest host adapters | `Runtime.Extensions.Hosting`, Microsoft DI abstractions | none |
| `LiteBus.Runtime.Extensions.Autofac.Hosting` | Autofac registration for manifest host adapters | `Runtime.Extensions.Hosting`, Autofac | none |
| `LiteBus.*.Extensions.Autofac` | Module registration for Autofac | Module package, Autofac runtime adapter | Autofac |
| `LiteBus` | Aggregate meta-package (core modules; storage/dispatch remain opt-in) | Commands, Queries, Events, Messaging, Inbox, Outbox, abstractions | none |
| `LiteBus.Testing` | Published test harness: `Test*` mediators and stores, `InboxOutboxTestHost`, processor pass helpers | `Commands`, `Events`, `Queries`, `Inbox`, `Outbox`, `Messaging`, `Runtime.Abstractions`, `Runtime.Extensions.Microsoft.Hosting`, `Extensions.Microsoft.DependencyInjection`, in-memory storage packages | `AwesomeAssertions`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting.Abstractions`, `Newtonsoft.Json` |
| `LiteBus.Storage.Testing` | Published xUnit store conformance bases for custom adapter authors | `Inbox.Abstractions`, `Outbox.Abstractions` | `AwesomeAssertions`, `xunit` |
| `LiteBus.Transport.Testing` | Published xUnit publisher and consumer conformance base for custom adapter authors | `Transport.Abstractions` | `xunit` |

## Dependency Rules

| From | May depend on | Must NOT depend on |
| --- | --- | --- |
| Contract roles | Other allowed contract roles | Core implementations, SDKs, and host frameworks |
| `Transport.Abstractions` | none | Any LiteBus project reference |
| `Inbox` / `Outbox` core | Abstractions, Messaging, Runtime.Abstractions | Commands, Events, Npgsql, EF, RabbitMQ, any Dispatch/Ingress |
| `*.Storage.*` | Parent axis core, abstractions, store SDK, shared storage technology | Dispatch, ingress, semantic mediators, and unrelated adapter concerns |
| `*.Dispatch.*` | Abstractions, axis core (`Inbox` / `Outbox` for `IRequires` ordering), `Messaging` when payload protection or trace helpers are required, `Transport.Abstractions` for transport dispatch | Storage, Ingress, broker SDKs, and unrelated adapter concerns |
| `*.Ingress.*` | Inbox core and abstractions, shared ingress, and the matching root transport package | Storage, dispatch, unrelated broker SDKs, and unrelated adapter concerns |
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

| Surface | Example |
| --- | --- |
| Repository folder / project | `LiteBus.Inbox.Storage.PostgreSql` |
| C# namespace | `LiteBus.Inbox.Storage.PostgreSql` |
| NuGet package ID | `LiteBus.Inbox.Storage.PostgreSql` (set in `src/Directory.Build.props`; only the `LiteBus` meta-package keeps the `LiteBus` ID) |
| Extension holder class | `InboxModuleBuilderPostgreSqlExtensions` |

### `Use*` Extension to Package Map

| Extension | Package |
| --- | --- |
| `AddAmqpTransport(...)` | `LiteBus.Transport.Amqp` |
| `AddKafkaTransport(...)` | `LiteBus.Transport.Kafka` |
| `AddAwsSqsTransport(...)` | `LiteBus.Transport.AwsSqs` |
| `AddAzureServiceBusTransport(...)` | `LiteBus.Transport.AzureServiceBus` |
| `AddInMemoryTransport()` | `LiteBus.Transport.InMemory` |
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
    builder.AddMessaging(_ => { });
    builder.AddCommands(c => c.RegisterFromAssembly(typeof(Program).Assembly));
    builder.AddQueries(q => q.RegisterFromAssembly(typeof(Program).Assembly));
    builder.AddEvents(e => e.RegisterFromAssembly(typeof(Program).Assembly));
});
```

### Inbox Composition

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddInbox(inbox =>
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
    builder.AddMessaging(_ => { });
    builder.AddOutbox(outbox =>
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
    builder.AddAmqpTransport(new AmqpConnectionOptions
    {
        Uri = new Uri(configuration.GetConnectionString("Amqp")!)
    });
    builder.AddMessaging(_ => { });

    builder.AddOutbox(outbox =>
    {
        outbox.UseAmqpDispatch(options =>
        {
            options.DefaultDestination = "orders.events";
        });
    });

    builder.AddInbox(inbox =>
    {
        inbox.UseAmqpIngress(ingress =>
        {
            ingress.UseOptions(new AmqpInboxIngressOptions
            {
                QueueName = "commands.inbox"
            });
        });
    });
});
```

`AddAmqpTransport` owns the horizontal transport at the root. Dispatch and ingress modules require that root module and share its `ITransportPublisher` and `IMessageConsumer`. The same rule applies to Kafka, AWS SQS, Azure Service Bus, and in-memory transport modules.

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
