# Outbox AMQP Dispatch

`outbox.UseAmqpDispatch(...)` from `LiteBus.Outbox.Dispatch.Amqp` publishes leased outbox envelopes to an AMQP 0.9.1 broker. Register the shared transport once at the root.

## When to Use It

Use transport dispatch when integration events leave the process and reach downstream consumers through RabbitMQ or LavinMQ. The outbox processor still owns leasing, retries, and dead-letter state.

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

`UseAmqpDispatch` requires the root AMQP transport module and only configures durable envelope routing. Register exactly one dispatcher per outbox: a second dispatcher registration throws `LiteBusConfigurationException`.

## Routing

| Outbox field | AMQP target |
|---|---|
| `Topic` | Route when set; the destination exchange comes from `DefaultDestination` |
| `ContractName` | Fallback route when `Topic` is unset |

Set `PublicationTarget` on `OutboxEnqueueMetadata` when enqueueing if each event type routes differently:

```csharp
OutboxEnqueueMetadata.Immediate with
{
    Target = new PublicationTarget.Topic("orders.order-submitted")
}
```

`PublicationTarget.Topic`, `PublicationTarget.Exchange`, and `PublicationTarget.Queue` each store their name on the envelope topic column. The dispatcher resolves the route from the tenant routing strategy when one is registered, otherwise from a configured `ResolveRoute` delegate, otherwise from the topic, otherwise from the contract name, and the AMQP publisher sends the destination as the exchange with the route as the routing key.

## Wire Format

The message body is the persisted JSON payload. Transport headers carry `litebus-message-id`, `litebus-contract-name`, `litebus-contract-version`, and optional correlation metadata.

## Failure Behavior

Publish failures throw to `PipelinedOutboxProcessor`. The processor records retry visibility or dead-letter state. Consumers must be idempotent.

See [AMQP Transport](amqp.md).
