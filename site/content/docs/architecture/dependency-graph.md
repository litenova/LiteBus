# Dependency Graph

LiteBus ships one package per concern, so your application references only the packages it runs. This page maps each package to its dependency role and shows which references each role allows.

> **This page is for** developers who select LiteBus packages and check that a new reference points in an allowed direction.
>
> **After reading it** you can place a package in its role and read what it may reference. For the processing model behind the roles, continue with the [Architecture](README.md).

Historical package and API names live in the [v6 Migration Guide](../migration/v6.md).

## Dependency Roles

Each role describes dependency direction and package purpose. Roles are not numeric layers, and feature axes stay independent of the role assigned to a package. The role table below is the normative dependency summary. [Generated Package Inventory](generated-package-inventory.md) records every evaluated shipping project reference and direct package reference, and documentation CI rejects a stale inventory.

| Role | Package examples | Allowed project dependencies | Allowed direct package dependencies |
| --- | --- | --- | --- |
| Platform contracts | `Runtime.Abstractions`, `Transport.Abstractions` | Platform contracts | BCL only |
| Mediation contracts | `Messaging.Abstractions`, command/query/event abstractions | Platform and mediation contracts | BCL only |
| Durable contracts | `DurableMessaging.Abstractions`, inbox/outbox/saga abstractions | Platform, mediation, and durable contracts | BCL only |
| Core implementation | `Runtime`, `Messaging`, semantic mediators, `Inbox`, `Outbox`, `Saga`, `Transport` | Contract roles and core implementations | Logging abstractions only where operational logging is required |
| Technology adapter | `Storage.PostgreSql`, `Storage.EntityFrameworkCore`, `Transport.Amqp`, `Transport.Kafka`, `Transport.AwsSqs`, `Transport.AzureServiceBus` | Contract roles, core implementations, and technology adapters | One relevant persistence or broker SDK, plus logging abstractions |
| Feature bridge | `Inbox.Storage.*`, `Inbox.Dispatch.*`, `Inbox.Ingress.*`, `Outbox.Storage.*`, `Outbox.Dispatch.*`, `Saga.InboxIntegration`, `Saga.Storage.*` | Contract roles, core implementations, technology adapters, and feature bridges | The relevant adapter SDK, plus logging abstractions |
| Host adapter | DI, hosting, OpenTelemetry, health checks, ASP.NET Core | Any applicable lower role and other host adapters | The relevant host framework |
| Consumer tooling | `LiteBus.Analyzers`, `LiteBus.Testing.*` | Any role needed by that narrowly scoped tool | Analyzer or test-support packages |
| Aggregate | `LiteBus` | Contract and core implementation roles | BCL only |

`ArchitectureDependencyPolicyTests` parses every project under `src/` and fails on a missing role, a forbidden reference direction, a storage, dispatch, or ingress adapter that crosses concerns, or an unapproved package reference. Technology-family checks traverse the complete project-reference closure, so a broker SDK cannot enter an adapter through a shared project. When you introduce a new architectural role or SDK family, update the matrix and the test together.

Only the `LiteBus` aggregate meta-package bundles core mediators and durable messaging. Storage, dispatch, ingress, broker transports, and OpenTelemetry remain opt-in.

## Aggregate Package Contents

The `LiteBus` package ID pulls exactly these projects, and nothing else:

- Mediation contracts and cores: `Messaging.Abstractions`, `Messaging`, `Commands.Abstractions`, `Commands`, `Queries.Abstractions`, `Queries`, `Events.Abstractions`, `Events`.
- Durable contracts and cores: `DurableMessaging.Abstractions`, `Inbox.Abstractions`, `Inbox`, `Outbox.Abstractions`, `Outbox`.

It does not pull storage adapters, dispatch or ingress adapters, transports, OpenTelemetry exporters, analyzers, or DI container adapters. Installing the aggregate never implies a broker SDK or an ORM. Reference the DI adapter for the container in use (`LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` or the Autofac equivalent, usually through the per-module shells or the semantic aggregate `LiteBus.Extensions.Microsoft.DependencyInjection`).

Mediation only:

