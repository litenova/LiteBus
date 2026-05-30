<h1 align="center">
  <a href="https://github.com/litenova/LiteBus">
    <img src="assets/logo/icon.png" alt="LiteBus Logo" width="128">
  </a>
  <br>
  LiteBus
</h1>

<h4 align="center">A semantic, high-performance mediator for CQS and DDD in .NET. Free under MIT, forever.</h4>

<p align="center">
  <a href="https://github.com/litenova/LiteBus/actions/workflows/release.yml">
    <img src="https://github.com/litenova/LiteBus/actions/workflows/release.yml/badge.svg" alt="Build Status" />
  </a>
  <a href="https://codecov.io/gh/litenova/LiteBus">
    <img src="https://codecov.io/gh/litenova/LiteBus/graph/badge.svg?token=XBNYITSV5A" alt="Code Coverage" />
  </a>
  <a href="https://www.nuget.org/packages/LiteBus.Commands.Extensions.Microsoft.DependencyInjection">
    <img src="https://img.shields.io/nuget/vpre/LiteBus.Commands.Extensions.Microsoft.DependencyInjection.svg" alt="NuGet Version" />
  </a>
  <a href="https://github.com/litenova/LiteBus/wiki">
    <img src="https://img.shields.io/badge/documentation-wiki-blue.svg" alt="Wiki" />
  </a>
</p>

LiteBus is an in-process mediator that keeps commands, queries, and events as separate, first-class concepts so your application code stays self-documenting. It runs handlers through a typed pipeline, caches handler metadata at startup, and depends on no DI container.

- **Semantic by design.** `ICommand<TResult>`, `IQuery<TResult>`, and `IEvent` instead of one generic request. Events can be plain POCOs.
- **Typed pipeline per message.** Distinct pre-handlers, post-handlers, and error-handlers for each message type, plus open generic handlers for cross-cutting concerns.
- **Event concurrency you control.** Order handlers into priority groups and run each group, and the handlers within it, sequentially or in parallel.
- **Durable when needed.** An inbox schedules commands and an outbox stores integration events, with at-least-once delivery on PostgreSQL.

## Install

```bash
dotnet add package LiteBus.Commands.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Queries.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Events.Extensions.Microsoft.DependencyInjection
```

The core messaging runtime is pulled in automatically. Install only the modules you use.

## Quick start

Define a command and its handler:

```csharp
public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<Guid>;

public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    public Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var productId = Guid.NewGuid();
        return Task.FromResult(productId);
    }
}
```

Register the modules and send the command:

```csharp
builder.Services.AddLiteBus(liteBus =>
{
    var assembly = typeof(Program).Assembly;
    liteBus.AddCommandModule(module => module.RegisterFromAssembly(assembly));
    liteBus.AddQueryModule(module => module.RegisterFromAssembly(assembly));
    liteBus.AddEventModule(module => module.RegisterFromAssembly(assembly));
});

// Inject ICommandMediator, IQueryMediator, IEventMediator where you need them
var productId = await commandMediator.SendAsync(new CreateProductCommand("Widget", 9.99m));
```

