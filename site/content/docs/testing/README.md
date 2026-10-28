# Testing

LiteBus applications are tested at several levels: one message's pipeline through the mediation harness, processors through in-memory storage, custom adapters through shared contract suites, and broker paths through the durable transport integration matrix. This page covers each level.

For the contributor reference (executor inventory, folder layout, fixtures, environment variables, coverage matrices, and the coverage gate), see [Integration Tests](integration-tests.md).

## Testing One Message's Pipeline

`MediationHarness`, from `LiteBus.Testing.Mediation`, runs the shipped pipeline over handler instances the test builds. It needs no host, container, or database:

```csharp
var result = await MediationHarness.For<CloseOrganizationCommand>()
    .With(new AuthorizationGuard<CloseOrganizationCommand>(metadata, authorizer))
    .RunAsync(command);

result.Outcome.Should().Be(MediationOutcome.Denied);
result.StagesRun.Should().Equal(PreStage.Guard);
result.MainHandlerRan.Should().BeFalse();
```

`StagesRun` records the stages that ran in order. Stages with no registered handler are skipped and absent from the list.

`MediationHarnessResult` also carries `Reason`, `Code`, `Failures`, `Value`, and `MainHandlerRan`, with `IsSuccess`, `IsDenied` and `IsInvalid` for the common assertions. `MainHandlerRan` is separate from the outcome because that is the question a test of a guard asks.

| Member | Use |
| --- | --- |
| `MediationHarness.For<TMessage>()` | Start a harness for one message type |
| `.With(handler)` / `.With(params object[])` | Add handler instances, registered by concrete type exactly as a host would |
| `.WithTags(params string[])` | Run under mediation tags, so tag filtering can be asserted |
| `.RunAsync(message)` | Run a message that produces no result |
| `.RunAsync<TResult>(message)` | Run a message that produces one, and read `Value` |
| `.EvaluateAsync(message)` | Ask the decision stages without performing the message |

Refusals are reported in the result, not thrown. Faults propagate, so a broken handler fails the test.

Each harness builds its own registry, so registrations do not leak between tests. Supply handler instances directly, constructing the handler under test with its test doubles.

What it deliberately leaves out is composition. It registers instances directly, so it proves nothing about a registration a host would reject, a module graph, or a container lifetime. Assert those against a host, which is what [Application Testing](application-testing.md) covers.

## InMemory Storage

The InMemory storage packages implement all store roles in one thread-safe class and use `TimeProvider` for lease expiry simulation. Full setup recipes (inbox and outbox registration, lease and retry simulation through a manual clock, and binding every store role to one shared instance) live in [Testing Durable Messaging](application-testing.md#testing-durable-messaging).

## Shared Store Contract Tests

`LiteBus.Storage.Testing` is a packable test-support package. It defines abstract `InboxStoreContractTests` and `OutboxStoreContractTests` suites that custom adapter authors can inherit in their own xUnit projects. The source project remains under `tests/LiteBus.Storage.Testing/` so the same suites run against LiteBus providers in this repository.

## Shared Transport Contract Tests

`LiteBus.Transport.Testing` is a packable xUnit test-support package for transport adapter authors. Derive from `TransportContractTests` and return an isolated `TransportContractContext` for each scenario. The inherited suite verifies payload and metadata round trips, explicit redelivery, and pre-publication cancellation. LiteBus runs it against the in-memory adapter and RabbitMQ. Third-party adapters should run it against their real broker in CI.

## PostgreSQL Integration Tests

PostgreSQL storage integration tests use Testcontainers with `postgres:16-alpine`. They validate current-version creation, column and type drift, lease fencing, and migration-owned schema contracts.

### Example Registration

```csharp
services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(c => c.Register<MyCommandHandler>());

    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<MyCommand>("my.command", 1);
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
    });
});
```

## Durable Transport Integration Tests

`LiteBus.Durable.IntegrationTests` (`tests/LiteBus.Durable.IntegrationTests/`) is the unified broker matrix for InMemory, Kafka, AWS SQS, Azure Service Bus (emulator plus optional live namespace), and AMQP. Tests are grouped in `Ingress/`, `Dispatch/Inbox/`, `Dispatch/Outbox/`, and `Registration/` subfolders by broker. Fast InMemory wire tests are tagged `TransportFast`.

Kafka, LocalStack, and AMQP scenarios use `TransportDocker`. Azure emulator and optional live tests use `TransportAzure`. Registration smoke (`Registration/BrokerDispatchIngressRegistrationTests.cs`) has no category trait and runs in the final Integration Tests CI batch.

The sample composition smoke test (`LiteBusV6CompositionSmokeTests`) lives in `LiteBus.Runtime.UnitTests`, not in the durable matrix.

AMQP dispatch and ingress tests use Testcontainers with RabbitMQ and LavinMQ images. Wire-level ingress scenarios, including batch accept and broker failure coverage, live in `LiteBus.Durable.IntegrationTests` under `Ingress/Amqp/` and `Dispatch/Inbox/Amqp/`. Low-level AMQP publish/consume/ack behavior without inbox/outbox lives in `LiteBus.Transport.IntegrationTests/Amqp/`.

Scenario matrices, the project map, fixtures, and environment variables live in [Integration Tests](integration-tests.md), which is the only matrix reference.

### Ack Policy (Ingress)

`TransportInboxIngressConsumer` discards (does not requeue) when acceptance throws:

- `MessageContractNotRegisteredException`
- `InboxDispatchException` (missing or invalid transport headers)
- `InboxStorageException` (store capacity and persistence rejections)
- `InvalidOperationException`, `ArgumentException`, `FormatException`, `JsonException`

All other failures honor `RequeueOnFailure` (default `true`). Unit coverage lives in `LiteBus.Inbox.UnitTests/Ingress/TransportInboxIngressConsumerTests.cs`.

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

## Next

See [Integration Tests](integration-tests.md) for the full project and scenario reference, [Documentation Semantic Validation](../contributing/semantic-validation.md) for executable documentation anchors, [Cookbook and Scenarios](../getting-started/cookbook.md), or the [Migration Guide](../migration/v6.md).
