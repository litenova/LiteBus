# Dispatcher Registration

- **ID**: `dispatch.registration`
- **Name**: Dispatcher registration
- **Maturity**: GA
- **Summary**: Register exactly one inbox and one outbox dispatcher per module through nested `Use*Dispatch` extensions on composite module builders.

## What It Does

Dispatch adapters register as child modules of `InboxModule` or `OutboxModule`. `InboxModuleBuilder.RegisterDispatcher` and `OutboxModuleBuilder.RegisterDispatcher` accept an `IInboxDispatcherModule` or `IOutboxDispatcherModule` implementation. Each broker-specific `Use*Dispatch` extension constructs the matching dispatcher module, which requires a transport registered once at the root.

If you call more than one dispatcher registration method on the same builder, composition throws `DurableStorageConfigurationException` at compose time. Module graph validation ensures the required parents and transports exist before any dispatcher module builds.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | `IInboxDispatcherModule` contract |
| `LiteBus.Outbox.Abstractions` | `IOutboxDispatcherModule` contract |
| `LiteBus.Inbox`, `LiteBus.Outbox` | Concrete module builder surfaces |
| `LiteBus.Inbox.Dispatch.*` and `LiteBus.Outbox.Dispatch.*` | Concrete dispatcher modules |

## Requires

- `durable-core.inbox` or `durable-core.outbox` (parent module must be registered)
- In-process dispatch also needs `mediator.commands` (inbox) or `mediator.events` (outbox)
- Transport dispatch also needs the matching `transport.*` broker module registered at the root

## Invariants

- Each inbox module scope holds exactly one `IInboxDispatcher`, and each outbox module scope holds exactly one `IOutboxDispatcher`.
- Dispatcher sub-modules declare `IRequires<InboxModule>` or `IRequires<OutboxModule>` for topological ordering.
- Transport dispatch modules declare the matching root transport module as a graph dependency.

## Non-Goals

