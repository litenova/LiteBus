# AMQP Transport

`LiteBus.Transport.Amqp` publishes and consumes through an AMQP 0.9.1 broker such as RabbitMQ or LavinMQ with the `RabbitMQ.Client` SDK. Inbox and outbox adapters compose it independently: installing the AMQP transport does not pull Kafka, AWS, or Azure SDKs.

**Production tier: GA**

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Transport.Amqp` | `ITransportPublisher`, `AmqpConsumer`, `AmqpConnectionOptions` |
| `LiteBus.Inbox.Dispatch.Amqp` | Outbound command dispatch from inbox processor |
| `LiteBus.Outbox.Dispatch.Amqp` | Outbound event publish from outbox processor |
| `LiteBus.Inbox.Ingress.Amqp` | Broker intake into `IInbox.AcceptAsync` |
| `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` | Optional `AddLiteBusAmqpMetrics()` registration |

Add inbox or outbox core and a storage package alongside the transport. Durable processing needs a store and a processor host; the transport alone persists nothing. See [Dependency Graph](../architecture/dependency-graph.md).

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
    inbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
    inbox.UseAmqpDispatch(transport => transport.DefaultDestination = "orders.commands");

    inbox.UseAmqpIngress(ingress =>
    {
        ingress.UseOptions(new AmqpInboxIngressOptions
        {
            QueueName = "orders.commands",
            PrefetchCount = 10,
            RequeueOnFailure = true
        });
    });

    inbox.EnableInboxProcessor();
});
```

`AddAmqpTransport` owns the broker connection at the root. Dispatch and ingress require that module and share its publisher and consumer. Register exactly one dispatcher per inbox or outbox: a second dispatcher registration throws `LiteBusConfigurationException`.

Set the destination to `string.Empty` to publish through RabbitMQ's default exchange and set the route to the target queue name. LiteBus uses that queue route as the publisher circuit scope. Named exchanges use the exchange name, so a failure on one exchange does not open another exchange's circuit.

Neutral consumers started through `IMessageConsumer` with default `TransportConsumerOptions` use unlimited AMQP prefetch: `PrefetchCount` defaults to `0`, which maps to RabbitMQ's unlimited native prefetch window. The `MaxInFlightMessages` admission limit bounds concurrent handlers but does not stop the broker from pushing prefetched deliveries into client memory, so set an explicit `PrefetchCount` when consuming from a large backlog with slow handlers.

The `StartAsync` cancellation token cancels subscription startup only. Cancelling or disposing it after startup completes does not affect deliveries; call `StopAsync` to end the subscription.

## Options Reference

| Type | Property | Default | Notes |
| --- | --- | --- | --- |
| `AmqpConnectionOptions` | `Uri` | `null` | When set, overrides `HostName`, `Port`, `UserName`, `Password`, and `VirtualHost`. Must be absolute, use the `amqp` or `amqps` scheme, and include a host. |
| | `HostName` | `"localhost"` | Broker host name. Required unless `Uri` is set. |
| | `Port` | `5672` | Broker port. Valid range is 1 through 65535. |
| | `VirtualHost` | `"/"` | AMQP virtual host. |
| | `UserName` | `"guest"` | Broker credential. |
| | `Password` | `"guest"` | Broker credential. |
| | `ClientProvidedName` | `null` | Optional connection name shown in broker management tools. |
| | `AutomaticRecoveryEnabled` | `true` | Recreate dropped connections and channels. |
| | `NetworkRecoveryInterval` | 5 s | Delay between recovery attempts. Must be positive when recovery is enabled. |
| | `CircuitBreaker.FailureThreshold` | `5` | Consecutive failures before the circuit opens. `0` disables the breaker. Copied into the connection breaker and each publisher circuit. |
| | `CircuitBreaker.BreakDuration` | 30 s | How long broker operations are rejected after the circuit opens. Must be positive when the threshold is above `0`. |
| `AmqpInboxIngressOptions` | `QueueName` | `""` (required) | Queue the ingress consumer subscribes to. |
| | `PrefetchCount` | `10` | Broker-side prefetch window. |
| | `DeclareQueue` | `true` | Declare the queue before consuming. |
| | `DurableQueue` | `true` | Declare the queue as durable. |
| | `RequeueOnFailure` | `true` | `basic.nack` with requeue on transient accept failure; poison failures are still discarded. |
| | `Safety.MaxInFlightMessages` | `32` requested | Enforced bound is 1: the AMQP adapter runs one handler at a time. |

## Connection Recovery

Recovery is configured on `AmqpConnectionOptions`: `AutomaticRecoveryEnabled` (default `true`) with a `NetworkRecoveryInterval` of 5 seconds. Recovery recreates the connection and its channels after a drop. Deliveries that were in flight when the connection dropped are redelivered by the broker rather than resumed, so handlers must be idempotent.

`TransportReconnectOptions` in `LiteBus.Transport` carries the same two settings as the shared reconnect shape; AMQP recovery itself is configured through `AmqpConnectionOptions`.

## Wire Headers

Dispatch copies durable envelope metadata through `TransportEnvelopeHeaderMapper` using canonical names from `TransportHeaders` (`litebus-message-id`, `litebus-contract-name`, `correlation-id`, `litebus-idempotency-key`, `litebus-visible-after`, and related fields). Ingress maps those headers back into `InboxAcceptItem` metadata.

For the transport-level acknowledgement mapping (`AcceptAsync` to `basic.ack`, and the nack variants), see the [AMQP transport contract](../catalog/transport/amqp.md).

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| At-least-once when `basic.ack` follows successful accept | Exactly-once side effects |
| `basic.nack` with requeue on `ReturnToQueueAsync` | Ordering across competing consumers |
| Poison drain when `RequeueOnFailure = false` | Cross-broker federation |

Handler exceptions propagate to ingress, which applies `RequeueOnFailure` through `ReturnToQueueAsync` or `DiscardAsync`. Poison failures (unknown contract, oversize body, malformed payload) are never requeued.

With `Mandatory = true` (the default on dispatch options), a publication the broker returns as unroutable fails with `AmqpMessageReturnedException`, so the leased envelope retries instead of being silently dropped. Set `Mandatory = false` to let the broker drop unroutable messages while publication succeeds.

Each delivery settles at most once: a repeat acknowledgement for the same delivery is ignored, and the consumer skips its failure-path requeue when the handler already settled before throwing.

## Operations

| Symptom | Check | Action |
| --- | --- | --- |
| Messages requeued indefinitely | Consumer prefetch, store health | Fix accept failure; verify idempotency keys |
| No ingress deliveries | Queue name, vhost, credentials | Confirm queue exists and consumer tag is unique |
| Circuit breaker open | `litebus.transport.circuit_breaker.open` gauge with `litebus.transport.broker="amqp"` | Inspect broker connectivity and backoff settings |

## Tests

| Scenario | Location |
| --- | --- |
| Publish/consume round-trip | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| Mandatory unroutable publish failure | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| Ingress end-to-end | `LiteBus.Durable.IntegrationTests` (`Ingress/Amqp/`) |
| PostgreSQL + AMQP reliable chain | `LiteBus.Storage.IntegrationTests` (`PostgreSql/`) |

## Related Docs

* [Inbox AMQP Ingress](inbox-amqp-ingress.md)
* [Outbox AMQP Dispatch](outbox-amqp-dispatch.md)
* [Kafka Transport](kafka.md)
* [Reliable Messaging](../reliable-messaging/README.md)
