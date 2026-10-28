# Inbox AMQP Dispatch

- **ID**: `dispatch.inbox.amqp`
- **Name**: Inbox AMQP dispatch
- **Maturity**: GA
- **Summary**: Publishes leased inbox envelopes to RabbitMQ or LavinMQ through `TransportInboxDispatcher` and `AmqpTransportModule`.

## What It Does

`AddAmqpTransport(...)` registers the horizontal AMQP infrastructure once at the root. `InboxModuleBuilder.UseAmqpDispatch(...)` registers only `TransportInboxDispatchModule`, which requires that root module. The inbox processor keeps the durable lease and retry behavior, but the dispatch step publishes to AMQP instead of calling local command handlers.

The common deployment pairs a producer service, which runs inbox storage and the processor, with a separate worker service that consumes the queue and accepts messages into its own inbox. The processor records completion only after it persists the outcome to the store, so this path delivers each message at least once.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddAmqpTransport(new AmqpConnectionOptions
    {
        HostName = "localhost",
        Port = 5672,
        VirtualHost = "/",
        UserName = "guest",
        Password = "guest"
    });

    litebus.AddInbox(inbox =>
    {
        inbox.EnableInboxProcessor();
        inbox.UseAmqpDispatch(
            options =>
            {
                options.DefaultDestination = "commands.exchange";
                options.ResolveRoute = envelope => envelope.ContractName;
                options.Persistent = true;
                options.Mandatory = true;
            });
    });
});
```

| API | Role |
| --- | --- |
| `InboxModuleBuilder.UseAmqpDispatch(Action<TransportInboxDispatcherOptions>)` | Registers the transport inbox dispatcher, which requires the root AMQP transport |
| `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)` | Resolves the route, maps the headers, and publishes through `ITransportPublisher` |

`TransportInboxDispatcherOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `DefaultDestination` | `""` | Exchange name (`""` uses the default direct exchange) |
| `ContentType` | `application/json` | MIME value written to the publish request |
| `Persistent` | `true` | Requests broker-persistent delivery |
| `Mandatory` | `true` | Fails the publish with `AmqpMessageReturnedException` when the message is unroutable |
| `ResolveRoute` | `null` | Per-envelope route override delegate |
| `ValidatePayloadBeforeDispatch` | `false` | Deserializes the payload before publish to detect a contract mismatch |

`AmqpConnectionOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `Uri` | `null` | Full AMQP URI that overrides the host, port, and credentials when set |
| `HostName` | `localhost` | Broker host name |
| `Port` | `5672` | Broker port |
| `VirtualHost` | `/` | AMQP virtual host |
| `UserName` / `Password` | `guest` / `guest` | Broker credentials |
| `ClientProvidedName` | `null` | Connection display name shown in the broker UI |
| `AutomaticRecoveryEnabled` | `true` | Enables reconnect in the RabbitMQ client |
| `NetworkRecoveryInterval` | `00:00:05` | Delay between reconnect attempts |
| `CircuitBreaker` | configured object | Separate thresholds for the connection breaker and the per-exchange publisher breakers |

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch.Amqp` | Inbox AMQP registration extension |
| `LiteBus.Inbox.Dispatch` | Shared transport inbox dispatcher |
| `LiteBus.Transport.Amqp` | AMQP transport adapter and OpenTelemetry registration |

## Requires

- `dispatch.registration`
- `dispatch.transport-core`
- `transport.amqp`
- `durable-core.inbox`

## Invariants

- Only one `IInboxDispatcher` can be registered in an inbox module builder scope.
- `TransportInboxDispatchModule<AmqpTransportModule>` requires the root AMQP transport module. Composition fails when `AddAmqpTransport` is missing, so no dispatch runs without an `ITransportPublisher`.
- Route resolution order is tenant strategy, then `ResolveRoute`, then `ContractName`.
- Processor semantics stay at-least-once. A successful AMQP publish alone does not mark the envelope complete.
- A mandatory publication the broker returns as unroutable throws `AmqpMessageReturnedException`, so the envelope stays leased and is retried instead of being silently dropped.

## Non-Goals

- AMQP queue consumption and accept semantics, which the `ingress.amqp` capability handles.
- Combining in-process and AMQP dispatch on the same inbox module.
- Broker-side queue topology management beyond publishing to the configured destination and route.

## Observability

| Signal | Name | Tags |
| --- | --- | --- |
| Activity | `send {destination}` | `messaging.destination.name`, `messaging.rabbitmq.destination.routing_key`, `messaging.message.id` |
| Gauge | `litebus.transport.circuit_breaker.open` | `litebus.transport.broker=amqp` |
| Gauge | `litebus.transport.circuit_breaker.failure_count` | `litebus.transport.broker=amqp` |
| Histogram | `litebus.inbox.processor.dispatch_duration` | processor tags |
| Counters | `litebus.inbox.processor.succeeded` / `failed` / `dead_lettered` | processor tags |

Register transport signals with `AddLiteBusAmqpMetrics()` and processor signals with `AddLiteBusInboxMetrics()`.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToAmqpQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Amqp/`) |
| `ProcessPendingAsync_ShouldPublishToAmqpAndMarkPostgreSqlEnvelopeCompleted` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Amqp/`) |
| `TransportPublishRequest_Mandatory_ShouldDefaultToTrue` | `LiteBus.Transport.UnitTests` (`Amqp/`, `AmqpPublishRequestDefaultsTests`) |
| `PublishAsync_WithMandatoryAndNoBinding_ShouldThrow` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `PublishAsync_WithMandatoryFalseAndNoBinding_ShouldSucceed` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |

### Untested

- The explicit inbox dispatch failure path when the broker is unreachable.
- The custom route resolver and tenant routing strategy branches.

### Out-of-Scope

- Queue consumer ack and retry semantics.
- Exchange and binding provisioning lifecycle.

## References

- [Inbox](../../reliable-messaging/inbox.md)
- [Outbox](../../reliable-messaging/outbox.md)
- [AMQP transport guide](../../integrations/amqp.md)
- [Integration test guide](../../testing/integration-tests.md)