```csharp
// Packages: LiteBus, LiteBus.Extensions.Microsoft.DependencyInjection
services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(c => c.RegisterFromAssembly(typeof(Program).Assembly));
    builder.AddQueries(q => q.RegisterFromAssembly(typeof(Program).Assembly));
    builder.AddEvents(e => e.RegisterFromAssembly(typeof(Program).Assembly));
});
```

Durable inbox and outbox with PostgreSQL storage and in-process dispatch:

```csharp
// Packages: LiteBus, LiteBus.Extensions.Microsoft.DependencyInjection,
// LiteBus.Inbox.Storage.PostgreSql, LiteBus.Outbox.Storage.PostgreSql,
// LiteBus.Inbox.Dispatch.InProcess, LiteBus.Outbox.Dispatch.InProcess
services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ShipOrder>("orders.ship", 1);
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor();
    });
    builder.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.submitted", 1);
        outbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        outbox.UseInProcessDispatch();
        outbox.EnableOutboxProcessor();
    });
});
```

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

`LiteBus.Runtime` depends only on `Runtime.Abstractions`. `ILiteBusBuilder` exposes only `Modules`. Feature packages add the normal `AddMessaging`, `AddCommands`, `AddQueries`, `AddEvents`, `AddInbox`, and `AddOutbox` extensions, and `IModuleRegistry` remains available for advanced composition.

Shared transports are root modules. Dispatch and ingress modules declare `IRequires<TTransportModule>` and never create an implicit broker module. Storage, dispatch, and ingress modules also declare their parent inbox or outbox requirement where the type system permits it. The module registry validates the complete graph before any module builds.

Microsoft DI and Autofac adapters each register one `IMessageDispatchScopeFactory`. Core packages depend only on that contract. When no factory is present, composition fails. A manual host can opt in to `RootMessageDispatchScopeFactory` explicitly. EF Core storage uses `IDbContextFactory<TContext>` through adapter-owned operation factories and does not create container scopes inside stores.

## Package Table

The Direct project references and Direct package references columns repeat [Generated Package Inventory](generated-package-inventory.md), which `scripts/Get-PackageInventory.ps1` writes from evaluated MSBuild metadata. The Role column follows the Dependency Roles matrix above. Documentation CI compares every row with the inventory as sets, so update the reference cells only by regenerating the inventory.

