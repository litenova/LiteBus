# Inbox AMQP Ingress

AMQP inbox ingress consumes commands from a broker queue and accepts them into the inbox store through `IInbox.AcceptAsync`. Pair it with inbox storage, one dispatcher, and `EnableInboxProcessor`.

RabbitMQ and LavinMQ both implement AMQP 0.9.1. `LiteBus.Transport.Amqp` connects to either broker.

## Flow

1. Upstream publisher sends JSON with LiteBus AMQP headers.
2. `TransportInboxIngressConsumer` runs the subscription loop.
3. `AmqpInboxIngressHandler` deserializes and calls `IInbox.AcceptAsync`.
4. Consumer acknowledges after store acceptance.
5. `PipelinedInboxProcessor` leases and dispatches through `IInboxDispatcher`.

## Registration

```csharp
builder.Services.AddLiteBus(builder =>
{
    builder.AddAmqpTransport(new AmqpConnectionOptions
    {
        Uri = new Uri(configuration.GetConnectionString("Amqp")!)
    });
    builder.AddMessaging(_ => { });
    builder.AddCommands(commands =>
        commands.RegisterFromAssembly(typeof(Program).Assembly));

    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
        inbox.UsePostgreSqlStorage(pg => pg.UseDataSource(dataSource));
        inbox.UseInProcessDispatch();
        inbox.UseAmqpIngress(ingress =>
        {
            ingress.UseOptions(new AmqpInboxIngressOptions
            {
                QueueName = "commands.inbox"
            });
        });
        inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(1));
    });
});
```

Ingress requires the root `AmqpTransportModule` registered by `AddAmqpTransport`. Broker connection settings belong to `AmqpConnectionOptions`, not to ingress options.

## Headers

Publishers must send `litebus-contract-name` and `litebus-contract-version`. Ingress defaults to broker-scoped idempotency from the AMQP `message-id` (or the `litebus-message-id` header when `Safety.TrustApplicationHeaders` is `true`) so redelivery after a successful accept does not create duplicate inbox rows.

Optional `litebus-idempotency-key`, `tenant-id`, `litebus-visible-after`, and `litebus-visible-after-delay` headers are ignored unless `AmqpInboxIngressOptions.Safety.TrustApplicationHeaders` is `true` on an authenticated broker binding. Correlation headers are always honored. Use `Safety.AuthorizeDeliveryAsync` to enforce tenant or contract policy before accept.

`Safety.MaxMessageBytes` caps the ingress body size (default 4 MiB). Oversize deliveries throw `InboxIngressException`, which the acknowledgement policy never requeues, so the delivery is discarded even when `RequeueOnFailure` is `true`.

## Inbox AMQP Patterns

Two AMQP patterns cover local execution and remote execution of commands:

| Direction | Package | Role |
| --- | --- | --- |
| Ingress (consume into inbox) | `LiteBus.Inbox.Ingress.Amqp` | Broker queue to `IInbox.AcceptAsync` |
| Dispatch (publish leased inbox) | `LiteBus.Inbox.Dispatch.Amqp` | `PipelinedInboxProcessor` to the explicitly registered root AMQP transport |

### Local Development Without a Broker

```csharp
inbox.UseInMemoryStorage();
inbox.UseInProcessDispatch();
```

### Remote Ingress, Local Execution

```text
External producer to AMQP queue to UseAmqpIngress to IInbox.AcceptAsync to store
  to PipelinedInboxProcessor to UseInProcessDispatch to ICommandMediator
```

### Remote Execution (Inbox as Outbound Command Bus)

```text
IInbox.AcceptAsync to store to PipelinedInboxProcessor to UseAmqpDispatch to AMQP exchange
  to remote service UseAmqpIngress to IInbox.AcceptAsync
```

See [Inbox](../reliable-messaging/inbox.md) and [AMQP Transport](amqp.md).
