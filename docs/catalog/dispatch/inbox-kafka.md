# Inbox Kafka Dispatch

## Header

- **ID**: `dispatch.inbox.kafka`
- **Name**: Inbox Kafka dispatch
- **Maturity**: GA
- **Summary**: Publish leased inbox envelopes to Kafka topics through `UseKafkaDispatch` with manual offset semantics on the consumer side.

## What It Does

Registers `TransportInboxDispatchModule` with `KafkaTransportModule`. `DefaultDestination` is the topic; route is the record key (defaults via contract name or custom resolver). Kafka transport uses manual offset commit on consume; dispatch publish does not commit offsets.

Pair Kafka dispatch with inbox idempotency and idempotent handlers. Kafka has no queue-style lease equivalent on the broker.

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

- Consumer offset commit happens on ingress/consume path, not dispatch.
- Failed consume records seek back for in-session retry (transport behavior).
- Handlers must tolerate redelivery after rebalance or uncommitted offsets.

## Non-Goals

- Does not implement Kafka transactions for consume-process-produce loops.
- Does not manage topic creation or schema registry.

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
| `InboxModuleBuilder.UseKafkaDispatch(Action<TransportInboxDispatcherOptions>)` | Registers inbox transport dispatcher that requires the root Kafka transport |
| `TransportInboxDispatcher.DispatchAsync(InboxEnvelope, CancellationToken)` | Publishes inbox lease payload and headers to Kafka |

`KafkaTransportOptions`:

| Property | Default | Role |
| --- | --- | --- |
| `BootstrapServers` | required | Kafka bootstrap server list |
| `ClientId` | `null` | Producer and consumer client id |
| `ConsumerGroupId` | `litebus-transport` | Group id for transport consumer side |
| `MessageTimeoutMs` | `null` | Producer local message timeout override |
| `SeekFailureBackoffInitial` | `00:00:00.250` | Initial seek retry delay |
| `SeekFailureBackoffMax` | `00:00:30` | Max seek retry delay |
| `SeekFailureBackoffMultiplier` | `2.0` | Seek retry multiplier |

## Observability

| Signal | Detail |
| --- | --- |
| `send {destination}` | Tags include topic as `messaging.destination.name`, record key as `messaging.kafka.message.key` |
| `litebus.transport.circuit_breaker.*` | Broker tag `kafka` |
| `process {destination}` | Not emitted on dispatch; consumer/ingress path only |
| `litebus.inbox.processor.*` | Standard inbox processor counters and dispatch duration histogram |

Register Kafka transport metrics through `AddLiteBusTransportMetrics()` from `LiteBus.Transport.Extensions.OpenTelemetry`.

## Deep Docs

- [Architecture.md: Kafka at-least-once semantics](../../architecture/README.md#kafka-at-least-once-semantics)

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `InboxDispatchExtensions_ShouldRegisterTransportDispatcher` | `LiteBus.Durable.IntegrationTests` (`Registration/`) (Kafka Theory case) |
| `ProcessPendingAsync_ShouldPublishLeasedEnvelopeToKafkaTopic` | `LiteBus.Durable.IntegrationTests` (`Dispatch/Inbox/Kafka/`) |

### Untested

- Inbox dispatch failure on unreachable brokers.
- Route override and tenant route strategy coverage.
- Kafka transaction usage, which is not part of current dispatcher contracts.

### Out-of-Scope

- Kafka transactions for consume-process-produce loops
- Topic creation and schema registry management
