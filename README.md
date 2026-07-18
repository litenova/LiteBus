# LiteBus

<p align="center">
  <img src="assets/logo/litebus-logo.svg" alt="LiteBus logo" width="360">
</p>

<p align="center">
  <a href="https://github.com/litenova/LiteBus/actions/workflows/build-and-test.yml"><img src="https://github.com/litenova/LiteBus/actions/workflows/build-and-test.yml/badge.svg" alt="Build and test status"></a>
  <a href="https://codecov.io/gh/litenova/LiteBus"><img src="https://codecov.io/gh/litenova/LiteBus/graph/badge.svg?token=XBNYITSV5A" alt="Code coverage"></a>
  <a href="https://www.nuget.org/packages/LiteBus.Commands.Extensions.Microsoft.DependencyInjection"><img src="https://img.shields.io/nuget/vpre/LiteBus.Commands.Extensions.Microsoft.DependencyInjection.svg" alt="NuGet version"></a>
  <a href="https://github.com/litenova/LiteBus/blob/main/LICENSE"><img src="https://img.shields.io/github/license/litenova/LiteBus" alt="License"></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/10.0"><img src="https://img.shields.io/badge/.NET-10.0-512BD4" alt=".NET 10"></a>
  <a href="https://litebus.io"><img src="https://img.shields.io/badge/docs-litebus.io-2f6fed" alt="Documentation"></a>
</p>

LiteBus is a mediator and durable messaging library for .NET 10. Commands, queries, and events have separate contracts and pipelines. Inbox and outbox processing use explicit storage, dispatch, and ingress adapters, so an application references an external SDK only when it selects that integration.

Version 6 is under development. Public APIs, package contents, and persisted formats may change before the `v6.0.0` tag.

## Package Selection

Install the package for each application concern. The package brings its abstractions and lower-layer runtime dependencies with it.

| Concern | Package |
| --- | --- |
| Commands | `LiteBus.Commands.Extensions.Microsoft.DependencyInjection` |
| Queries | `LiteBus.Queries.Extensions.Microsoft.DependencyInjection` |
| Events | `LiteBus.Events.Extensions.Microsoft.DependencyInjection` |
| Inbox core | `LiteBus.Inbox` |
| Outbox core | `LiteBus.Outbox` |
| PostgreSQL storage | `LiteBus.Inbox.Storage.PostgreSql` or `LiteBus.Outbox.Storage.PostgreSql` |
| Entity Framework Core storage | `LiteBus.Inbox.Storage.EntityFrameworkCore` or `LiteBus.Outbox.Storage.EntityFrameworkCore` |
| In-memory storage for tests | `LiteBus.Inbox.Storage.InMemory` or `LiteBus.Outbox.Storage.InMemory` |
| Broker transport | `LiteBus.Transport.Amqp`, `LiteBus.Transport.Kafka`, `LiteBus.Transport.AwsSqs`, or `LiteBus.Transport.AzureServiceBus` |
| OpenTelemetry registration | `LiteBus.Inbox.Extensions.OpenTelemetry`, `LiteBus.Outbox.Extensions.OpenTelemetry`, or `LiteBus.Transport.Extensions.OpenTelemetry` |

