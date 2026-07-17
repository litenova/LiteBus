# Outbox AMQP Dispatch

Publish leased outbox envelopes to an AMQP 0.9.1 broker through `outbox.UseAmqpDispatch(...)` from `LiteBus.Outbox.Dispatch.Amqp`. Register the shared transport once at the root.

## When to Use It

Use transport dispatch when integration events should leave the process and reach downstream consumers through RabbitMQ or LavinMQ. The outbox processor still owns leasing, retries, and dead-letter state.

For in-process replay into LiteBus event handlers, use `UseInProcessDispatch()` from `LiteBus.Outbox.Dispatch.InProcess`.

## Registration

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.AddAmqpTransport(new AmqpConnectionOptions
    {
        Uri = new Uri(configuration.GetConnectionString("Amqp")!)
    });
    builder.AddMessaging(_ => { });
    builder.AddOutbox(outbox =>
    {
        outbox.Contracts.Register<OrderSubmitted>("orders.order-submitted", 1);
        outbox.UseInMemoryStorage(); // or UsePostgreSqlStorage / UseEntityFrameworkCoreStorage
        outbox.UseAmqpDispatch(o => o.DefaultDestination = "orders.order-submitted");
        outbox.EnableOutboxProcessor();
    });
});
```

`UseAmqpDispatch` requires the root AMQP transport module and only configures durable envelope routing.

## Routing

| Outbox field | AMQP target |
|---|---|
| `Topic` | Routing key or destination when set |
| `ContractName` | Fallback routing metadata in transport headers |

Set `PublicationTarget` on `OutboxEnqueueMetadata` when enqueueing if each event type routes differently:

```csharp
OutboxEnqueueMetadata.Immediate with
{
    Target = new PublicationTarget.Topic("orders.order-submitted")
}
```

`PublicationTarget.Exchange` and `PublicationTarget.Queue` store explicit AMQP exchange or queue names on the envelope topic column for dispatchers that map them to broker targets.

## Wire Format

The message body is the persisted JSON payload. Transport headers carry `litebus-message-id`, `litebus-contract-name`, `litebus-contract-version`, and optional correlation metadata.

## Failure Behavior

Publish failures throw to `PipelinedOutboxProcessor`. The processor records retry visibility or dead-letter state. Consumers must be idempotent.

See [AMQP transport](amqp.md).