- It does not register storage, ingress, or processor loops. Those are sibling child modules.
- It does not allow combining in-process and broker dispatch on the same axis in one module. Choose one execution target.
- It does not provide a unified `UseTransportDispatch(TransportKind, ...)` meta-API. Each broker is a separate package.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddKafkaTransport(new KafkaTransportOptions { BootstrapServers = "localhost:9092" });

    litebus.AddInbox(inbox =>
    {
        inbox.EnableInboxProcessor();
        inbox.UseInProcessDispatch();
    });

    litebus.AddOutbox(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseKafkaDispatch(options => options.DefaultDestination = "events");
    });
});
```

### `InboxModuleBuilder.RegisterDispatcher(IInboxDispatcherModule)`

| | |
| --- | --- |
| Package | `LiteBus.Inbox` |
| Returns | `InboxModuleBuilder` for chaining |
| Role | Low-level registration that stores the dispatcher child module for `InboxModule.Build()` |

It throws `DurableStorageConfigurationException` when a second dispatcher module is registered on the same builder.

### `OutboxModuleBuilder.RegisterDispatcher(IOutboxDispatcherModule)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox` |
| Returns | `OutboxModuleBuilder` for chaining |
| Role | Low-level registration that stores the dispatcher child module for `OutboxModule.Build()` |

### `InboxModuleBuilder.UseInProcessDispatch()`

| | |
| --- | --- |
| Package | `LiteBus.Inbox.Dispatch.InProcess` |
| Registers | `CommandInboxDispatcher` as the `IInboxDispatcher`, through `CommandInboxDispatchModule` |
| Requires | `AddCommands` and contract registration for handled command types |

### `OutboxModuleBuilder.UseInProcessDispatch(Action<InProcessOutboxDispatchOptionsBuilder>? configure = null)`

| | |
| --- | --- |
| Package | `LiteBus.Outbox.Dispatch.InProcess` |
| Registers | `EventOutboxDispatcher` as the `IOutboxDispatcher`, through `EventOutboxDispatchModule` |
| Requires | `AddEvents` and contract registration for published event types |
| Options | `RequireHandler` (default `true`) fails the attempt when no handler is registered |

### Broker `Use*Dispatch` Extensions (Inbox and Outbox)

| Extension | Package | Required root transport |
| --- | --- | --- |
| `UseAmqpDispatch(configure)` | `*.Dispatch.Amqp` | `AddAmqpTransport(...)` |
| `UseAzureServiceBusDispatch(configure)` | `*.Dispatch.AzureServiceBus` | `AddAzureServiceBusTransport(...)` |
| `UseAwsSqsDispatch(configure)` | `*.Dispatch.AwsSqs` | `AddAwsSqsTransport(...)` |
| `UseKafkaDispatch(configure)` | `*.Dispatch.Kafka` | `AddKafkaTransport(...)` |
| `UseInMemoryDispatch(configure?)` | `*.Dispatch.InMemory` | `AddInMemoryTransport()` |

Each extension builds `TransportInboxDispatcherOptions` or `TransportOutboxDispatcherOptions`, wraps `TransportInboxDispatchModule` or `TransportOutboxDispatchModule`, and calls `RegisterDispatcher`.

Registration runs inside `AddInbox(...)` or `AddOutbox(...)` alongside storage and processor enablement. Compose through the parent module builder only.

## Observability

| Signal | Source | When emitted |
| --- | --- | --- |
| No registration-time metrics | None | Composition emits no metrics until the processor runs |
| `send {destination}` activity | `LiteBusTransportTelemetry.PublishOperationName` on activity source `LiteBus.Transport` | The concrete broker publisher emits it around the SDK send call |
| `litebus.inbox.processor.dispatch_duration` histogram | `LiteBusInboxTelemetry.ProcessorDispatchDurationInstrumentName` | The processor wraps every inbox `DispatchAsync` call |
| `litebus.outbox.processor.dispatch_duration` histogram | `LiteBusOutboxTelemetry.ProcessorDispatchDurationInstrumentName` | The processor wraps every outbox `DispatchAsync` call |
| `litebus.inbox.processor.succeeded` / `failed` / `dead_lettered` | Inbox processor pass counters | After dispatch returns or throws |
| `litebus.outbox.processor.published` / `failed` / `dead_lettered` | Outbox processor pass counters | After dispatch returns or throws |
| `litebus.transport.circuit_breaker.*` | `LiteBusTransportTelemetry` meter | Incremented when broker publishes fail on transport dispatch paths |

Register inbox and outbox meters through `AddLiteBusInboxMetrics()` and `AddLiteBusOutboxMetrics()`. Register the shared transport meter through `AddLiteBusTransportMetrics()`. AMQP applications may use the compatibility alias `AddLiteBusAmqpMetrics()`.

## Analyzers

- **LB1014**: Inbox or outbox processor enabled without a dispatcher in the same module builder scope.

## References

- [Inbox.md](../../reliable-messaging/inbox.md)
- [Outbox.md](../../reliable-messaging/outbox.md)
- [Dependency-Graph.md](../../architecture/dependency-graph.md)
- [Migration Guide](../../migration/v6.md)

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
| `OutboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) | InMemory, AMQP, Azure, AWS SQS, Kafka (Theory data) |
| `UseAmqpDispatch_WithAmqpTransportModule_ShouldRegisterTransportOutboxDispatcher` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`) | AMQP module wiring |
| `ProcessorBackgroundService_WhenDispatcherMissing_ShouldThrowOnBuild` | `LiteBus.Inbox.UnitTests` |

### Untested

- Autofac `AddLiteBus` registration parity.
- Duplicate `IInboxDispatcher` or `IOutboxDispatcher` registrations through low-level registry bypasses.
- LB1014 analyzer behavior in dispatch test projects, which the analyzer unit tests cover.

### Out-of-Scope

- Registering storage, ingress, or processor loops, which are sibling child modules
- Combining in-process and broker dispatch on the same inbox or outbox module
- Unified `UseTransportDispatch(TransportKind, ...)` meta-API across brokers
- Flat top-level dispatcher registrars
