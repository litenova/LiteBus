# Inbox Kafka Dispatch

## Header

- **ID**: `dispatch.inbox.kafka`
- **Name**: Inbox Kafka dispatch
- **Maturity**: GA
- **Summary**: Publish leased inbox envelopes to Kafka topics through `UseKafkaDispatch` with manual offset semantics on the consumer side.

## What It Does

This extension registers `TransportInboxDispatchModule` with `KafkaTransportModule`. `DefaultDestination` is the topic, and the route is the record key. The key defaults to the contract name or comes from a custom resolver. The Kafka transport commits offsets manually on consume, and dispatch publish does not commit offsets.

Pair Kafka dispatch with inbox idempotency and idempotent handlers, because Kafka has no broker-side equivalent of a queue lease.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Dispatch.Kafka` | Registration glue |
| `LiteBus.Inbox.Dispatch` | Shared dispatcher |
| `LiteBus.Transport.Kafka` | Confluent adapter |

## Requires

- `dispatch.transport-core`
- `transport.kafka`
- `durable-core.inbox`

## Invariants

- The consumer commits offsets on the ingress or consume path, never on dispatch.
- Failed consume records seek back for an in-session retry. The transport layer owns that behavior.
- Handlers must tolerate redelivery after a rebalance or uncommitted offsets.

## Non-Goals

- It does not implement Kafka transactions for consume-process-produce loops.
- It does not manage topic creation or the schema registry.

## Public Surface

```csharp
services.AddLiteBus(litebus =>
{
    litebus.AddKafkaTransport(new KafkaTransportOptions
    {
        BootstrapServers = "localhost:9092",
        ClientId = "orders-inbox-dispatch"
    });

    litebus.AddInbox(inbox =>
    {
        inbox.EnableInboxProcessor();
        inbox.UseKafkaDispatch(
            options =>
            {
                options.DefaultDestination = "orders.commands";
                options.ResolveRoute = envelope => envelope.ContractName;
            });
    });
});
```

| API | Role |
| --- | --- |
| `InboxModuleBuilder.UseKafkaDispatch(Action<TransportInboxDispatcherOptions>)` | Registers the inbox transport dispatcher, which requires the root Kafka transport |
| `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)` | Publishes the inbox lease payload and headers to Kafka |

`KafkaTransportOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `BootstrapServers` | required | Kafka bootstrap server list |
| `ClientId` | `null` | Producer and consumer client id |
| `ConsumerGroupId` | `litebus-transport` | Group id for the transport consumer side |
| `MessageTimeoutMs` | `null` | Producer local message timeout override |
| `SocketTimeoutMs` | `null` | Positive socket timeout override, or the librdkafka default when unset |
| `MessageSendMaxRetries` | `null` | Positive producer send retry override, or the librdkafka default when unset |
| `SeekFailureBackoffInitial` | `00:00:00.250` | Initial seek retry delay |
| `SeekFailureBackoffMax` | `00:00:30` | Max seek retry delay |
| `SeekFailureBackoffMultiplier` | `2.0` | Seek retry multiplier |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | Tags include the topic as `messaging.destination.name` and the record key as `messaging.kafka.message.key` |
| `litebus.transport.circuit_breaker.*` | Broker tag `kafka` |
| `process {destination}` | Dispatch does not emit it. It appears on the consumer or ingress path only |
| `litebus.inbox.processor.*` | The standard inbox processor counters and dispatch duration histogram |

Register Kafka transport metrics through `AddLiteBusTransportMetrics()` from `LiteBus.Transport.Extensions.OpenTelemetry`.

## References

- [Architecture.md: Kafka at-least-once semantics](../../architecture/README.md#kafka-at-least-once-semantics)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) (Kafka Theory case) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToKafkaTopic` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Kafka/`) |

### Untested

- Inbox dispatch failure when brokers are unreachable.
- Route override and tenant route strategy coverage.
- Kafka transaction usage, which the current dispatcher contracts do not cover.

### Out-of-Scope

- Kafka transactions for consume-process-produce loops
- Topic creation and schema registry management
