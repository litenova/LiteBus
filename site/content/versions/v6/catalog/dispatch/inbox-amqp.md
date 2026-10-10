# Inbox AMQP Dispatch

- **ID**: `dispatch.inbox.amqp`
- **Name**: Inbox AMQP dispatch
- **Maturity**: GA
- **Summary**: Publishes leased inbox envelopes to RabbitMQ or LavinMQ through `TransportInboxDispatcher` and `AmqpTransportModule`.

## What It Does

`AddAmqpTransport(...)` registers the horizontal AMQP infrastructure once at the root. `InboxModuleBuilder.UseAmqpDispatch(...)` wires only `TransportInboxDispatchModule`, which requires that root module. The inbox processor keeps the durable lease and retry behavior, but the dispatch step publishes to AMQP instead of calling local command handlers.

Common deployment shape is a producer service with inbox storage and processor enabled, plus a separate worker service that consumes the queue and accepts into its own inbox. Because the processor acknowledges completion only after store persist, this path is at-least-once.

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
| `InboxModuleBuilder.UseAmqpDispatch(Action<TransportInboxDispatcherOptions>)` | Registers transport inbox dispatcher that requires the root AMQP transport |
| `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)` | Resolves route, maps headers, publishes through `ITransportPublisher` |

`TransportInboxDispatcherOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `DefaultDestination` | `""` | Exchange name (`""` targets default direct exchange) |
| `ContentType` | `application/json` | MIME value written to publish request |
| `Persistent` | `true` | Requests broker-persistent delivery |
| `Mandatory` | `false` | Fails publish when message is unroutable |
| `ResolveRoute` | `null` | Route override delegate per envelope |
| `ValidatePayloadBeforeDispatch` | `false` | Deserializes payload before publish to detect contract mismatch |

`AmqpConnectionOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `Uri` | `null` | Full AMQP URI, overrides host/port/credentials when set |
| `HostName` | `localhost` | Broker host name |
| `Port` | `5672` | Broker port |
| `VirtualHost` | `/` | AMQP virtual host |
| `UserName` / `Password` | `guest` / `guest` | Broker credentials |
| `ClientProvidedName` | `null` | Connection display name in broker UI |
| `AutomaticRecoveryEnabled` | `true` | Enables RabbitMQ client reconnect |
| `NetworkRecoveryInterval` | `00:00:05` | Delay between reconnect attempts |
| `CircuitBreaker` | configured object | Separate connection and per-exchange publisher breaker thresholds |

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
- `TransportInboxDispatchModule` throws at compose time when no `ITransportPublisher` is registered.
- Route resolution order is tenant strategy, then `ResolveRoute`, then `ContractName`.
- Processor semantics remain at-least-once, AMQP publish success alone does not mark completion.

## Non-Goals

- AMQP queue consumption and accept semantics (handled by `ingress.amqp`).
- Combining in-process and AMQP dispatch on the same inbox module.
- Broker-side queue topology management beyond publishing to configured destination and route.

## Observability

| Signal | Name | Tags |
| --- | --- | --- |
| Activity | `send {destination}` | `messaging.destination.name`, `messaging.rabbitmq.destination.routing_key`, `messaging.message.id` |
| Counter | `litebus.transport.circuit_breaker.open` | `litebus.transport.broker=amqp` |
| Counter | `litebus.transport.circuit_breaker.failure_count` | `litebus.transport.broker=amqp` |
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

### Untested

- Explicit inbox dispatch failure path when broker is unreachable.
- `Mandatory = true` unroutable publish behavior in integration tests.
- Custom route resolver and tenant routing strategy branches.

### Out-of-Scope

- Queue consumer ack and retry semantics.
- Exchange and binding provisioning lifecycle.

## Deep Docs

- [Inbox](../../reliable-messaging/inbox.md)
- [Outbox](../../reliable-messaging/outbox.md)
- [AMQP transport guide](../../integrations/amqp.md)
- [Integration test guide](../../testing/integration-tests.md)
