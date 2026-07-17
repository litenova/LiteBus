# Outbox AMQP Dispatch

- **ID**: `dispatch.outbox.amqp`
- **Name**: Outbox AMQP dispatch
- **Maturity**: GA
- **Summary**: Publish leased outbox envelopes to RabbitMQ with LiteBus contract headers through `TransportOutboxDispatcher`.

## What It Does

`UseAmqpDispatch(configure, connectionOptions)` on `OutboxModuleBuilder` registers `TransportOutboxDispatchModule` with `AmqpTransportModule`. After domain logic enqueues events to outbox storage, the processor publishes to the configured exchange when rows are leased.

Route resolution prefers envelope `Topic`, then custom resolver, then contract name. Headers carry contract identity and trace context for downstream ingress.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Outbox.Dispatch.Amqp` | Registration glue |
| `LiteBus.Outbox.Dispatch` | Shared transport outbox dispatcher |
| `LiteBus.Transport.Amqp` | RabbitMQ client |

## Requires

- `dispatch.transport-core`
- `transport.amqp`
- `durable-core.outbox`

## Invariants

- Default hook failure policy is `CompleteDespiteHookFailure` for transport outbox modules.
- At-least-once publication: crash after broker ack but before terminal persist can duplicate messages.
- Downstream consumers must deduplicate (message id, idempotency header, or business keys).

## Non-Goals

- Does not subscribe to queues.
- Does not guarantee transactional outbox with broker (two-phase ack not built in).

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddOutboxModule(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseAmqpDispatch(
            options =>
            {
                options.DefaultDestination = "events.exchange";
                options.ResolveRoute = envelope => envelope.Topic ?? envelope.ContractName;
                options.Persistent = true;
            },
            new AmqpConnectionOptions
            {
                HostName = "localhost",
                Port = 5672,
                UserName = "guest",
                Password = "guest"
            });
    });
});
```

| API | Role |
| --- | --- |
| `OutboxModuleBuilder.UseAmqpDispatch(Action<TransportOutboxDispatcherOptions>, AmqpConnectionOptions)` | Registers outbox transport dispatcher and AMQP transport |
| `TransportOutboxDispatchModule.DefaultHookFailurePolicy` | Defaults to `CompleteDespiteHookFailure` |
| `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)` | Publishes outbox envelope to broker with canonical headers |

`TransportOutboxDispatcherOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `DefaultDestination` | `""` | Exchange name |
| `ContentType` | `application/json` | Outgoing MIME content type |
| `Persistent` | `true` | Durable message flag |
| `Mandatory` | `false` | Fail publish when unroutable |
| `ResolveRoute` | `null` | Route selector after topic fallback |
| `ValidatePayloadBeforeDispatch` | `false` | Deserialize payload before publish for validation |

Outbox route order is tenant strategy, then envelope `Topic`, then `ResolveRoute`, then contract name.

`AmqpConnectionOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `Uri` | `null` | Full AMQP URI, overrides host/port/credentials when set |
| `HostName` | `localhost` | Broker host |
| `Port` | `5672` | Broker port |
| `VirtualHost` | `/` | AMQP virtual host |
| `UserName` / `Password` | `guest` / `guest` | Broker credentials |
| `ClientProvidedName` | `null` | Connection name shown in broker UI |
| `AutomaticRecoveryEnabled` | `true` | Enables reconnect |
| `NetworkRecoveryInterval` | `00:00:05` | Delay between reconnect attempts |
| `CircuitBreaker` | configured object | Publish and connection breaker thresholds |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | Exchange, RabbitMQ routing key, and envelope id on the AMQP send path |
| `litebus.transport.circuit_breaker.*` | Broker tag `amqp` on shared transport meter |
| `litebus.outbox.processor.published` / `failed` / `dead_lettered` | After dispatch and terminal persist |
| `litebus.outbox.processor.dispatch_duration` | Includes AMQP publish latency |

## Deep Docs

- [Outbox.md](../../reliable-messaging/outbox.md)
- [Architecture.md](../../architecture/README.md)
- [Amqp-Transport.md](../../integrations/amqp.md)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `UseAmqpDispatch_WithAmqpTransportModule_ShouldRegisterTransportOutboxDispatcher` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`) |
| `OutboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) |
| `ProcessPendingAsync_ShouldPublishEnvelopeToAmqpQueue` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`) |
| `ProcessPendingAsync_WhenTopicMissing_ShouldUseContractNameAsRoutingKey` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`) |
| `ProcessPendingAsync_ShouldPublishToAmqpAndMarkPostgreSqlEnvelopePublished` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Outbox/Amqp/`) |

### Untested

- After-dispatch hook failure path with `CompleteDespiteHookFailure` under live AMQP.
- Outbox dispatch failure and circuit breaker behavior for AMQP dispatch projects.
- Route override via tenant strategy in AMQP-specific coverage.

### Out-of-Scope

- Subscribing to or consuming AMQP queues
- Two-phase transactional outbox with broker acknowledgment persisted before terminal store state
