# Kafka Transport

**Production tier: GA** (transport platform and dispatch). **Kafka ingress is Beta**; see [v6 feature index](../reference/feature-index-v6.md).

`LiteBus.Transport.Kafka` wraps Confluent.Kafka for publish and consume. Inbox and outbox adapters compose it independently; installing Kafka transport does not pull AMQP or AWS SDKs.

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Transport.Kafka` | `IMessageTransport`, `KafkaConsumer`, connection options |
| `LiteBus.Inbox.Dispatch.Kafka` | Outbound command dispatch from inbox processor |
| `LiteBus.Outbox.Dispatch.Kafka` | Outbound event publish from outbox processor |
| `LiteBus.Inbox.Ingress.Kafka` | Broker intake into `IInbox.AcceptAsync` |

Add inbox/outbox core and storage packages as usual. See [Dependency graph](../architecture/dependency-graph.md).

## Registration

```csharp
builder.Modules.AddInboxModule(inbox =>
{
    inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
    inbox.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
    inbox.UseInProcessDispatch();

    inbox.UseKafkaDispatch(kafka => kafka.UseOptions(new KafkaTransportOptions
    {
        BootstrapServers = "localhost:9092"
    }));

    inbox.UseKafkaIngress(ingress =>
    {
        ingress.UseOptions(new KafkaInboxIngressOptions
        {
            Destination = "orders.commands",
            PrefetchCount = 10,
            Connection = new KafkaTransportOptions { BootstrapServers = "localhost:9092" },
            RequeueOnFailure = true
        });
    });

    inbox.EnableInboxProcessor();
});
```

## Options Reference

| Type | Property | Default | Notes |
| --- | --- | --- | --- |
| `KafkaTransportOptions` | `BootstrapServers` | required | Broker list |
| | `ConsumerGroupId` | `litebus-transport` | Consumer group for ingress |
| | `SeekFailureBackoffInitial` | 250 ms | Delay before re-read after seek |
| | `SeekFailureBackoffMax` | 30 s | Cap on seek backoff |
| | `SeekFailureBackoffMultiplier` | 2.0 | Per-offset failure multiplier |
| `TransportConsumerOptions` | `PrefetchCount` | adapter default | Max in-flight deliveries |
| `KafkaInboxIngressOptions` | `RequeueOnFailure` | `true` | Seek back on transient accept failure |

## Guarantees and Non-Guarantees

| Guaranteed | Not guaranteed |
| --- | --- |
| At-least-once delivery when handlers ack after successful accept/dispatch | Exactly-once side effects |
| Offset committed only after `TransportMessage.AcceptAsync` | In-session retry without seek on failure |
| Seek-on-failure rewinds to failed offset before next consume | Ordering across partitions |

On transient ingress failure, `ReturnToQueueAsync` seeks the consumer to the failed offset. The offset is not committed until accept succeeds. Handlers must be idempotent.

## Operations

| Symptom | Check | Action |
| --- | --- | --- |
| Same message reprocessed repeatedly | Consumer lag, seek backoff logs | Fix root accept/dispatch failure; verify idempotency |
| Consumer stuck on one offset | Repeated seek at same partition/offset | Inspect store availability; increase backoff cap if broker pressure |
| No ingress | Topic name, group id, ACLs | Confirm `Destination` topic exists; reset group if needed |

Monitor consumer lag, seek retry rate, and inbox/outbox row counts. See [Production runbook](../operations/runbook.md).

## Tests

| Scenario | Location |
| --- | --- |
| Seek on `ReturnToQueueAsync` | `LiteBus.Transport.UnitTests` |
| Ingress transient failure redelivery | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`) |
| Ingress and dispatch round-trip | `LiteBus.Durable.IntegrationTests` (`Ingress/Kafka/`, `Dispatch/Inbox/Kafka/`, `Dispatch/Outbox/Kafka/`) |

## Related Docs

* [AMQP transport](amqp.md)
* [AWS SQS transport](aws-sqs.md)
* [Azure Service Bus transport](azure-service-bus.md)
* [Inbox AMQP ingress](inbox-amqp-ingress.md) (ingress patterns shared across brokers)
* [Reliable messaging](../reliable-messaging/README.md)
* [Integration tests](../testing/integration-tests.md)