The [Dependency Graph](https://litebus.io/docs/architecture/dependency-graph) lists every package, its architectural layer, and its direct references.

Read the published documentation at [litebus.io](https://litebus.io), or run the
Fumadocs site locally from [`site`](site/README.md).

## Quick Start

Install one or more semantic mediator modules:

```bash
dotnet add package LiteBus.Commands.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Queries.Extensions.Microsoft.DependencyInjection
dotnet add package LiteBus.Events.Extensions.Microsoft.DependencyInjection
```

Define a command and one handler:

```csharp
public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<Guid>;

public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    public Task<Guid> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        var productId = Guid.NewGuid();
        return Task.FromResult(productId);
    }
}
```

Register the messaging and semantic features in one callback. The module registry validates the completed dependency graph, so callback order does not change build order:

```csharp
builder.Services.AddLiteBus(liteBus =>
{
    var applicationAssembly = typeof(Program).Assembly;

    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(commands =>
        commands.RegisterFromAssembly(applicationAssembly));
    liteBus.AddQueries(queries =>
        queries.RegisterFromAssembly(applicationAssembly));
    liteBus.AddEvents(events =>
        events.RegisterFromAssembly(applicationAssembly));
});
```

Inject the mediator for the operation being performed:

```csharp
var productId = await commandMediator.SendAsync(
    new CreateProductCommand("Widget", 9.99m),
    cancellationToken);
```

The [Getting Started](https://litebus.io/docs/getting-started) guide covers commands, queries, events, module declaration, and handler discovery.

## Durable Messaging

An inbox stores a command before execution. An outbox stores an event before publication. Storage, dispatch, ingress, and hosted processing remain separate choices.

```csharp
builder.Services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(commands =>
        commands.RegisterFromAssembly(typeof(Program).Assembly));

    liteBus.AddInbox(inbox =>
    {
        inbox.Contracts.Register<CreateProductCommand>("catalog.create-product");
        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
        inbox.EnableInboxProcessor();
    });
});
```

Use in-memory storage for tests and local behavior checks. Use the PostgreSQL or Entity Framework Core adapter when durable writes must participate in an application transaction. See [Inbox](https://litebus.io/docs/reliable-messaging/inbox), [Outbox](https://litebus.io/docs/reliable-messaging/outbox), and [Transactional Messaging Writes](https://litebus.io/docs/reliable-messaging/transactional-writes).

## Architecture

LiteBus projects follow an explicit dependency role matrix. Every shipping project is assigned one role, and architecture tests reject forbidden project edges and direct package references.

| Dependency role | Responsibility |
| --- | --- |
| Platform, mediation, and durable contracts | Stable abstractions without SDK or host dependencies |
| Core implementation | Default implementations and broker-neutral runtime behavior |
| Technology adapter | One persistence or broker technology |
| Feature bridge | Storage, dispatch, ingress, and cross-feature integration |
| Host adapter | Dependency injection, hosting, ASP.NET Core, diagnostics, and telemetry composition |
| Consumer tooling and aggregate | Analyzer/test support and the core-only convenience package |

The project count is intentional. Inbox and outbox remain separate, each broker and store remains opt-in, and integration SDKs do not enter unrelated dependency graphs. See [Architecture](https://litebus.io/docs/architecture) for the module lifecycle and package rules.

## Documentation

Repository documentation is authoritative. Start with the [Documentation Index](https://litebus.io/docs).

| Subject | Reference |
| --- | --- |
| Compile-checked application sample | [LiteBus Sample](samples/LiteBus.Sample/README.md) |
| Capability and package inventory | [v6 Feature Index](https://litebus.io/docs/reference/feature-index-v6) and [Capability Catalog](https://litebus.io/docs/reference/capability-catalog) |
| Module and dependency model | [Architecture](https://litebus.io/docs/architecture) and [Dependency Graph](https://litebus.io/docs/architecture/dependency-graph) |
| Handler behavior | [Handler Pipeline](https://litebus.io/docs/concepts/handler-pipeline) and [Execution Context](https://litebus.io/docs/concepts/execution-context) |
| Reliable messaging | [Reliable Messaging Semantics](https://litebus.io/docs/reliable-messaging/semantics) |
| Operations | [Production Runbook](https://litebus.io/docs/operations/runbook) and [Diagnostics and Health](https://litebus.io/docs/operations/diagnostics-and-health) |
| Testing | [Testing](https://litebus.io/docs/testing) and [Integration Tests](https://litebus.io/docs/testing/integration-tests) |
| Upgrade work | [Migration Guide v6](https://litebus.io/docs/migration/v6) |

## Build and Test

```bash
dotnet restore LiteBus.slnx
dotnet build LiteBus.slnx --configuration Release --no-restore
dotnet test LiteBus.slnx --configuration Release --no-build
pwsh ./scripts/Test-Documentation.ps1
```

Docker is required for the PostgreSQL, AMQP, Kafka, AWS SQS emulator, Azure Service Bus emulator, and relational integration suites. The [Integration Tests](https://litebus.io/docs/testing/integration-tests) guide lists the CI categories and local commands.

## Contributing

Read [Contributing](https://litebus.io/docs/contributing) before changing public APIs, package references, module dependencies, or persisted envelope behavior.

LiteBus is licensed under the [MIT License](LICENSE).
