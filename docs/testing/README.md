# Testing

This page covers testing durable messaging in LiteBus v6: InMemory storage for fast unit tests, shared store contract harnesses, Testcontainers for PostgreSQL and AMQP integration tests, and the durable transport integration matrix.

For a full inventory of integration test projects, fixtures, scenarios, and CI filters, see [Integration Tests](integration-tests.md).
## InMemory Storage

The InMemory storage packages implement all store roles in one thread-safe class. They use `TimeProvider` for lease expiry simulation.

### Inbox Unit Test Setup

```csharp
services.AddLiteBus(builder =>
{
    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddCommandModule(c => c.Register<ProcessPaymentCommandHandler>());
    builder.Modules.AddInboxModule(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UseInMemoryStorage();
        inbox.UseInProcessDispatch();
    });
});
```

Resolve `IInbox`, call `AcceptAsync`, then resolve `IInboxProcessor` and call `ProcessPendingAsync` to execute the handler in the same test.

### Outbox Unit Test Setup

```csharp
services.AddLiteBus(builder =>
{
    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddEventModule(e => e.Register<OrderSubmittedEventHandler>());
    builder.Modules.AddOutboxModule(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.order-submitted", 1);
        outbox.UseInMemoryStorage();
        outbox.UseInProcessDispatch();
    });
});
```

When using `UseInProcessDispatch`, the real event pipeline runs. Mock `IEventMediator` only when testing code outside the dispatch package.

### Lease and Retry Behavior

InMemory stores honor `InboxProcessorOptions.LeaseDuration` and `OutboxProcessorOptions.LeaseDuration`. Advance time with a custom `TimeProvider` registered in DI to test lease reclaim and retry visibility without `Task.Delay`.

## Shared Store Contract Tests

`LiteBus.Storage.Testing` (`tests/LiteBus.Storage.Testing/`, `IsTestProject=false`) defines abstract `InboxStoreContractTests` and `OutboxStoreContractTests` suites.

## PostgreSQL Integration Tests

PostgreSQL storage integration tests use Testcontainers with `postgres:16-alpine`. v6 schema is version **1** only (create + validate; no upgrade scripts).

### Example Registration

```csharp
services.AddLiteBus(builder =>
{
    builder.Modules.AddMessageModule(_ => { });
    builder.Modules.AddCommandModule(c => c.Register<MyCommandHandler>());

    builder.Modules.AddInboxModule(inbox =>
    {
        inbox.Contracts.Register<MyCommand>("my.command", 1);
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
    });
});
```

## AMQP Integration Tests

AMQP dispatch and ingress tests use Testcontainers with RabbitMQ and LavinMQ images. Wire-level ingress scenarios (including batch accept and the richest failure suite) live in `LiteBus.Durable.IntegrationTests` under `Ingress/Amqp/` and `Dispatch/Inbox/Amqp/`. Low-level AMQP publish/consume/ack behavior without inbox/outbox lives in `LiteBus.Transport.IntegrationTests/Amqp/`.

## Durable Transport Integration Tests

`LiteBus.Durable.IntegrationTests` (`tests/LiteBus.Durable.IntegrationTests/`) is the unified broker matrix for InMemory, Kafka, AWS SQS, Azure Service Bus (emulator plus optional live namespace), and AMQP. Tests are grouped in `Ingress/`, `Dispatch/Inbox/`, `Dispatch/Outbox/`, and `Registration/` subfolders by broker. Fast InMemory wire tests are tagged `TransportFast`. Kafka, LocalStack, and AMQP scenarios use `TransportDocker`. Azure emulator and optional live tests use `TransportAzure`. Registration smoke (`Registration/BrokerDispatchIngressRegistrationTests.cs`) has no category trait and runs in the final Integration Tests CI batch.

Sample v6 composition smoke (`LiteBusV6CompositionSmokeTests`) lives in `LiteBus.Runtime.UnitTests`, not in the durable matrix.

