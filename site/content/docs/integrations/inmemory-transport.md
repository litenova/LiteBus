# In-Memory Transport

`LiteBus.Transport.InMemory` is a process-local channel broker for tests and local workflows. It implements the same `ITransportPublisher` and `IMessageConsumer` contracts as the external brokers, so dispatch and ingress code paths run unchanged without a broker. It is not durable: queued messages live in process memory and do not survive a restart.

**Production tier: GA** for tests and local workflows. Not a production broker replacement: no durability, no cross-process delivery.

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Transport.InMemory` | `ITransportPublisher`, `InMemoryConsumer`, `InMemoryTransportBroker` |
| `LiteBus.Inbox.Dispatch.InMemory` | Outbound command dispatch from inbox processor |
| `LiteBus.Outbox.Dispatch.InMemory` | Outbound event publish from outbox processor |
| `LiteBus.Inbox.Ingress.InMemory` | In-process intake into `IInbox.AcceptAsync` |

Add inbox or outbox core and a storage package alongside the transport. The transport alone persists nothing.

## Registration

```csharp
builder.AddInMemoryTransport(new InMemoryTransportOptions
{
    DestinationCapacity = 256
});
builder.AddMessaging(_ => { });
builder.AddInbox(inbox =>
{
    inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
    inbox.UseInMemoryStorage();
    inbox.UseInMemoryDispatch(dispatch => dispatch.DefaultDestination = "orders.commands");
    inbox.UseInMemoryIngress(ingress =>
    {
        ingress.UseOptions(new InMemoryInboxIngressOptions
        {
            Destination = "orders.commands",
            RequeueOnFailure = true
        });
    });
    inbox.EnableInboxProcessor();
});
```

`AddInMemoryTransport` registers the shared broker at the root. Dispatch and ingress require that module. Register exactly one dispatcher per inbox or outbox: a second dispatcher registration throws `LiteBusConfigurationException`. Omit the options argument to use the default capacity of 1024 unsettled deliveries per destination.

## Options Reference

| Type | Property | Default | Notes |
| --- | --- | --- | --- |
| `InMemoryTransportOptions` | `DestinationCapacity` | `1024` | Maximum queued and in-flight deliveries admitted per destination. Must be positive; validated during composition. |
| `InMemoryInboxIngressOptions` | `Destination` | `""` (required) | Logical queue name the ingress consumer subscribes to |
| | `RequeueOnFailure` | `true` | Re-enqueue with `Redelivered = true` on transient accept failure |
| | `Safety.MaxInFlightMessages` | `32` requested | Enforced bound is 1: the in-memory adapter runs one handler at a time |

When a destination reaches capacity, `PublishAsync` waits asynchronously until a delivery settles or the caller cancels. The transport does not drop old or new writes. `AcceptAsync` and `DiscardAsync` release the delivery's capacity reservation; `ReturnToQueueAsync` re-enqueues with `Redelivered = true` and retains the reservation, so a waiting publisher cannot take the only slot and deadlock redelivery.

A handler that returns successfully without settling is treated as unprocessed: the delivery is returned to the destination with `Redelivered = true` instead of being dropped, so always settle with `AcceptAsync`, `DiscardAsync`, or `ReturnToQueueAsync` before returning.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| At-least-once delivery within the process | Durability across restarts |
| Same `TransportMessage` contract as external brokers | Delivery across processes or hosts |
| Full destinations wait for capacity; nothing is dropped | Production broker behavior under load |

## Operations

| Symptom | Check | Action |
| --- | --- | --- |
| Publisher stalls | Destination depth, consumer running | Start the ingress consumer; drain the destination before raising capacity |
| Same message reprocessed repeatedly | Accept failure logs | Fix root accept failure; redelivery retains its capacity slot by design |

Shared circuit-breaker gauges are tagged `litebus.transport.broker="inmemory"` on the `LiteBus.Transport` meter.

## Tests

| Scenario | Location |
| --- | --- |
| Publish and consume delivery | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| Requeue redelivery and handler-throw requeue | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| Full destination waits; canceled wait releases | `LiteBus.Transport.UnitTests` (`InMemory/`) |
| Ingress end-to-end | `LiteBus.Durable.IntegrationTests` (`Ingress/InMemory/`) |
| Inbox and outbox dispatch round-trip | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/InMemory/`, `Dispatch/Outbox/InMemory/`) |

## Related Docs

* [AMQP Transport](amqp.md)
* [Reliable Messaging](../reliable-messaging/README.md)
* [Integration Tests](../testing/integration-tests.md)
