# Testing

This page covers testing LiteBus: the mediation harness for testing one message's pipeline, in-memory storage for fast unit tests, shared store contract harnesses, Testcontainers for PostgreSQL and AMQP integration tests, and the durable transport integration matrix.

For a full inventory of integration test projects, fixtures, scenarios, and CI filters, see [Integration Tests](integration-tests.md). Pull request and release workflows collect every test batch with the repository run settings and require at least 90 percent merged source-line coverage.

## Testing One Message's Pipeline

`MediationHarness`, from `LiteBus.Testing.Mediation`, runs the shipped pipeline over handler instances the test builds. No host, no container, no database:

```csharp
var result = await MediationHarness.For<CloseOrganizationCommand>()
    .With(new AuthorizationGuard<CloseOrganizationCommand>(metadata, authorizer))
    .RunAsync(command);

result.Outcome.Should().Be(MediationOutcome.Denied);
result.StagesRun.Should().Equal(PreStage.Guard);
result.MainHandlerRan.Should().BeFalse();
```

`StagesRun` is the assertion the harness exists for. When the point of the library is that behavior moved into named stages, which stages ran is what a test of that behavior wants to say, and only the stage runner knows it. A stage with no registered handler is skipped by the pipeline and absent from the list, so the sequence reads as what happened rather than what could have.

`MediationHarnessResult` also carries `Reason`, `Code`, `Failures`, `Value`, and `MainHandlerRan`, with `IsSuccess`, `IsDenied` and `IsInvalid` for the common assertions. `MainHandlerRan` is separate from the outcome because it is the question a test of a guard is actually asking.

| Member | Use |
| --- | --- |
| `MediationHarness.For<TMessage>()` | Start a harness for one message type |
| `.With(handler)` / `.With(params object[])` | Add handler instances, registered by concrete type exactly as a host would |
| `.WithTags(params string[])` | Run under mediation tags, so tag filtering can be asserted |
| `.RunAsync(message)` | Run a message that produces no result |
| `.RunAsync<TResult>(message)` | Run a message that produces one, and read `Value` |
| `.EvaluateAsync(message)` | Ask the decision stages without performing the message |

A refusal is reported in the result rather than raised, because a test asserting a denial should not have to catch one. A genuine fault still propagates, so a broken handler fails the test as a fault rather than as an outcome.

Each harness builds its own registry, so a guard registered in one test cannot reach the next, and handler instances are supplied rather than resolved, which is the point: build the one guard under test with the doubles you want.

What it deliberately leaves out is composition. It registers instances directly, so it proves nothing about a registration a host would reject, a module graph, or a container lifetime. Assert those against a host, which is what [Application Testing](application-testing.md) covers.

## Coverage Gate

`coverlet.runsettings` is the single collector configuration for local, pull request, and release runs. It emits JSON for the repository gate and Cobertura for Codecov and human-readable reports. The collector excludes test, sample, benchmark, and test-support assemblies.

`scripts/Test-CoverageThreshold.ps1` merges executable line identities by source document and line number across every JSON report. It also merges branch identities and reports branch coverage. The required threshold is 90 percent for lines under `src/`. A line covered by any unit, transport, or integration batch counts once. A line present but uncovered in every batch also counts once. This avoids both duplicate counting and the false pass produced by averaging per-process percentages.

CI writes isolated reports under `artifacts/coverage/` for these batches:

- unit tests;
- fast transport integration tests;
- Docker-backed durable and wire transport tests;
- Azure Service Bus emulator tests;
- the remaining integration suite.

After all batches finish, CI runs:

```powershell
./scripts/Test-CoverageThreshold.ps1 -Root artifacts/coverage -LineThreshold 90
```

For the same full-suite check locally, with Docker available, run:

```powershell
./scripts/run-coverage.ps1 -ResultsDirectory ./coverage -SkipReport -EnforceThreshold
```

Do not combine reports produced from different source revisions or build states. Source line identities can move between builds and make such a merge invalid.
## InMemory Storage

The InMemory storage packages implement all store roles in one thread-safe class. They use `TimeProvider` for lease expiry simulation.

### Inbox Unit Test Setup

```csharp
services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddCommands(c => c.Register<ProcessPaymentCommandHandler>());
    builder.AddInbox(inbox =>
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
    builder.AddMessaging(_ => { });
    builder.AddEvents(e => e.Register<OrderSubmittedEventHandler>());
    builder.AddOutbox(outbox =>
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

`LiteBus.Storage.Testing` is a packable test-support package. It defines abstract `InboxStoreContractTests` and `OutboxStoreContractTests` suites that custom adapter authors can inherit in their own xUnit projects. The source project remains under `tests/LiteBus.Storage.Testing/` so the same suites run against LiteBus providers in this repository.

## Shared Transport Contract Tests

`LiteBus.Transport.Testing` is a packable xUnit test-support package for transport adapter authors. Derive from `TransportContractTests` and return an isolated `TransportContractContext` for each scenario. The inherited suite verifies payload and metadata round trips, explicit redelivery, and pre-publication cancellation. LiteBus runs it against the in-memory adapter and RabbitMQ; third-party adapters should run it against their real broker in CI.

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

## AMQP Integration Tests

AMQP dispatch and ingress tests use Testcontainers with RabbitMQ and LavinMQ images. Wire-level ingress scenarios (including batch accept and the richest failure suite) live in `LiteBus.Durable.IntegrationTests` under `Ingress/Amqp/` and `Dispatch/Inbox/Amqp/`. Low-level AMQP publish/consume/ack behavior without inbox/outbox lives in `LiteBus.Transport.IntegrationTests/Amqp/`.

## Durable Transport Integration Tests

`LiteBus.Durable.IntegrationTests` (`tests/LiteBus.Durable.IntegrationTests/`) is the unified broker matrix for InMemory, Kafka, AWS SQS, Azure Service Bus (emulator plus optional live namespace), and AMQP. Tests are grouped in `Ingress/`, `Dispatch/Inbox/`, `Dispatch/Outbox/`, and `Registration/` subfolders by broker. Fast InMemory wire tests are tagged `TransportFast`. Kafka, LocalStack, and AMQP scenarios use `TransportDocker`. Azure emulator and optional live tests use `TransportAzure`. Registration smoke (`Registration/BrokerDispatchIngressRegistrationTests.cs`) has no category trait and runs in the final Integration Tests CI batch.

The sample composition smoke test (`LiteBusV6CompositionSmokeTests`) lives in `LiteBus.Runtime.UnitTests`, not in the durable matrix.

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
| `LiteBus.Runtime.UnitTests` | Sample composition smoke (`Runtime/Composition/`) |
| `LiteBus.Transport.IntegrationTesting` | Shared messages, traits, `FlakyInbox`, polling helpers |
| `LiteBus.Transport.Testing` | Published transport adapter contract suite (not a test executor) |
| `LiteBus.Transport.IntegrationTests` | AMQP wire protocol (no inbox/outbox) |
| `LiteBus.Storage.IntegrationTests` | PostgreSQL and EF Core storage E2E |
| `LiteBus.Extensions.IntegrationTests` | ASP.NET management, health checks, OpenTelemetry |

## Next

See [Integration Tests](integration-tests.md) for the full project and scenario reference, [Documentation Semantic Validation](../reference/semantic-validation.md) for executable documentation anchors, [Cookbook and Scenarios](../getting-started/cookbook.md), or the [Migration Guide](../migration/v6.md).