| Package | Role | Direct project references | Direct package references |
| --- | --- | --- | --- |
| `LiteBus.Runtime.Abstractions` | Platform contracts | none | none |
| `LiteBus.Runtime` | Core implementation | `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | Host adapter | `LiteBus.Runtime`, `LiteBus.Runtime.Extensions.Microsoft.Hosting` | `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `LiteBus.Runtime.Extensions.Autofac` | Host adapter | `LiteBus.Runtime`, `LiteBus.Runtime.Extensions.Autofac.Hosting` | `Autofac`, `Autofac.Extensions.DependencyInjection` |
| `LiteBus.Messaging.Extensions.Microsoft.DependencyInjection` | Host adapter | `LiteBus.Messaging`, `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | none |
| `LiteBus.Messaging.Extensions.Autofac` | Host adapter | `LiteBus.Messaging`, `LiteBus.Runtime.Extensions.Autofac` | none |
| `LiteBus.Commands.Extensions.Microsoft.DependencyInjection` | Host adapter | `LiteBus.Commands`, `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | none |
| `LiteBus.Commands.Extensions.Autofac` | Host adapter | `LiteBus.Commands`, `LiteBus.Runtime.Extensions.Autofac` | none |
| `LiteBus.Queries.Extensions.Microsoft.DependencyInjection` | Host adapter | `LiteBus.Queries`, `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | none |
| `LiteBus.Queries.Extensions.Autofac` | Host adapter | `LiteBus.Queries`, `LiteBus.Runtime.Extensions.Autofac` | none |
| `LiteBus.Events.Extensions.Microsoft.DependencyInjection` | Host adapter | `LiteBus.Events`, `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` | none |
| `LiteBus.Events.Extensions.Autofac` | Host adapter | `LiteBus.Events`, `LiteBus.Runtime.Extensions.Autofac` | none |
| `LiteBus.Extensions.Microsoft.DependencyInjection` | Host adapter | `LiteBus.Commands.Extensions.Microsoft.DependencyInjection`, `LiteBus.Events.Extensions.Microsoft.DependencyInjection`, `LiteBus.Messaging.Extensions.Microsoft.DependencyInjection`, `LiteBus.Queries.Extensions.Microsoft.DependencyInjection` | none |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | Host adapter | `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| `LiteBus.Extensions.AspNetCore` | Host adapter | `LiteBus.Commands.Abstractions`, `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Messaging.Abstractions` | Mediation contracts | `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Messaging` | Core implementation | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Commands.Abstractions` | Mediation contracts | `LiteBus.Messaging.Abstractions` | none |
| `LiteBus.Commands` | Core implementation | `LiteBus.Commands.Abstractions`, `LiteBus.Messaging`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Commands.Recurring` | Feature bridge | `LiteBus.Commands`, `LiteBus.Commands.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Commands.Recurring.Storage.PostgreSql` | Feature bridge | `LiteBus.Commands`, `LiteBus.Commands.Recurring`, `LiteBus.Runtime.Abstractions`, `LiteBus.Storage.PostgreSql` | `Npgsql`, `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Commands.Storage.Marten` | Feature bridge | `LiteBus.Commands`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | `Marten`, `Npgsql` |
| `LiteBus.Commands.Storage.PostgreSql` | Feature bridge | `LiteBus.Commands`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Queries.Abstractions` | Mediation contracts | `LiteBus.Messaging.Abstractions` | none |
| `LiteBus.Queries` | Core implementation | `LiteBus.Messaging`, `LiteBus.Queries.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Events.Abstractions` | Mediation contracts | `LiteBus.Messaging.Abstractions` | none |
| `LiteBus.Events` | Core implementation | `LiteBus.Events.Abstractions`, `LiteBus.Messaging`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Inbox.Abstractions` | Durable contracts | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.DurableMessaging.Abstractions` | Durable contracts | `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Inbox` | Core implementation | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging`, `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Inbox.Storage.PostgreSql` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Inbox.Storage.EntityFrameworkCore` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Storage.EntityFrameworkCore` | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` |
| `LiteBus.Inbox.Storage.InMemory` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Inbox.Dispatch.InProcess` | Feature bridge | `LiteBus.Commands`, `LiteBus.Commands.Abstractions`, `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Inbox.Dispatch` | Feature bridge | `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | none |
| `LiteBus.Inbox.Dispatch.Amqp` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Dispatch`, `LiteBus.Transport.Amqp` | none |
| `LiteBus.Inbox.Dispatch.AzureServiceBus` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Dispatch`, `LiteBus.Transport.AzureServiceBus` | none |
| `LiteBus.Inbox.Dispatch.AwsSqs` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Dispatch`, `LiteBus.Transport.AwsSqs` | none |
| `LiteBus.Inbox.Dispatch.Kafka` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Dispatch`, `LiteBus.Transport.Kafka` | none |
| `LiteBus.Inbox.Dispatch.InMemory` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Dispatch`, `LiteBus.Transport.InMemory` | none |
| `LiteBus.Inbox.Ingress` | Feature bridge | `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Inbox.Ingress.Amqp` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Ingress`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.Amqp` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Inbox.Ingress.AzureServiceBus` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Ingress`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.AzureServiceBus` | none |
| `LiteBus.Inbox.Ingress.AwsSqs` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Ingress`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.AwsSqs` | none |
| `LiteBus.Inbox.Ingress.Kafka` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Ingress`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.Kafka` | none |
| `LiteBus.Inbox.Ingress.InMemory` | Feature bridge | `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Ingress`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.InMemory` | none |
| `LiteBus.Outbox.Abstractions` | Durable contracts | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Outbox` | Core implementation | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Saga.Abstractions` | Durable contracts | `LiteBus.Commands.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Saga` | Core implementation | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Saga.Abstractions` | none |
| `LiteBus.Saga.InboxIntegration` | Feature bridge | `LiteBus.Commands.Abstractions`, `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging`, `LiteBus.Saga` | none |
| `LiteBus.Saga.Storage.PostgreSql` | Feature bridge | `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Saga`, `LiteBus.Saga.Abstractions`, `LiteBus.Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Outbox.Storage.PostgreSql` | Feature bridge | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Storage.PostgreSql` | `Npgsql` |
| `LiteBus.Outbox.Storage.Marten` | Feature bridge | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Storage.PostgreSql`, `LiteBus.Runtime.Abstractions` | `Marten` |
| `LiteBus.Outbox.Storage.EntityFrameworkCore` | Feature bridge | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Storage.EntityFrameworkCore` | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` |
| `LiteBus.Outbox.Storage.InMemory` | Feature bridge | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Outbox.Dispatch.InProcess` | Feature bridge | `LiteBus.Events`, `LiteBus.Events.Abstractions`, `LiteBus.Messaging`, `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions` | none |
| `LiteBus.Outbox.Dispatch` | Feature bridge | `LiteBus.Messaging`, `LiteBus.Outbox.Abstractions`, `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | none |
| `LiteBus.Outbox.Dispatch.Amqp` | Feature bridge | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Dispatch`, `LiteBus.Transport.Amqp` | none |
| `LiteBus.Outbox.Dispatch.AzureServiceBus` | Feature bridge | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Dispatch`, `LiteBus.Transport.AzureServiceBus` | none |
| `LiteBus.Outbox.Dispatch.AwsSqs` | Feature bridge | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Dispatch`, `LiteBus.Transport.AwsSqs` | none |
| `LiteBus.Outbox.Dispatch.Kafka` | Feature bridge | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Dispatch`, `LiteBus.Transport.Kafka` | none |
| `LiteBus.Outbox.Dispatch.InMemory` | Feature bridge | `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Dispatch`, `LiteBus.Transport.InMemory` | none |
| `LiteBus.Transport.Abstractions` | Platform contracts | none | none |
| `LiteBus.Transport` | Core implementation | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport.Abstractions` | none |
| `LiteBus.Transport.Amqp` | Technology adapter | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | `RabbitMQ.Client` |
| `LiteBus.Transport.Kafka` | Technology adapter | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | `Confluent.Kafka` |
| `LiteBus.Transport.AwsSqs` | Technology adapter | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | `AWSSDK.SQS` |
| `LiteBus.Transport.AzureServiceBus` | Technology adapter | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | `Azure.Messaging.ServiceBus`, `Microsoft.Extensions.Logging.Abstractions` |
| `LiteBus.Transport.InMemory` | Technology adapter | `LiteBus.Runtime.Abstractions`, `LiteBus.Transport`, `LiteBus.Transport.Abstractions` | none |
| `LiteBus.Messaging.Extensions.OpenTelemetry` | Host adapter | `LiteBus.Messaging` | `OpenTelemetry` |
| `LiteBus.Inbox.Extensions.OpenTelemetry` | Host adapter | `LiteBus.Inbox` | `OpenTelemetry` |
| `LiteBus.Outbox.Extensions.OpenTelemetry` | Host adapter | `LiteBus.Outbox` | `OpenTelemetry` |
| `LiteBus.Transport.Extensions.OpenTelemetry` | Host adapter | `LiteBus.Transport` | `OpenTelemetry` |
| `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` | Host adapter | `LiteBus.Transport.Amqp` | `OpenTelemetry` |
| `LiteBus.Storage.PostgreSql` | Technology adapter | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions` | `Npgsql` |
| `LiteBus.Storage.EntityFrameworkCore` | Technology adapter | `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Messaging.Abstractions` | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Relational` |
| `LiteBus.Analyzers` | Consumer tooling | none | `Microsoft.CodeAnalysis.Analyzers (private)`, `Microsoft.CodeAnalysis.CSharp (private)`, `System.Collections.Immutable (private)` |
| `LiteBus.Runtime.Extensions.Hosting` | Host adapter | `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.Hosting.Abstractions` |
| `LiteBus.Runtime.Extensions.Microsoft.Hosting` | Host adapter | `LiteBus.Runtime.Extensions.Hosting` | `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions` |
| `LiteBus.Runtime.Extensions.Autofac.Hosting` | Host adapter | `LiteBus.Runtime.Extensions.Hosting` | `Autofac`, `Microsoft.Extensions.Hosting.Abstractions` |
| `LiteBus` | Aggregate | `LiteBus.Commands`, `LiteBus.Commands.Abstractions`, `LiteBus.DurableMessaging.Abstractions`, `LiteBus.Events`, `LiteBus.Events.Abstractions`, `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Messaging`, `LiteBus.Messaging.Abstractions`, `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Queries`, `LiteBus.Queries.Abstractions` | none |
| `LiteBus.Testing` | Consumer tooling | none | none |
| `LiteBus.Testing.Mediation` | Consumer tooling | `LiteBus.Commands.Abstractions`, `LiteBus.Events.Abstractions`, `LiteBus.Messaging`, `LiteBus.Messaging.Abstractions`, `LiteBus.Queries.Abstractions`, `LiteBus.Runtime` | none |
| `LiteBus.Testing.Transport` | Consumer tooling | `LiteBus.Transport.Abstractions` | none |
| `LiteBus.Testing.DurableMessaging` | Consumer tooling | `LiteBus.Extensions.Microsoft.DependencyInjection`, `LiteBus.Inbox`, `LiteBus.Inbox.Abstractions`, `LiteBus.Inbox.Storage.InMemory`, `LiteBus.Messaging`, `LiteBus.Outbox`, `LiteBus.Outbox.Abstractions`, `LiteBus.Outbox.Storage.InMemory`, `LiteBus.Runtime.Abstractions` | `Microsoft.Extensions.DependencyInjection` |
| `LiteBus.Testing.Hosting` | Consumer tooling | `LiteBus.Inbox`, `LiteBus.Runtime.Abstractions`, `LiteBus.Runtime.Extensions.Microsoft.Hosting` | `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting.Abstractions` |
| `LiteBus.Storage.Testing` | Consumer tooling | `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions` | `AwesomeAssertions`, `StyleCop.Analyzers (private)`, `xunit`, `Xunit.SkippableFact` |
| `LiteBus.Transport.Testing` | Consumer tooling | `LiteBus.Transport.Abstractions` | `StyleCop.Analyzers (private)`, `xunit`, `Xunit.SkippableFact` |

