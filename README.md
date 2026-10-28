# LiteBus

![LiteBus logo](https://raw.githubusercontent.com/litenova/LiteBus/main/assets/logo/icon.png)

[![CI status](https://github.com/litenova/LiteBus/actions/workflows/ci.yml/badge.svg)](https://github.com/litenova/LiteBus/actions/workflows/ci.yml)
[![NuGet version](https://img.shields.io/nuget/v/LiteBus.svg)](https://www.nuget.org/packages/LiteBus)
[![NuGet downloads](https://img.shields.io/nuget/dt/LiteBus.svg)](https://www.nuget.org/packages/LiteBus)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![MIT license](https://img.shields.io/github/license/litenova/LiteBus)](https://github.com/litenova/LiteBus/blob/main/LICENSE)

Commands, queries, and events for .NET, with a full handler pipeline and an optional inbox, outbox, and sagas. LiteBus is MIT licensed, free for commercial use, and will stay free.

Full documentation: [litebus.io/docs](https://litebus.io/docs)

## Table of Contents

- [Features](#features)
- [Installation](#installation)
- [Usage](#usage)
- [Examples](#examples)
- [Packages](#packages)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [Support](#support)
- [Security](#security)
- [License](#license)

## Features

| Area | What you get |
| --- | --- |
| Messages | Commands, queries, streaming queries (`IAsyncEnumerable`), and events, each with its own mediator |
| Pipeline | Pre-handlers, validators, post-handlers, error handlers, and completion handlers |
| Decisions | Guards that deny, validators that reject, and shortcuts that answer before the handler runs |
| Handler selection | Priorities, tags, predicates, polymorphic dispatch, generic messages, and open generic handlers |
| Cross-cutting | Idempotency, auditing, and authorization declared per message |
| Context | Execution context with shared items, tags, and abort from any stage |
| Durable messaging | Inbox, transactional outbox, sagas, scheduling, retries, and dead-lettering |
| Storage | PostgreSQL, Entity Framework Core, Marten, and in-memory |
| Brokers | RabbitMQ, Kafka, AWS SQS, Azure Service Bus, and in-memory |
| Hosting | Microsoft DI or Autofac, Generic Host, health checks, management endpoints, and OpenTelemetry |
| Tooling | Roslyn analyzers and test harnesses |

## Installation

LiteBus targets .NET 10. For commands, queries, and events on Microsoft dependency injection, install one package:

```bash
dotnet add package LiteBus.Extensions.Microsoft.DependencyInjection
```

To install only the modules you use, or to use Autofac, reference the per-module packages instead:

```bash
dotnet add package LiteBus.Commands.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Queries.Extensions.Autofac
```

Durable messaging, storage, and broker support are separate packages. See [Packages](#packages).

## Usage

Register LiteBus and discover the handlers in your assembly:

```csharp
builder.Services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(c => c.RegisterFromAssembly(typeof(Program).Assembly));
    liteBus.AddQueries(q => q.RegisterFromAssembly(typeof(Program).Assembly));
    liteBus.AddEvents(e => e.RegisterFromAssembly(typeof(Program).Assembly));
});
```

Define a command and its handler:

```csharp
public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<Guid>;

public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
        => Task.FromResult(Guid.NewGuid());
}
```

Inject `ICommandMediator` and send it:

```csharp
var id = await commandMediator.SendAsync(new CreateProductCommand("Widget", 9.99m), cancellationToken);
```

[Getting Started](https://litebus.io/docs/getting-started) walks through queries, events, and handler discovery.

## Examples

### Queries and Streaming Queries

```csharp
public sealed record GetProductQuery(Guid Id) : IQuery<ProductDto>;
public sealed record SearchProductsQuery(string Term) : IStreamQuery<ProductDto>;

public sealed class SearchProductsQueryHandler : IStreamQueryHandler<SearchProductsQuery, ProductDto>
{
    public async IAsyncEnumerable<ProductDto> StreamAsync(
        SearchProductsQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var product in _repository.SearchAsync(query.Term, cancellationToken))
            yield return new ProductDto(product.Id, product.Name);
    }
}

var product = await queryMediator.QueryAsync(new GetProductQuery(id), cancellationToken);

await foreach (var item in queryMediator.StreamAsync(new SearchProductsQuery("chair"), cancellationToken))
    Console.WriteLine(item.Name);
```

See [Queries](https://litebus.io/docs/concepts/queries).

### Events With Ordered Handlers

Events can be plain classes with no LiteBus dependency. Every handler runs, and `HandlerPriority` sets the order.

```csharp
public sealed record OrderShipped(Guid OrderId, string TrackingNumber);

[HandlerPriority(1)]
public sealed class UpdateReadModel : IEventHandler<OrderShipped> { /* ... */ }

[HandlerPriority(2)]
public sealed class SendShippingEmail : IEventHandler<OrderShipped> { /* ... */ }

await eventMediator.PublishAsync(new OrderShipped(orderId, trackingNumber), cancellationToken);
```

See [Events](https://litebus.io/docs/concepts/events) and [Handler Priority](https://litebus.io/docs/concepts/handler-priority).

### Validators, Pre-Handlers, and Post-Handlers

```csharp
public sealed class CreateProductValidator : ICommandValidator<CreateProductCommand>
{
    public Task<Validity> ValidateAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
        => Task.FromResult(command.Price > 0
            ? Validity.Valid
            : Validity.Invalid("the price must be positive", nameof(command.Price)));
}

public sealed class LogCommand : ICommandPreHandler<CreateProductCommand>
{
    public Task PreHandleAsync(CreateProductCommand command, CancellationToken cancellationToken = default) { /* ... */ }
}

public sealed class PublishProductCreated : ICommandPostHandler<CreateProductCommand, Guid>
{
    public Task PostHandleAsync(CreateProductCommand command, Guid productId, CancellationToken cancellationToken = default) { /* ... */ }
}
```

A failed validation reaches the caller as `LiteBusMessageInvalidException` carrying every failure. See [The Handler Pipeline](https://litebus.io/docs/concepts/handler-pipeline) and [Validation](https://litebus.io/docs/concepts/validation).

### Guards

A guard decides whether the work happens at all, for example an authorization check that needs the loaded entity.

```csharp
public sealed class CancelOrderGuard : ICommandGuard<CancelOrderCommand>
{
    public async Task<Verdict> DecideAsync(CancelOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = await _orders.LoadAsync(command.OrderId, cancellationToken);

        return order is null ? Verdict.Deny("the order does not exist") : Verdict.Allow;
    }
}
```

See [Deciding Whether the Work Happens](https://litebus.io/docs/concepts/handler-pipeline#deciding-whether-the-work-happens).

### One Handler for Many Messages

A handler for a base type or interface runs for every message that implements it. An open generic handler runs for every message of its kind.

```csharp
public sealed class AuditPreHandler : ICommandPreHandler<IAuditableCommand> { /* ... */ }

public sealed class LoggingPreHandler<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand { /* ... */ }
```

See [Polymorphic Dispatch](https://litebus.io/docs/concepts/polymorphic-dispatch) and [Open Generic Handlers](https://litebus.io/docs/concepts/open-generic-handlers).

### Handlers Selected by Tag

Untagged handlers run for every call. Tagged handlers run only when the caller names their tag.

```csharp
[HandlerTag("PublicAPI")]
public sealed class StrictValidationPreHandler : ICommandPreHandler<CreateUserCommand> { /* ... */ }

await commandMediator.SendAsync(command, "PublicAPI", cancellationToken);
```

See [Handler Filtering](https://litebus.io/docs/concepts/handler-filtering).

### Idempotent Commands

A repeated `PaymentId` is answered from the store instead of running the handler again.

```csharp
public sealed class ApplyPaymentDefinition : IIdempotencyDefinition<ApplyPaymentCommand>
{
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<ApplyPaymentCommand>(command => command.PaymentId.ToString());
}

liteBus.AddCommands(commands =>
{
    commands.RegisterFromAssembly(typeof(Program).Assembly);
    commands.EnableIdempotency();
    commands.UsePostgreSqlIdempotencyStore(postgres => postgres.UseDataSource(dataSource));
});
```

See [Idempotency](https://litebus.io/docs/concepts/idempotency).

### Transactional Outbox

Store an event beside your data and publish it to the broker after the commit.

```bash
dotnet add package LiteBus.Outbox.Storage.PostgreSql
dotnet add package LiteBus.Outbox.Dispatch.Amqp
```

```csharp
liteBus.AddAmqpTransport(new AmqpConnectionOptions { Uri = new Uri(amqpConnectionString) });

liteBus.AddOutbox(outbox =>
{
    outbox.Contracts.Register<OrderSubmitted>("orders.order-submitted", 1);
    outbox.UsePostgreSqlStorage(postgres => postgres.UseDataSource(dataSource));
    outbox.UseAmqpDispatch(amqp => amqp.DefaultDestination = "orders.order-submitted");
    outbox.EnableOutboxProcessor();
});

await outbox.EnqueueAsync(new OrderSubmitted { OrderId = orderId }, cancellationToken);
```

See [Outbox](https://litebus.io/docs/reliable-messaging/outbox), [Inbox](https://litebus.io/docs/reliable-messaging/inbox), [Saga](https://litebus.io/docs/reliable-messaging/saga), and [Transactional Writes](https://litebus.io/docs/reliable-messaging/transactional-writes).

## Packages

LiteBus ships as 87 packages, one per concern, so a service installs only what it runs. A Kafka service never references the RabbitMQ client, and an in-memory test host never references Npgsql.

### Mediation

| Package | Provides |
| --- | --- |
| `LiteBus.Commands` | Command mediator, handlers, and pipeline |
| `LiteBus.Queries` | Query and stream query mediator |
| `LiteBus.Events` | Event mediator with multiple handlers |
| `LiteBus.Messaging` | Shared pipeline, registry, and execution context |
| `LiteBus.Commands.Recurring` | Commands sent on a schedule |
| `LiteBus` | Commands, queries, events, inbox, and outbox core in one reference |

### Storage

| | Inbox | Outbox | Saga | Command idempotency | Recurring lease |
| --- | --- | --- | --- | --- | --- |
| PostgreSQL | yes | yes | yes | yes | yes |
| Entity Framework Core | yes | yes | | | |
| Marten | | yes | | yes | |
| In-memory | yes | yes | yes | for tests | |

Packages follow the pattern `LiteBus.Inbox.Storage.PostgreSql`, `LiteBus.Outbox.Storage.EntityFrameworkCore`, `LiteBus.Outbox.Storage.Marten`, `LiteBus.Saga.Storage.PostgreSql`, `LiteBus.Commands.Storage.PostgreSql`, `LiteBus.Commands.Storage.Marten`, and `LiteBus.Commands.Recurring.Storage.PostgreSql`.

### Brokers

| | Transport | Outbox dispatch | Inbox dispatch | Inbox ingress |
| --- | --- | --- | --- | --- |
| RabbitMQ (AMQP) | yes | yes | yes | yes |
| Apache Kafka | yes | yes | yes | yes |
| AWS SQS | yes | yes | yes | yes |
| Azure Service Bus | yes | yes | yes | yes |
| In-memory | yes | yes | yes | yes |
| In-process, no broker | | yes | yes | |

Packages follow the pattern `LiteBus.Transport.Kafka`, `LiteBus.Outbox.Dispatch.Kafka`, `LiteBus.Inbox.Dispatch.Kafka`, and `LiteBus.Inbox.Ingress.Kafka`. Outbox dispatch publishes stored events to the broker, inbox dispatch runs stored commands through it, and inbox ingress accepts commands consumed from it.

### Durable Messaging Core

`LiteBus.Inbox`, `LiteBus.Outbox`, `LiteBus.Saga`, `LiteBus.Saga.InboxIntegration`, `LiteBus.Inbox.Dispatch`, `LiteBus.Outbox.Dispatch`, `LiteBus.Inbox.Ingress`, `LiteBus.Transport`, `LiteBus.Storage.PostgreSql`, `LiteBus.Storage.EntityFrameworkCore`

### Containers and Hosting

| Package | Provides |
| --- | --- |
| `LiteBus.Extensions.Microsoft.DependencyInjection` | Commands, queries, events, and messaging for Microsoft DI in one reference |
| `LiteBus.Commands.Extensions.Autofac`, `.Queries`, `.Events`, `.Messaging`, `.Runtime` | The same modules for Autofac |
| `LiteBus.Runtime.Extensions.Microsoft.Hosting`, `LiteBus.Runtime.Extensions.Autofac.Hosting` | Startup tasks and background processors on the Generic Host |
| `LiteBus.Extensions.Diagnostics.HealthChecks` | ASP.NET Core health checks backed by LiteBus diagnostic probes |
| `LiteBus.Extensions.AspNetCore` | Management endpoints and HTTP idempotency keys |

### Observability

`LiteBus.Messaging.Extensions.OpenTelemetry`, `LiteBus.Inbox.Extensions.OpenTelemetry`, `LiteBus.Outbox.Extensions.OpenTelemetry`, `LiteBus.Transport.Extensions.OpenTelemetry`, and `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` register traces and metrics for mediation, processors, and brokers.

### Tooling

| Package | Provides |
| --- | --- |
| `LiteBus.Analyzers` | Compile-time checks for registration and configuration mistakes |
| `LiteBus.Testing.Mediation` | Command, query, and event mediator doubles |
| `LiteBus.Testing.DurableMessaging` | Inbox, outbox, processor, and in-memory host helpers |
| `LiteBus.Testing.Hosting`, `LiteBus.Testing.Transport`, `LiteBus.Testing` | Generic Host lifecycle helpers, transport publisher doubles, and test clocks |

Each module also has an `*.Abstractions` package for libraries that only reference contracts. The [Feature and Package Index](https://litebus.io/docs/reference/feature-index) maps every feature to its package, and the [Dependency Graph](https://litebus.io/docs/architecture/dependency-graph) lists what each one references.


## Documentation

- [Getting Started](https://litebus.io/docs/getting-started) and the [Cookbook](https://litebus.io/docs/getting-started/cookbook)
- [The Handler Pipeline](https://litebus.io/docs/concepts/handler-pipeline)
- [Reliable Messaging](https://litebus.io/docs/reliable-messaging)
- [Operations](https://litebus.io/docs/operations) and [Testing](https://litebus.io/docs/testing)
- [Differences from MediatR](https://litebus.io/docs/migration/mediatr-differences) and the [Migration Guide v7](https://litebus.io/docs/migration/v7)
- [Changelog](https://github.com/litenova/LiteBus/blob/main/Changelog.md)

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](https://github.com/litenova/LiteBus/blob/main/CONTRIBUTING.md) for the workflow and build steps, and the [AI Use Policy](https://github.com/litenova/LiteBus/blob/main/AI_POLICY.md) if you use generative AI tools.

## Support

Ask questions in [GitHub Discussions](https://github.com/litenova/LiteBus/discussions) and report bugs in [GitHub Issues](https://github.com/litenova/LiteBus/issues). See [SUPPORT.md](https://github.com/litenova/LiteBus/blob/main/SUPPORT.md) for what to include.

## Security

Report vulnerabilities privately as described in [SECURITY.md](https://github.com/litenova/LiteBus/blob/main/SECURITY.md), not in a public issue.

## License

LiteBus is licensed under the [MIT License](https://github.com/litenova/LiteBus/blob/main/LICENSE).