### Ack Policy (Ingress)

`TransportInboxIngressConsumer` discards (does not requeue) when acceptance throws:

- `MessageContractNotRegisteredException`
- `InboxDispatchException` (missing or invalid transport headers)
- `InboxStorageException` (store capacity and persistence rejections)
- `InvalidOperationException`, `ArgumentException`, `FormatException`, `JsonException`

All other failures honor `RequeueOnFailure` (default `true`). Unit coverage: `LiteBus.Inbox.UnitTests/Ingress/TransportInboxIngressConsumerTests.cs`.

### CI Filters

```bash
# Fast subset (no Docker): durable matrix only
dotnet test tests/LiteBus.Durable.IntegrationTests --filter "Category=TransportFast"

# Kafka + LocalStack SQS + AMQP (Docker required)
dotnet test tests/LiteBus.Durable.IntegrationTests --filter "Category=TransportDocker"

# Azure Service Bus emulator + optional live overlay (Docker required for emulator)
dotnet test tests/LiteBus.Durable.IntegrationTests --filter "Category=TransportAzure"

# Same filters across the full solution (matches CI)
dotnet test LiteBus.slnx --filter "Category=TransportFast"
dotnet test LiteBus.slnx --filter "Category=TransportDocker"
dotnet test LiteBus.slnx --filter "Category=TransportAzure"
```

Optional live Azure tests additionally require `LITEBUS_TEST_AZURE_SERVICEBUS_CONNECTION_STRING` and `LITEBUS_TEST_AZURE_SERVICEBUS_QUEUE`.

### Ingress Coverage Matrix

Same legend and rows as [Integration tests: ingress matrix](integration-tests.md#coverage-matrix-ingress). Summary:

| Scenario | InMemory | Kafka | AWS | Azure | AMQP |
|----------|:--------:|:-----:|:---:|:-----:|:----:|
| Happy-path E2E | yes | yes | yes | yes | yes |
| Unknown contract | yes | yes | yes | yes | yes |
| Invalid JSON | yes | yes | yes | yes | yes |
| Store full | yes | yes | yes | yes | yes |
| Duplicate MessageId | yes |: | yes |: | partial |
| Header edge cases | yes |: | partial |: | partial |
| Requeue on/off | yes | partial | yes | partial | partial |

**partial** = subset of scenarios or covered in another project; see [Integration tests](integration-tests.md) for definitions.

### Dispatch Coverage Matrix

| Scenario | InMemory | Kafka | AWS | Azure | AMQP |
|----------|:--------:|:-----:|:---:|:-----:|:----:|
| Inbox E2E | yes | yes | yes | yes | yes |
| Outbox E2E | yes | yes | yes | yes | yes |
| Full header propagation (outbox) | yes | yes | yes | yes | yes |
| Contract-name route fallback | yes | yes | yes | yes | yes |
| Unreachable broker failure |: | yes | yes |: | Transport.IntegrationTests |
| Circuit breaker open |: | yes | yes |: | unit |

### Project Map

| Project | Role |
|---------|------|
| `LiteBus.Durable.IntegrationTests` | Unified broker matrix (`Ingress/`, `Dispatch/`, `Registration/`) |
| `LiteBus.Runtime.UnitTests` | v6 composition smoke (`Runtime/Composition/`) |
| `LiteBus.Transport.IntegrationTesting` | Shared messages, traits, `FlakyInbox`, polling helpers |
| `LiteBus.Transport.IntegrationTests` | AMQP wire protocol (no inbox/outbox) |
| `LiteBus.Storage.IntegrationTests` | PostgreSQL and EF Core storage E2E |
| `LiteBus.Extensions.IntegrationTests` | ASP.NET management, health checks, OpenTelemetry |

## Next

See [Integration Tests](integration-tests.md) for the full project and scenario reference, [Cookbook and Scenarios](../getting-started/cookbook.md), or [Migration Guide v6](../migration/v6.md).
