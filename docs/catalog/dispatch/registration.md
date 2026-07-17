# Dispatcher Registration

- **ID**: `dispatch.registration`
- **Name**: Dispatcher registration
- **Maturity**: GA
- **Summary**: Register exactly one inbox and one outbox dispatcher per module through nested `Use*Dispatch` extensions on composite module builders.

## What It Does

Dispatch adapters register as child modules of `InboxModule` or `OutboxModule`. `InboxModuleBuilder.RegisterDispatcher` and `OutboxModuleBuilder.RegisterDispatcher` accept an `IInboxDispatcherModule` or `IOutboxDispatcherModule` implementation. Each broker-specific `Use*Dispatch` extension constructs the matching dispatcher module (and often a transport child module) and passes it to `RegisterDispatcher`.

Calling more than one dispatcher registration method on the same builder throws `LiteBusConfigurationException` at compose time. The shared transport modules also guard against duplicate `IInboxDispatcher` or `IOutboxDispatcher` registrations in the dependency registry.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | `IInboxDispatcherModule`, builder surface |
| `LiteBus.Outbox.Abstractions` | `IOutboxDispatcherModule`, builder surface |
| `LiteBus.Inbox.Dispatch.*` / `LiteBus.Outbox.Dispatch.*` | Concrete dispatcher modules |

## Requires

- `durable-core.inbox` or `durable-core.outbox` (parent module must be registered)
- For in-process dispatch: `mediator.commands` (inbox) or `mediator.events` (outbox)
- For transport dispatch: matching `transport.*` broker module registered by the dispatch extension

## Invariants

- Exactly one `IInboxDispatcher` and one `IOutboxDispatcher` per respective module configuration scope.
- Dispatcher sub-modules declare `IRequires<InboxModule>` or `IRequires<OutboxModule>` for topological ordering.
- Transport dispatch modules require `IMessageTransport` to be registered before the dispatcher (usually via the same `Use*Dispatch` call).

## Non-Goals

- Does not register storage, ingress, or processor loops (those are sibling child modules).
- Does not allow combining in-process and broker dispatch on the same axis in one module (choose one execution target).
- Does not provide a unified `UseTransportDispatch(TransportKind, ...)` meta-API; each broker is a separate package.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddInboxModule(inbox =>
    {
        inbox.EnableInboxProcessor();
        inbox.UseInProcessDispatch();
    });

    litebus.AddOutboxModule(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseKafkaDispatch(
            options => options.DefaultDestination = "events",
            new KafkaTransportOptions { BootstrapServers = "localhost:9092" });
    });
});
```

### `InboxModuleBuilder.RegisterDispatcher(IInboxDispatcherModule)`

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Abstractions` |
| Returns | `InboxModuleBuilder` for chaining |
| Role | Low-level registration; stores the dispatcher child module for `InboxModule.Build()` |

Throws `LiteBusConfigurationException` when a second dispatcher module is registered on the same builder.

### `OutboxModuleBuilder.RegisterDispatcher(IOutboxDispatcherModule)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Abstractions` |
| Returns | `OutboxModuleBuilder` for chaining |
| Role | Low-level registration; stores the dispatcher child module for `OutboxModule.Build()` |

### `InboxModuleBuilder.UseInProcessDispatch()`

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Dispatch.InProcess` |
| Registers | `CommandInboxDispatchModule` to `CommandInboxDispatcher` as `IInboxDispatcher` |
| Requires | `AddCommandModule` and contract registration for handled command types |

### `OutboxModuleBuilder.UseInProcessDispatch()`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch.InProcess` |
| Registers | `EventOutboxDispatchModule` to `EventOutboxDispatcher` as `IOutboxDispatcher` |
| Requires | `AddEventModule` and contract registration for published event types |

### Broker `Use*Dispatch` Extensions (Inbox and Outbox)

| Extension | Package | Transport child |
| --- | --- | --- |
| `UseAmqpDispatch(configure, AmqpConnectionOptions)` | `*.Dispatch.Amqp` | `AmqpTransportModule` |
| `UseAzureServiceBusDispatch(configure, AzureServiceBusTransportOptions)` | `*.Dispatch.AzureServiceBus` | `AzureServiceBusTransportModule` |
| `UseAwsSqsDispatch(configure, AwsSqsTransportOptions)` | `*.Dispatch.AwsSqs` | `AwsSqsTransportModule` |
| `UseKafkaDispatch(configure, KafkaTransportOptions)` | `*.Dispatch.Kafka` | `KafkaTransportModule` |
| `UseInMemoryDispatch(configure?)` | `*.Dispatch.InMemory` | `InMemoryTransportModule` |