Queries (`IQueryMediator.QueryAsync`) and events (`IEventMediator.PublishAsync`) follow the same shape. The [Getting Started](https://github.com/litenova/LiteBus/wiki/Getting-Started) guide walks through all three end to end.

## Features

| Feature | What it does | Docs |
| --- | --- | --- |
| Typed pipeline | Pre-, post-, and error-handlers per message type, with an ambient context shared across a single mediation. | [The Handler Pipeline](https://github.com/litenova/LiteBus/wiki/The-Handler-Pipeline) |
| Handler priority | Order handlers within a stage, and group event handlers into execution phases. | [Handler Priority](https://github.com/litenova/LiteBus/wiki/Handler-Priority) |
| Tags and predicates | Run a different set of handlers per call based on runtime context. | [Handler Filtering](https://github.com/litenova/LiteBus/wiki/Handler-Filtering) |
| Polymorphic dispatch | A handler for a base type runs for every derived message. | [Polymorphic Dispatch](https://github.com/litenova/LiteBus/wiki/Polymorphic-Dispatch) |
| Open generic handlers | One handler applies to every matching message; closed at startup. Ideal for logging, validation, metrics. | [Open Generic Handlers](https://github.com/litenova/LiteBus/wiki/Open-Generic-Handlers) |
| Event concurrency | Sequential or parallel execution across priority groups and within a group. | [Event Module](https://github.com/litenova/LiteBus/wiki/Event-Module) |
| Streaming queries | Return `IAsyncEnumerable<T>` for large result sets. | [Query Module](https://github.com/litenova/LiteBus/wiki/Query-Module) |
| Command inbox | Schedule commands for reliable, out-of-band execution with idempotency keys. | [Command Inbox](https://github.com/litenova/LiteBus/wiki/Command-Inbox) |
| Outbox | Store integration events in the same transaction as a state change, publish after commit. | [Outbox](https://github.com/litenova/LiteBus/wiki/Outbox) |
| DI-agnostic core | First-class Microsoft DI and Autofac adapters; an adapter pattern for others. | [Architecture](https://github.com/litenova/LiteBus/wiki/Architecture) |

## Packages

LiteBus ships as small packages so you reference only what you run. The full layout, including abstractions and DI adapters, is in the [Dependency Graph](https://github.com/litenova/LiteBus/wiki/Dependency-Graph).

<details>
<summary>Full package matrix</summary>

| Category | Package |
| --- | --- |
| Metapackage | `LiteBus` |
| Core modules | `LiteBus.Commands`, `LiteBus.Queries`, `LiteBus.Events`, `LiteBus.Inbox`, `LiteBus.Outbox`, `LiteBus.Messaging`, `LiteBus.Runtime` |
| Abstractions | `LiteBus.Commands.Abstractions`, `LiteBus.Queries.Abstractions`, `LiteBus.Events.Abstractions`, `LiteBus.Inbox.Abstractions`, `LiteBus.Outbox.Abstractions`, `LiteBus.Messaging.Abstractions`, `LiteBus.Runtime.Abstractions` |
| PostgreSQL | `LiteBus.Inbox.Storage.PostgreSql`, `LiteBus.Outbox.Storage.PostgreSql` |
| Microsoft DI | `LiteBus.Extensions.Microsoft.DependencyInjection`, `LiteBus.Commands.Extensions.Microsoft.DependencyInjection`, `LiteBus.Queries.Extensions.Microsoft.DependencyInjection`, `LiteBus.Events.Extensions.Microsoft.DependencyInjection`, `LiteBus.Messaging.Extensions.Microsoft.DependencyInjection`, `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` |
| Background work | `ILiteBusBackgroundWork` via `LiteBus.Inbox`, `LiteBus.Outbox`, storage, and ingress modules (requires `AddLiteBus` on MS DI or Autofac) |
| Autofac | `LiteBus.Commands.Extensions.Autofac`, `LiteBus.Queries.Extensions.Autofac`, `LiteBus.Events.Extensions.Autofac`, `LiteBus.Messaging.Extensions.Autofac`, `LiteBus.Runtime.Extensions.Autofac` |

</details>

## Coming from MediatR

| MediatR | LiteBus |
| --- | --- |
| `IRequest<TResponse>` | `ICommand<TResult>` (writes) or `IQuery<TResult>` (reads) |
| `IRequest` | `ICommand` |
| `INotification` | `IEvent`, or any POCO, no interface required |
| `IStreamRequest<TResponse>` | `IStreamQuery<TResult>` returning `IAsyncEnumerable<TResult>` |
| `IPipelineBehavior<,>` | Typed pre-, post-, and error-handlers per message type, plus open generic handlers |

See [LiteBus vs. MediatR](https://github.com/litenova/LiteBus/wiki/LiteBus-and-MediatR-Differences) for the full comparison and migration notes.

## Documentation

The [LiteBus Wiki](https://github.com/litenova/LiteBus/wiki) is the complete reference: concepts, per-module guides, reliable messaging, internals, troubleshooting, and a glossary.

## License

LiteBus is free and licensed under the [MIT License](LICENSE), and always will be. Contributions are welcome; see [Contributing](https://github.com/litenova/LiteBus/wiki/Contributing).
