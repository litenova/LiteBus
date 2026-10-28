# Outbox AMQP Dispatch

- **ID**: `dispatch.outbox.amqp`
- **Name**: Outbox AMQP dispatch
- **Maturity**: GA
- **Summary**: Publish leased outbox envelopes to RabbitMQ with LiteBus contract headers through `TransportOutboxDispatcher`.

## What It Does

`AddAmqpTransport(...)` registers `AmqpTransportModule` once at the root. `UseAmqpDispatch(configure)` on `OutboxModuleBuilder` registers `TransportOutboxDispatchModule` as a feature bridge to that transport. After domain logic enqueues events to outbox storage, the processor publishes them to the configured exchange when rows are leased.

Route resolution prefers the custom resolver, then the envelope `Topic`, then the contract name. Headers carry contract identity and trace context for downstream ingress.

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
- Publication is at-least-once. A crash after the broker acknowledges but before the terminal persist can duplicate messages.
- Downstream consumers must deduplicate by message id, idempotency header, or business keys.
- A mandatory publication the broker returns as unroutable throws `AmqpMessageReturnedException`, so the envelope stays leased and is retried instead of being silently dropped.

## Non-Goals

- It does not subscribe to queues.
- It does not guarantee a transactional outbox with the broker. Two-phase acknowledgment is not built in.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddAmqpTransport(new AmqpConnectionOptions
    {
        HostName = "localhost",
        Port = 5672,
        UserName = "guest",
        Password = "guest"
    });

    litebus.AddOutbox(outbox =>
    {
        outbox.EnableOutboxProcessor();
        outbox.UseAmqpDispatch(
            options =>
            {
                options.DefaultDestination = "events.exchange";
                options.ResolveRoute = envelope => envelope.Topic ?? envelope.ContractName;
                options.Persistent = true;
            });
    });
});
```

| API | Role |
| --- | --- |
| `OutboxModuleBuilder.UseAmqpDispatch(Action<TransportOutboxDispatcherOptions>)` | Registers the outbox transport dispatcher, which requires the root AMQP transport |
| `TransportOutboxDispatchModule.DefaultHookFailurePolicy` | The policy defaults to `CompleteDespiteHookFailure` |
| `TransportOutboxDispatcher.DispatchAsync(OutboxEnvelope, CancellationToken)` | Publishes the outbox envelope to the broker with canonical headers |

`TransportOutboxDispatcherOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `DefaultDestination` | `""` | Exchange name |
| `ContentType` | `application/json` | Outgoing MIME content type |
| `Persistent` | `true` | Durable message flag |
| `Mandatory` | `true` | Fails the publish with `AmqpMessageReturnedException` when the message is unroutable |
| `ResolveRoute` | `null` | Route selector. It wins over the envelope `Topic` and must return a non-empty route |
| `TenantRoutingStrategy` | `null` | Per-tenant destination override, registered as the `ITenantRoutingStrategy` that the dispatcher resolves |
| `ValidatePayloadBeforeDispatch` | `false` | Deserializes the payload before publish for validation |

On the outbox axis, the route order is the tenant strategy, then `ResolveRoute`, then the envelope `Topic`, then the contract name.

`AmqpConnectionOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `Uri` | `null` | Full AMQP URI that overrides the host, port, and credentials when set |
| `HostName` | `localhost` | Broker host |
| `Port` | `5672` | Broker port |
| `VirtualHost` | `/` | AMQP virtual host |
| `UserName` / `Password` | `guest` / `guest` | Broker credentials |
| `ClientProvidedName` | `null` | Connection name shown in the broker UI |
| `AutomaticRecoveryEnabled` | `true` | Enables reconnect |
| `NetworkRecoveryInterval` | `00:00:05` | Delay between reconnect attempts |
| `CircuitBreaker` | configured object | Separate thresholds for the connection breaker and the per-exchange publisher breakers |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | The exchange, the RabbitMQ routing key, and the envelope id on the AMQP send path |
| `litebus.transport.circuit_breaker.*` | Broker tag `amqp` on the shared transport meter |
| `litebus.outbox.processor.published` / `failed` / `dead_lettered` | After dispatch and the terminal persist |
| `litebus.outbox.processor.dispatch_duration` | It includes AMQP publish latency |

## References

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
| `TransportPublishRequest_Mandatory_ShouldDefaultToTrue` | `LiteBus.Transport.UnitTests` (`Amqp/`, `AmqpPublishRequestDefaultsTests`) |
| `PublishAsync_WithMandatoryAndNoBinding_ShouldThrow` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |
| `PublishAsync_WithMandatoryFalseAndNoBinding_ShouldSucceed` | `LiteBus.Transport.IntegrationTests` (`Amqp/`) |

### Untested

- The after-dispatch hook failure path with `CompleteDespiteHookFailure` under live AMQP.
- The outbox dispatch failure and circuit breaker behavior for the AMQP dispatch projects.
- Route override through the tenant strategy in AMQP-specific coverage.

### Out-of-Scope

- Subscribing to or consuming AMQP queues
- A two-phase transactional outbox that persists the broker acknowledgment before the terminal store state