Per-module `*.Extensions.Microsoft.DependencyInjection` and `*.Extensions.Autofac` shells are registration patterns, not shipping packages, so they have no inventory row. Each one depends on its module package and the matching runtime adapter.

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

Shared storage technology packages contain only invariants that are identical across durable axes. `LiteBus.Storage.PostgreSql` owns schema mechanics, database-clock access, and idempotency resolution. `LiteBus.Storage.EntityFrameworkCore` owns provider resolution, relational lease execution, and common durable operations. Inbox and outbox adapters retain their own tables, row mappings, lifecycle transitions, and public contracts. When sharing it would couple independent semantics or require a cross-axis abstraction, similar-looking transition code remains axis-owned.

Additional rules:

- Query packages do not depend on inbox or outbox packages.
- Core inbox and outbox modules register writers and processors only. They do not register dispatchers.
- Processor background services validate that exactly one dispatcher is registered.
- Stable contracts use names and versions, not assembly-qualified CLR names.
- When each closed type is registered, closed generic messages are supported. Open generic contracts are rejected.

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
| `AddRecurring(...)` (commands) | `LiteBus.Commands.Recurring` |
| `UsePostgreSqlRecurringLease(...)` (commands) | `LiteBus.Commands.Recurring.Storage.PostgreSql` |
| `UsePostgreSqlIdempotencyStore(...)` (commands) | `LiteBus.Commands.Storage.PostgreSql` |
| `UseMartenUnitOfWork()`, `UseMartenIdempotencyStore(...)` (commands) | `LiteBus.Commands.Storage.Marten` |
| `WithIdempotencyKey(...)` (ASP.NET Core endpoints) | `LiteBus.Extensions.AspNetCore` |
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

PostgreSQL and AMQP integration tests require Docker. When Docker is unavailable, CI skips them without failing.

## Next

- [Architecture](README.md): Storage, Dispatch, and Ingress flow diagrams
- [v6 Migration Guide](../migration/v6.md): historical package mappings
