# AMQP Transport

**Production tier: GA**

`LiteBus.Transport.Amqp` wraps RabbitMQ.Client for publish and consume. Inbox and outbox adapters compose it independently; installing AMQP transport does not pull Kafka or AWS SDKs.

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Transport.Amqp` | `ITransportPublisher`, `AmqpConsumer`, `AmqpConnectionOptions` |
| `LiteBus.Inbox.Dispatch.Amqp` | Outbound command dispatch from inbox processor |
| `LiteBus.Outbox.Dispatch.Amqp` | Outbound event publish from outbox processor |
| `LiteBus.Inbox.Ingress.Amqp` | Broker intake into `IInbox.AcceptAsync` |
| `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` | Optional `AddLiteBusAmqpMetrics()` registration |

Add inbox/outbox core and storage packages as usual. See [Dependency graph](../architecture/dependency-graph.md).

## Registration

```csharp
var connection = new AmqpConnectionOptions
{
    HostName = "localhost",
    UserName = "guest",
    Password = "guest"
};

builder.AddMessaging(_ => { });
builder.AddCommands(c => c.RegisterFromAssembly(typeof(ShipOrderCommandHandler).Assembly));
builder.AddAmqpTransport(connection);

builder.AddInbox(inbox =>
{
    inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
    inbox.UsePostgreSqlStorage(connectionString);
    inbox.UseInProcessDispatch();

    inbox.UseAmqpDispatch(transport => transport.DefaultDestination = "orders.commands");

    inbox.UseAmqpIngress(ingress =>
    {
        ingress.UseOptions(new AmqpInboxIngressOptions
        {
            QueueName = "orders.commands",
            PrefetchCount = 10,
            RequeueOnFailure = true,
            Safety = new TransportInboxIngressSafetyOptions
            {
                MaxInFlightMessages = 8,
                BatchSize = 10
            }
        });
    });

    inbox.EnableInboxProcessor();
});
```

`AddAmqpTransport` owns the broker connection at the root. Dispatch and ingress require that module and share its publisher and consumer.

Set the destination to `string.Empty` to publish through RabbitMQ's default exchange and set the route to the target
queue name. LiteBus uses that queue route as the publisher circuit scope. Named exchanges use the exchange name, so a
failure on one exchange does not open another exchange's circuit.

## Wire Headers

Dispatch copies durable envelope metadata through `TransportEnvelopeHeaderMapper` using canonical names from `TransportHeaders` (`litebus-message-id`, `litebus-contract-name`, `correlation-id`, `litebus-idempotency-key`, `litebus-visible-after`, and related fields). Ingress maps those headers back into `InboxAcceptItem` metadata.

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| At-least-once when `basic.ack` follows successful accept | Exactly-once side effects |
| `basic.nack` with requeue on `ReturnToQueueAsync` | Ordering across competing consumers |
| Poison drain when `RequeueOnFailure = false` | Cross-broker federation |

Handler exceptions propagate to ingress, which applies `RequeueOnFailure` through `ReturnToQueueAsync` or `DiscardAsync`.

## Operations

| Symptom | Check | Action |
| --- | --- | --- |
| Messages requeued indefinitely | Consumer prefetch, store health | Fix accept failure; verify idempotency keys |
| No ingress deliveries | Queue name, vhost, credentials | Confirm queue exists and consumer tag is unique |
| Circuit breaker open | `litebus.amqp.circuit_breaker.open` metric | Inspect broker connectivity and backoff settings |

## Tests

| Scenario | Location |
| --- | --- |
| Publish/consume round-trip | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| Ingress end-to-end | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| PostgreSQL + AMQP reliable chain | `LiteBus.Storage.IntegrationTests` (`PostgreSql/`) |

## Related Docs

* [Inbox AMQP ingress](inbox-amqp-ingress.md)
* [Outbox AMQP dispatch](outbox-amqp-dispatch.md)
* [Kafka transport](kafka.md)
* [Reliable messaging](../reliable-messaging/README.md)