Each extension builds `TransportInboxDispatcherOptions` or `TransportOutboxDispatcherOptions`, wraps `TransportInboxDispatchModule` or `TransportOutboxDispatchModule`, and calls `RegisterDispatcher`.

Registration runs inside `AddInboxModule(...)` or `AddOutboxModule(...)` alongside storage and processor enablement. v6 removed flat top-level dispatcher registrars; compose through the parent module builder only.

## Observability

| Signal | Source | When emitted |
| --- | --- | --- |
| No registration-time metrics |: | Compose is silent until the processor runs |
| `send {destination}` activity | `LiteBusTransportTelemetry.PublishOperationName` on activity source `LiteBus.Transport` | Concrete broker publisher around the SDK send call |
| `litebus.inbox.processor.dispatch_duration` histogram | `LiteBusInboxTelemetry.ProcessorDispatchDurationInstrumentName` | Processor wraps every inbox `DispatchAsync` call |
| `litebus.outbox.processor.dispatch_duration` histogram | `LiteBusOutboxTelemetry.ProcessorDispatchDurationInstrumentName` | Processor wraps every outbox `DispatchAsync` call |
| `litebus.inbox.processor.succeeded` / `failed` / `dead_lettered` | Inbox processor pass counters | After dispatch returns or throws |
| `litebus.outbox.processor.published` / `failed` / `dead_lettered` | Outbox processor pass counters | After dispatch returns or throws |
| `litebus.transport.circuit_breaker.*` | `LiteBusTransportTelemetry` meter | Broker publish failures on transport dispatch paths |

Register inbox and outbox meters through `AddLiteBusInboxMetrics()` and `AddLiteBusOutboxMetrics()`. Register the shared transport meter through `AddLiteBusTransportMetrics()`. AMQP applications may use the compatibility alias `AddLiteBusAmqpMetrics()`.

## Analyzers

- **LB1014**: Inbox or outbox processor enabled without a dispatcher in the same module builder scope.

## Deep Docs

- [Inbox.md](../../reliable-messaging/inbox.md)
- [Outbox.md](../../reliable-messaging/outbox.md)
- [Dependency-Graph.md](../../architecture/dependency-graph.md)
- [Migration-Guide-v6.md](../../migration/v6.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `AddInboxInProcessDispatcher_ShouldRegisterCommandInboxDispatcher` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddInboxInProcessDispatcher_WhenCalledTwice_ShouldThrow` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddInboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow` | `LiteBus.Inbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddInboxModule_WithNestedStorageAndDispatcher_ShouldResolveInboxServices` | `LiteBus.Inbox.UnitTests` |
| `AddOutboxInProcessDispatcher_ShouldRegisterInProcessOutboxDispatcher` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddOutboxInProcessDispatcher_WhenCalledTwice_ShouldThrow` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `AddOutboxInProcessDispatcher_WhenAnotherDispatcherRegistered_ShouldThrow` | `LiteBus.Outbox.UnitTests` (`Dispatch/InProcess/`) |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) | InMemory, AMQP, Azure, AWS SQS, Kafka (Theory data) |
| `InboxModuleBuilderAwsDispatchExtensions_should_expose_use_aws_sqs_dispatch` | `LiteBus.Inbox.UnitTests` (`Dispatch/AwsSqs/`) | AWS SQS public API surface |
| `OutboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) | InMemory, AMQP, Azure, AWS SQS, Kafka (Theory data) |
| `UseAmqpDispatch_WithAmqpTransportModule_ShouldRegisterTransportOutboxDispatcher` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`) | AMQP module wiring |
| `ProcessorBackgroundService_WhenDispatcherMissing_ShouldThrowOnBuild` | `LiteBus.Inbox.UnitTests` |

### Untested

- Autofac `AddLiteBus` registration parity.
- Duplicate `IInboxDispatcher` or `IOutboxDispatcher` via low-level registry bypasses.
- LB1014 analyzer behavior in dispatch test projects (covered in analyzers unit tests).

### Out-of-Scope

- Registering storage, ingress, or processor loops (sibling child modules)
- Combining in-process and broker dispatch on the same inbox or outbox module
- Unified `UseTransportDispatch(TransportKind, ...)` meta-API across brokers
- Flat v5-style top-level dispatcher registrars (removed in v6)
