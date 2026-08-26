<p align="center">
  <img src="assets/logo/icon.svg" alt="LiteBus logo" width="130">
</p>

<h1 align="center">LiteBus</h1>

<p align="center">
  <a href="https://github.com/litenova/LiteBus/actions/workflows/build-and-test.yml"><img src="https://github.com/litenova/LiteBus/actions/workflows/build-and-test.yml/badge.svg" alt="Build and test status"></a>
  <a href="https://app.codecov.io/gh/litenova/LiteBus"><img src="https://codecov.io/gh/litenova/LiteBus/branch/main/graph/badge.svg" alt="Test coverage"></a>
  <a href="https://litebus.io/docs"><img src="https://img.shields.io/badge/Documentation-Available-0A66C2?logo=gitbook" alt="Documentation"></a>
  <a href="https://www.nuget.org/packages/LiteBus"><img src="https://img.shields.io/nuget/v/LiteBus.svg" alt="NuGet version"></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/10.0"><img src="https://img.shields.io/badge/.NET-10.0-512BD4" alt=".NET 10"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/litenova/LiteBus" alt="MIT license"></a>
  <a href="AI_POLICY.md"><img src="https://img.shields.io/badge/AI-Policy-2EA043?logo=github" alt="AI policy"></a>
</p>

LiteBus provides command, query, and event mediation for .NET 10 applications using CQS and DDD. Durable modules add inbox, outbox, saga, storage, dispatch, ingress, hosting, and operational APIs without requiring unrelated broker or database SDKs.

LiteBus is open source under the MIT license, free for commercial use, and will remain free.
Find all API and architecture documentation at https://litebus.io/docs.

## What LiteBus Includes

- Separate command, query, and event contracts, mediators, handlers, and pipelines.
- Declarative per-message metadata and an audit trail recorded at the mediation boundary, including refusals and cancellations.
- Handler priorities, filters, pre-handlers, gates that refuse or answer early, post-handlers, error handlers, and completion handlers that observe every outcome.
- Durable inbox processing for commands and transactional outbox processing for events.
- Saga state with correlation, tenancy, optimistic concurrency, and duplicate dispatch suppression.
- Opt-in PostgreSQL, Entity Framework Core, in-memory, AMQP, Kafka, AWS SQS, and Azure Service Bus adapters.
- Generic Host integration, health checks, diagnostics, management endpoints, and OpenTelemetry registration.

## Quick Start

Install the command module for Microsoft dependency injection:

```bash
dotnet add package LiteBus.Commands.Extensions.Microsoft.DependencyInjection
```

Define a command and handler:

```csharp
public sealed record CreateProductCommand(string Name, decimal Price) : ICommand<Guid>;

public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    public Task<Guid> HandleAsync(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Guid.NewGuid());
    }
}
```

Register messaging and command handlers:

```csharp
builder.Services.AddLiteBus(liteBus =>
{
    liteBus.AddMessaging(_ => { });
    liteBus.AddCommands(commands =>
        commands.RegisterFromAssembly(typeof(Program).Assembly));
});
```

Inject `ICommandMediator` and send the command:

```csharp
var productId = await commandMediator.SendAsync(
    new CreateProductCommand("Widget", 9.99m),
    cancellationToken);
```

The [Getting Started guide](https://litebus.io/docs/getting-started) covers commands, queries, events, module registration, and handler discovery.

## Durable Messaging

The inbox persists commands before execution. The outbox persists events before publication. Saga storage tracks correlated workflow state. Each axis selects its storage, dispatch, ingress, and processor modules explicitly.

Use in-memory adapters for tests. Use PostgreSQL or Entity Framework Core when message writes must participate in an application transaction. Broker integrations remain separate packages so applications install only the SDKs they use.

See [Reliable Messaging](https://litebus.io/docs/reliable-messaging), [Transactional Messaging Writes](https://litebus.io/docs/reliable-messaging/transactional-writes), and [Package Selection](https://litebus.io/docs/architecture/dependency-graph).

## Documentation

- [Documentation Index](https://litebus.io/docs)
- [Feature and Package Index](https://litebus.io/docs/reference/feature-index-v6)
- [Architecture and Module Model](https://litebus.io/docs/architecture)
- [Operations](https://litebus.io/docs/operations)
- [Testing](https://litebus.io/docs/testing)
- [Migration Guide](https://litebus.io/docs/migration/v6)

## Build and Test

```bash
dotnet restore LiteBus.slnx
dotnet build LiteBus.slnx --configuration Release --no-restore
dotnet test LiteBus.slnx --configuration Release --no-build
```

Docker is required for integration suites that exercise PostgreSQL, AMQP, Kafka, AWS SQS, and Azure Service Bus emulators.

## Project Policy

- [Contributing](CONTRIBUTING.md)
- [AI Use Policy](AI_POLICY.md)
- [Security Policy](SECURITY.md)
- [Support](SUPPORT.md)
- [MIT License](LICENSE)
